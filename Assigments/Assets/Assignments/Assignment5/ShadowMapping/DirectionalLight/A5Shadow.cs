using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class A5Shadow : MonoBehaviour
{
    Shader _depthShader;
    
    Camera _shadowCam;
    
    //SM
    RenderTexture _depthTexture;

    //[Range(0,1)]
    //public float _bias = 0.01f;

    
    public int _resolution = 1024;

    public FilterMode _filterMode = FilterMode.Bilinear;
    
    [Range(0,1)]
    public float shadowIntensity = 0.43f;
    
    public bool sm = false; 
    
    
    //Blur
    public ComputeShader _blur;
    RenderTexture _blurDepthTexture;
    public int blurIterations = 1;

    //PCF
    public bool pcf_on = false; 
    [Range(0,30)]
    public int _filterBoxLength = 4;
    
    //VSM
    public bool vsm_on = false; 
    
    [Range(0,1)]
    public float varianceShadowExpansion = 0.97f;

    //SSSM
    Camera _mainCam;
    Camera _sssmCam;
    Shader _getSSSMShader;
    RenderTexture _ssShadowMap0;
    RenderTexture _ssShadowMap1;
    public bool sssm = false; 
    // Start is called before the first frame update
    void Start()
    {
        
    }

    void OnEnable()
    {
        _depthShader = _depthShader ? _depthShader : Shader.Find("Assignment/Depth"); 
        _getSSSMShader = _getSSSMShader ? _getSSSMShader : Shader.Find("Assignment/genSSSM"); 
    }

    // Update is called once per frame
    void Update()
    {
            UpdateRenderTexture();
            UpdateShadowCamera();
            UpdateShaderValues();
            Blur();
    }

    void Blur()
    {
        if (!_blur) return;
        for (int i = 0; i < blurIterations; i++)
        {
            _blur.SetTexture(0, "Read", _depthTexture);
            _blur.SetTexture(0, "Result", _blurDepthTexture);
            _blur.Dispatch(0, _depthTexture.width / 8, _depthTexture.height / 8, 1);

            Swap(ref _blurDepthTexture, ref _depthTexture);
        }
    }
    void Swap<T>(ref T a, ref T b)
    {
        T temp = a;
        a = b;
        b = temp;
    }
    void SetUpShadowCam(){
        
            if (_shadowCam) return;
        
            GameObject go = new GameObject("shadow cam"); 
            go.hideFlags = HideFlags.DontSave; 

            _shadowCam = go.AddComponent<Camera>();
            _shadowCam.orthographic = true;
            _shadowCam.nearClipPlane = 0;
            //_shadowCam.farClipPlane = 40;//动态调节
            _shadowCam.enabled = false;
            _shadowCam.backgroundColor = new Color(0, 0, 0, 0);
            _shadowCam.clearFlags = CameraClearFlags.SolidColor;
    }
    
    void SetUpSSSMCam(){
        
        if (_sssmCam) return;
        
        GameObject go = new GameObject("sssm cam"); 
        go.hideFlags = HideFlags.DontSave; 

        _sssmCam = go.AddComponent<Camera>();
        _sssmCam.orthographic = true;
        _sssmCam.nearClipPlane = 0;
        //_shadowCam.farClipPlane = 40;//动态调节
        _sssmCam.enabled = false;
        _sssmCam.backgroundColor = new Color(0, 0, 0, 0);
        _sssmCam.clearFlags = CameraClearFlags.SolidColor;
    }

    void SetUpMainCam()
    {
        if (_mainCam) return;
        _mainCam = GameObject.Find("Main Camera").GetComponent<Camera>() ;
        _mainCam.depthTextureMode = DepthTextureMode.Depth;
    }

    void UpdateRenderTexture(){
        
            if (_depthTexture == null)
            {
                _depthTexture = createRenderTexture();
            }
            if (_blurDepthTexture == null)
            {
                _blurDepthTexture = createRenderTexture();
            }

            if (_ssShadowMap0 == null)
            {
                _ssShadowMap0 = createRenderTexture();
            }
            if (_ssShadowMap1 == null)
            {
                _ssShadowMap1 = createRenderTexture();
            }

            //面板更改resolution时重新创建shadow map
            if (_depthTexture != null && (_depthTexture.width != _resolution || _depthTexture.filterMode!= _filterMode ))
            {
                DestroyImmediate(_depthTexture);
                _depthTexture = null;
            }
    }

    RenderTexture createRenderTexture()
    {
        RenderTexture tg = new RenderTexture(_resolution, _resolution, 24, RenderTextureFormat.RGFloat);
        tg.filterMode = _filterMode;
        tg.wrapMode = TextureWrapMode.Clamp;
        tg.enableRandomWrite = true;
        tg.Create();

        return tg;
    }

    void UpdateShadowCamera()
    {

        SetUpMainCam();
        
        SetUpShadowCam();

        SetUpSSSMCam();

        
        Camera cam = _shadowCam;
        Light l = FindObjectOfType<Light>();
        
        cam.transform.position = l.transform.position;
        cam.transform.rotation = l.transform.rotation;
        cam.transform.LookAt(cam.transform.position + cam.transform.forward, cam.transform.up);
        
        //让深度相机始终处于包围渲染的物体的状态
        Vector3 center, extents;
        List<Renderer> renderers = new List<Renderer>();
        renderers.AddRange(FindObjectsOfType<Renderer>());

        GetRenderersExtents(renderers, cam.transform, out center, out extents);

        center.z -= extents.z / 2;
        cam.transform.position = cam.transform.TransformPoint(center);
        cam.nearClipPlane = 0;
        cam.farClipPlane = extents.z;

        cam.aspect = extents.x / extents.y;
        cam.orthographicSize = extents.y / 2;
        
        cam.targetTexture = _depthTexture;
        cam.RenderWithShader(_depthShader, "");
        
        Camera cam1 = _sssmCam;
        cam1.CopyFrom(_mainCam);
        cam1.targetTexture = _ssShadowMap0;
        cam1.RenderWithShader(_depthShader, "");
        
    }
    void GetRenderersExtents(List<Renderer> renderers, Transform frame, out Vector3 center, out Vector3 extents)
    {
        Vector3[] arr = new Vector3[8];

        Vector3 min = Vector3.one * Mathf.Infinity;
        Vector3 max = Vector3.one * Mathf.NegativeInfinity;
        foreach (var r in renderers)
        {
            GetBoundsPoints(r.bounds, arr, frame.worldToLocalMatrix);

            foreach(var p in arr)
            {
                for(int i = 0; i < 3; i ++)
                {
                    min[i] = Mathf.Min(p[i], min[i]);
                    max[i] = Mathf.Max(p[i], max[i]);
                }
            }
        }

        extents = max - min;
        center = (max + min) / 2;
    }
    
    void GetBoundsPoints(Bounds b, Vector3[] points, Matrix4x4? mat = null)
    {
        Matrix4x4 trans = mat ?? Matrix4x4.identity;

        int count = 0;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 v = b.extents;
            v.x *= x;
            v.y *= y;
            v.z *= z;
            v += b.center;
            v = trans.MultiplyPoint(v);

            points[count++] = v;
        }
    }

    void UpdateShaderValues(){
        //Shader.SetGlobalFloat("_Delta", _bias);
        Shader.SetGlobalTexture("_ShadowTex", _depthTexture);
        Shader.SetGlobalMatrix("_LightMatrix", _shadowCam.transform.worldToLocalMatrix);
        Shader.SetGlobalFloat("_ShadowMapResolution", _resolution);
        Shader.SetGlobalFloat("_VarianceShadowExpansion", varianceShadowExpansion);
        Shader.SetGlobalFloat("_FilterBoxLength",_filterBoxLength);
        Shader.SetGlobalFloat("_ShadowIntensity",shadowIntensity);
        Shader.SetGlobalVector("_LightPos",_shadowCam.transform.position);
        //Shader.SetGlobalMatrix("_TestMatrix",GL.GetGPUProjectionMatrix(_shadowCam.projectionMatrix, false)*_shadowCam.worldToCameraMatrix);
        
        if(sm) {
            Shader.EnableKeyword("SM");}
        else{
            Shader.DisableKeyword("SM");
        }
        if (pcf_on && ( sm || sssm )) {
            Shader.EnableKeyword("PCF_ON");}
        else{
            Shader.DisableKeyword("PCF_ON");
        } 
        if(vsm_on && ( sm || sssm )) {
            Shader.EnableKeyword("VSM_ON");}
        else{
            Shader.DisableKeyword("VSM_ON");
        }
        if(sssm) {
            Shader.EnableKeyword("SSSM");}
        else{
            Shader.DisableKeyword("SSSM");
        }
        
        Matrix4x4 projectionMatrix = GL.GetGPUProjectionMatrix(_shadowCam.projectionMatrix, false);
        projectionMatrix = (projectionMatrix * _shadowCam.worldToCameraMatrix);
        Shader.SetGlobalMatrix("_TestmMatrix", projectionMatrix);
        
        Vector4 size = Vector4.zero;
        size.y = _shadowCam.orthographicSize * 2;
        size.x = _shadowCam.aspect * size.y;
        size.z = _shadowCam.farClipPlane;
        size.w = 1.0f / _resolution;
        Shader.SetGlobalVector("_ShadowTexScale", size);
        
        Matrix4x4 vpMatrix = _mainCam.projectionMatrix * _mainCam.worldToCameraMatrix;
        Shader.SetGlobalMatrix("_InverseVPMatrix", vpMatrix.inverse);//通过深度图重建世界坐标，视口射线插值方式
        
        Graphics.Blit(_ssShadowMap0, _ssShadowMap1, new Material(_getSSSMShader));
        Shader.SetGlobalTexture("_SSShadowMap", _ssShadowMap1);
        
    }

    void OnDisable()
    {
        Debug.Log("Disable!");
        Shader.DisableKeyword("PCF_ON");
        Shader.DisableKeyword("VSM_ON");
        Shader.DisableKeyword("SSSM");
        Shader.DisableKeyword("SM");
        OnDestroy();
    }

    void OnDestroy()
    {
        if (_shadowCam)
        {
            DestroyImmediate(_shadowCam.gameObject); 
            _shadowCam = null;
        }
        
        if (_sssmCam)
        {
            DestroyImmediate(_sssmCam.gameObject); 
            _sssmCam = null;
        }

        if (_depthTexture)
        {
            DestroyImmediate(_depthTexture);
            _depthTexture = null;
        }

        if (_blurDepthTexture)
        {
            DestroyImmediate(_blurDepthTexture);
            _blurDepthTexture = null;
        }

        if (_ssShadowMap0)
        {
            DestroyImmediate(_ssShadowMap0);
            _ssShadowMap0 = null;
        }
        if (_ssShadowMap1)
        {
            DestroyImmediate(_ssShadowMap1);
            _ssShadowMap1 = null;
        }
    }
}
