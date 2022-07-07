using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class A5CascadedSM : MonoBehaviour
{
    Shader _depthShader;
    
    Camera _shadowCam;
    
    Light _dirLight;
    
    RenderTexture[] depthTextures = new RenderTexture[4];

    public int _resolution = 1024;
    
    public FilterMode _filterMode = FilterMode.Bilinear;

    [Range(0,1)]
    public float shadowIntensity = 0.43f;

    private Matrix4x4 biasMatrix = Matrix4x4.identity;

    List<Matrix4x4> world2ShadowMats = new List<Matrix4x4>(4);
    
    GameObject[] _shadowCamSplits = new GameObject[4];
    
    public bool debug_mode = false;

    //PCF
    public bool csm_pdf_on = false;
    [Range(0,30)]
    public int _filterBoxLength = 4;

    //VSM
    public bool csm_vsm_on = false;
    [Range(0,1)]
    public float varianceShadowExpansion = 0.97f;
    void Awake()
    {
        biasMatrix.SetRow(0, new Vector4(0.5f, 0, 0, 0.5f));
        biasMatrix.SetRow(1, new Vector4(0, 0.5f, 0, 0.5f));
        biasMatrix.SetRow(2, new Vector4(0, 0, 0.5f, 0.5f));
        biasMatrix.SetRow(3, new Vector4(0, 0, 0, 1f));
        InitFrustumCorners();
    }

    // Use this for initialization
    void Start()
    {
    }

    private void OnEnable()
    {
        _depthShader = _depthShader ? _depthShader : Shader.Find("Assignment/CSM/A5Depth");
        _dirLight = _dirLight ? _dirLight : FindObjectOfType<Light>();
    }

    private void Update()
    {
        UpdateRenderTexture();
        UpdateShadowCamera();
        UpdateShaderValues();
    }

    void UpdateShaderValues()
    {
        Shader.SetGlobalFloat("_gShadowBias", 0.005f);
        Shader.SetGlobalFloat("_gShadowStrength", 0.5f);
        Shader.SetGlobalMatrixArray("_gWorld2Shadow", world2ShadowMats);
        Shader.SetGlobalFloat("_ShadowIntensity",shadowIntensity);
        Shader.SetGlobalFloat("_FilterBoxLength",_filterBoxLength);
        Shader.SetGlobalFloat("_VarianceShadowExpansion", varianceShadowExpansion);

        Shader.EnableKeyword("CSM");
        
        if (csm_pdf_on) {
            Shader.EnableKeyword("CSM_PDF_ON");}
        else{
            Shader.DisableKeyword("CSM_PDF_ON");
        } 
        if (debug_mode) {
            Shader.EnableKeyword("DEBUG_MODE");}
        else{
            Shader.DisableKeyword("DEBUG_MODE");
        }
        if (csm_vsm_on) {
            Shader.EnableKeyword("CSM_VSM_ON");}
        else{
            Shader.DisableKeyword("CSM_VSM_ON");
        }

    }

    void UpdateShadowCamera()
    {
        SetUpShadowCam();
        CalcMainCameraSplitsFrustumCorners();
        CalcLightCameraSplitsFrustum();
        
        if (_dirLight)
        {
            if (!_shadowCam)
            {
                CreateRenderTexture();
            }

            world2ShadowMats.Clear();
            for (int i = 0; i < 4; i++)
            {
                ConstructLightCameraSplits(i);

                _shadowCam.targetTexture = depthTextures[i];
                _shadowCam.RenderWithShader(_depthShader, "");

                Matrix4x4 projectionMatrix = GL.GetGPUProjectionMatrix(_shadowCam.projectionMatrix, false);
                world2ShadowMats.Add(projectionMatrix * _shadowCam.worldToCameraMatrix);
            }
        }
    }

    void UpdateRenderTexture()
    {
        if (depthTextures[0] == null)
        {
            CreateRenderTexture();
        }
        if (depthTextures[0] != null && (depthTextures[0].width != _resolution || depthTextures[0].filterMode!= _filterMode ))
        {
            for (int i = 0; i < depthTextures.Length; i++)
            {
                DestroyImmediate(depthTextures[i]);
                depthTextures[i] = null;
            }
        }
    }

    private void CreateRenderTexture()
    {
        RenderTextureFormat rtFormat = RenderTextureFormat.Default;
        if (!SystemInfo.SupportsRenderTextureFormat(rtFormat))
            rtFormat = RenderTextureFormat.Default;

        for (int i = 0; i < 4; i++)
        {
            depthTextures[i] = new RenderTexture(_resolution, _resolution, 24, rtFormat);
            depthTextures[i].filterMode = _filterMode;
            depthTextures[i].wrapMode = TextureWrapMode.Clamp;
            depthTextures[i].enableRandomWrite = true;
            depthTextures[i].Create();
            Shader.SetGlobalTexture("_gShadowMapTexture" + i, depthTextures[i]);
        }
    }

    void SetUpShadowCam()
    {
        if (_shadowCam) return;
        GameObject go = new GameObject("Directional Light Camera");
        _shadowCam = go.AddComponent<Camera>();

        //LightCamera.cullingMask = 1 << LayerMask.NameToLayer("Caster");
        _shadowCam.backgroundColor = Color.white;
        _shadowCam.clearFlags = CameraClearFlags.SolidColor;
        _shadowCam.orthographic = true;
        _shadowCam.enabled = false;

        for (int i = 0; i < 4; i++)
        {
            _shadowCamSplits[i] = new GameObject("_shadowCamSplits" + i);
        }

    }

    

    float[] _LightSplitsNear;
    float[] _LightSplitsFar;

    struct FrustumCorners
    {
        public Vector3[] nearCorners;
        public Vector3[] farCorners;
    }

    FrustumCorners[] mainCamera_Splits_fcs;
    FrustumCorners[] lightCamera_Splits_fcs;

    void InitFrustumCorners()
    {
        mainCamera_Splits_fcs = new FrustumCorners[4];
        lightCamera_Splits_fcs = new FrustumCorners[4];
        for (int i = 0; i < 4; i++)
        {
            mainCamera_Splits_fcs[i].nearCorners = new Vector3[4];
            mainCamera_Splits_fcs[i].farCorners = new Vector3[4];

            lightCamera_Splits_fcs[i].nearCorners = new Vector3[4];
            lightCamera_Splits_fcs[i].farCorners = new Vector3[4];
        }
    }

    void CalcMainCameraSplitsFrustumCorners()
    {
        float near = Camera.main.nearClipPlane;
        float far = Camera.main.farClipPlane;

        float[] nears = { near, far * 0.067f + near, far * 0.133f + far * 0.067f + near, far * 0.267f + far * 0.133f + far * 0.067f + near };
        float[] fars = { far * 0.067f + near, far * 0.133f + far * 0.067f + near, far * 0.267f + far * 0.133f + far * 0.067f + near, far };

        _LightSplitsNear = nears;
        _LightSplitsFar = fars;

        Shader.SetGlobalVector("_gLightSplitsNear", new Vector4(_LightSplitsNear[0], _LightSplitsNear[1], _LightSplitsNear[2], _LightSplitsNear[3]));
        Shader.SetGlobalVector("_gLightSplitsFar", new Vector4(_LightSplitsFar[0], _LightSplitsFar[1], _LightSplitsFar[2], _LightSplitsFar[3]));

        for (int k = 0; k < 4; k++)
        {
            Camera.main.CalculateFrustumCorners(new Rect(0, 0, 1, 1), _LightSplitsNear[k], Camera.MonoOrStereoscopicEye.Mono, mainCamera_Splits_fcs[k].nearCorners);
            for (int i = 0; i < 4; i++)
            {
                mainCamera_Splits_fcs[k].nearCorners[i] = Camera.main.transform.TransformPoint(mainCamera_Splits_fcs[k].nearCorners[i]);
            }

            Camera.main.CalculateFrustumCorners(new Rect(0, 0, 1, 1), _LightSplitsFar[k], Camera.MonoOrStereoscopicEye.Mono, mainCamera_Splits_fcs[k].farCorners);
            for (int i = 0; i < 4; i++)
            {
                mainCamera_Splits_fcs[k].farCorners[i] = Camera.main.transform.TransformPoint(mainCamera_Splits_fcs[k].farCorners[i]);
            }
        }
    }

    void CalcLightCameraSplitsFrustum()
    {
        if (_shadowCam == null)
            return;

        for (int k = 0; k < 4; k++)
        {
            for (int i = 0; i < 4; i++)
            {
                lightCamera_Splits_fcs[k].nearCorners[i] = _shadowCamSplits[k].transform.InverseTransformPoint(mainCamera_Splits_fcs[k].nearCorners[i]);
                lightCamera_Splits_fcs[k].farCorners[i] = _shadowCamSplits[k].transform.InverseTransformPoint(mainCamera_Splits_fcs[k].farCorners[i]);
            }

            float[] xs = { lightCamera_Splits_fcs[k].nearCorners[0].x, lightCamera_Splits_fcs[k].nearCorners[1].x, lightCamera_Splits_fcs[k].nearCorners[2].x, lightCamera_Splits_fcs[k].nearCorners[3].x,
                       lightCamera_Splits_fcs[k].farCorners[0].x, lightCamera_Splits_fcs[k].farCorners[1].x, lightCamera_Splits_fcs[k].farCorners[2].x, lightCamera_Splits_fcs[k].farCorners[3].x };

            float[] ys = { lightCamera_Splits_fcs[k].nearCorners[0].y, lightCamera_Splits_fcs[k].nearCorners[1].y, lightCamera_Splits_fcs[k].nearCorners[2].y, lightCamera_Splits_fcs[k].nearCorners[3].y,
                       lightCamera_Splits_fcs[k].farCorners[0].y, lightCamera_Splits_fcs[k].farCorners[1].y, lightCamera_Splits_fcs[k].farCorners[2].y, lightCamera_Splits_fcs[k].farCorners[3].y };

            float[] zs = { lightCamera_Splits_fcs[k].nearCorners[0].z, lightCamera_Splits_fcs[k].nearCorners[1].z, lightCamera_Splits_fcs[k].nearCorners[2].z, lightCamera_Splits_fcs[k].nearCorners[3].z,
                       lightCamera_Splits_fcs[k].farCorners[0].z, lightCamera_Splits_fcs[k].farCorners[1].z, lightCamera_Splits_fcs[k].farCorners[2].z, lightCamera_Splits_fcs[k].farCorners[3].z };

            float minX = Mathf.Min(xs);
            float maxX = Mathf.Max(xs);

            float minY = Mathf.Min(ys);
            float maxY = Mathf.Max(ys);

            float minZ = Mathf.Min(zs);
            float maxZ = Mathf.Max(zs);

            lightCamera_Splits_fcs[k].nearCorners[0] = new Vector3(minX, minY, minZ);
            lightCamera_Splits_fcs[k].nearCorners[1] = new Vector3(maxX, minY, minZ);
            lightCamera_Splits_fcs[k].nearCorners[2] = new Vector3(maxX, maxY, minZ);
            lightCamera_Splits_fcs[k].nearCorners[3] = new Vector3(minX, maxY, minZ);

            lightCamera_Splits_fcs[k].farCorners[0] = new Vector3(minX, minY, maxZ);
            lightCamera_Splits_fcs[k].farCorners[1] = new Vector3(maxX, minY, maxZ);
            lightCamera_Splits_fcs[k].farCorners[2] = new Vector3(maxX, maxY, maxZ);
            lightCamera_Splits_fcs[k].farCorners[3] = new Vector3(minX, maxY, maxZ);

            Vector3 pos = lightCamera_Splits_fcs[k].nearCorners[0] + (lightCamera_Splits_fcs[k].nearCorners[2] - lightCamera_Splits_fcs[k].nearCorners[0]) * 0.5f;


            _shadowCamSplits[k].transform.position = _shadowCamSplits[k].transform.TransformPoint(pos);
            _shadowCamSplits[k].transform.rotation = _dirLight.transform.rotation;

        }
    }

    void ConstructLightCameraSplits(int k)
    {
        _shadowCam.transform.position = _shadowCamSplits[k].transform.position;
        _shadowCam.transform.rotation = _shadowCamSplits[k].transform.rotation;

        _shadowCam.nearClipPlane = lightCamera_Splits_fcs[k].nearCorners[0].z;
        _shadowCam.farClipPlane = lightCamera_Splits_fcs[k].farCorners[0].z;

        _shadowCam.aspect = Vector3.Magnitude(lightCamera_Splits_fcs[k].nearCorners[0] - lightCamera_Splits_fcs[k].nearCorners[1]) / Vector3.Magnitude(lightCamera_Splits_fcs[k].nearCorners[1] - lightCamera_Splits_fcs[k].nearCorners[2]);
        _shadowCam.orthographicSize = Vector3.Magnitude(lightCamera_Splits_fcs[k].nearCorners[1] - lightCamera_Splits_fcs[k].nearCorners[2]) * 0.5f;
    }

    void OnDrawGizmos()
    {
        if (!debug_mode)
            return;
        if (_shadowCam == null)
            return;

        FrustumCorners[] fcs = new FrustumCorners[4];
        for (int k = 0; k < 4; k++)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(mainCamera_Splits_fcs[k].nearCorners[1], mainCamera_Splits_fcs[k].nearCorners[2]);

            fcs[k].nearCorners = new Vector3[4];
            fcs[k].farCorners = new Vector3[4];

            for (int i = 0; i < 4; i++)
            {
                fcs[k].nearCorners[i] = _shadowCamSplits[k].transform.TransformPoint(lightCamera_Splits_fcs[k].nearCorners[i]);
                fcs[k].farCorners[i] = _shadowCamSplits[k].transform.TransformPoint(lightCamera_Splits_fcs[k].farCorners[i]);
            }

            Gizmos.color = Color.red;
            Gizmos.DrawLine(fcs[k].nearCorners[0], fcs[k].nearCorners[1]);
            Gizmos.DrawLine(fcs[k].nearCorners[1], fcs[k].nearCorners[2]);
            Gizmos.DrawLine(fcs[k].nearCorners[2], fcs[k].nearCorners[3]);
            Gizmos.DrawLine(fcs[k].nearCorners[3], fcs[k].nearCorners[0]);

            Gizmos.color = Color.green;
            Gizmos.DrawLine(fcs[k].farCorners[0], fcs[k].farCorners[1]);
            Gizmos.DrawLine(fcs[k].farCorners[1], fcs[k].farCorners[2]);
            Gizmos.DrawLine(fcs[k].farCorners[2], fcs[k].farCorners[3]);
            Gizmos.DrawLine(fcs[k].farCorners[3], fcs[k].farCorners[0]);

            Gizmos.DrawLine(fcs[k].nearCorners[0], fcs[k].farCorners[0]);
            Gizmos.DrawLine(fcs[k].nearCorners[1], fcs[k].farCorners[1]);
            Gizmos.DrawLine(fcs[k].nearCorners[2], fcs[k].farCorners[2]);
            Gizmos.DrawLine(fcs[k].nearCorners[3], fcs[k].farCorners[3]);
        }
    }
    private void OnDisable()
    {
        Shader.DisableKeyword("CSM");
        OnDestroy();
    }

    void OnDestroy()
    {
        debug_mode = false;

        if (_shadowCam)
        {
            DestroyImmediate(_shadowCam.gameObject); 
            _shadowCam = null;
        }

        for (int i = 0; i < 4; i++)
        {
            if (depthTextures[i])
            {
                DestroyImmediate(depthTextures[i]);
                depthTextures[i] = null;
            }

            if (_shadowCamSplits[i])
            {
                DestroyImmediate(_shadowCamSplits[i]);
                _shadowCamSplits[i] = null;
            }
        }
        
        
    }
}
