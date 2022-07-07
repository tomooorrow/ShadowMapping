Shader "Assignment/genSSSM"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    	_Delta ("Delta", Float) = 0.01
    }
    SubShader
    {
        Tags { 
        	"RenderType"="Opapa" 
        	}

        Pass{
        	ZTest off 
			Fog { Mode Off }
			Cull back
			Lighting Off
			ZWrite Off
        	//Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile NONE PCF_ON VSM_ON

            #include "UnityCG.cginc"

            sampler2D _CameraDepthTexture;

            sampler2D _ShadowTex;
            float4 _ShadowTexScale;
            float4x4 _LightMatrix;

            float _Delta;//Bias
            float _ShadowIntensity;
            float _ShadowMapResolution;
            float4x4 _ViewPortRay;
            float4x4 _InverseVPMatrix;

            sampler2D _gShadowMapTexture0;
			//PCF
            int _FilterBoxLength;//PCF Filter
			//VSM
            float _VarianceShadowExpansion;
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
                
            };
			struct v2f
			{
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float4 wPos : TEXCOORD2;
			};
			
			v2f vert(appdata v)
			{
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv.xy;
				o.wPos = mul(unity_ObjectToWorld, v.vertex);
				return o;
				
			}
            float PCFSample(float depth, float2 uv){
            	//取当前 shading point 对应 shandow cam 记录深度图上的一定范围深度
            	//与该点在 shandow cam 坐标系下的深度进行比较，加权
                float shadow=0.0;
                for(int x=-_FilterBoxLength;x<=_FilterBoxLength;++x)
                {
                    for(int y=-_FilterBoxLength;y<=_FilterBoxLength;++y)
                    {
                        float4 samp = tex2D(_ShadowTex,uv+float2(x,y)/_ShadowMapResolution);
                    	float sDepth = samp.r;
                        shadow += sDepth < depth-0.01 ? 1 : 0 ;
                    }
                }
                return 1 -_ShadowIntensity * shadow/((_FilterBoxLength*2+1)*(_FilterBoxLength*2+1));
            }

            float VSMSample(float depth,float4 samp)
            {
                // https://www.gdcvault.com/play/1023808/Rendering-Antialiased-Shadows-with-Moment
                // https://developer.nvidia.com/gpugems/GPUGems3/gpugems3_ch08.html
                // The moments of the fragment live in "_shadowTex"
                float2 s = samp.rg;

                // average / expected depth and depth^2 across the texels
                // E(x) and E(x^2)
                float x = s.r; 
                float x2 = s.g;
                
                // calculate the variance of the texel based on
                // the formula var = E(x^2) - E(x)^2
                float var = x2 - x*x; 

                // calculate our initial probability based on the basic depths
                // if our depth is closer than x, then the fragment has a 100%
                // probability of being lit (p=1)
                float p = depth <= x;
                
                // calculate the upper bound of the probability using Chebyshev's inequality
                float delta = depth - x;
                float p_max = var / (var + delta*delta);

                // To alleviate the light bleeding, expand the shadows to fill in the gaps
                float amount = _VarianceShadowExpansion;
                p_max = clamp( (p_max - amount) / (1 - amount), 0, 1);

                return  1-_ShadowIntensity*(1 - max(p, p_max));
            }
			
			
			fixed4 frag(v2f i) : SV_Target
			{

				float depthTextureValue = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);

				depthTextureValue = 1 - depthTextureValue;

				float4 ndc = float4(i.uv.x * 2 - 1, i.uv.y * 2 - 1, depthTextureValue*2-1 , 1);
				
				float4 worldPos = mul(_InverseVPMatrix, ndc);
				worldPos /= worldPos.w;
				//return worldPos;
				float4 lightSpacePos = mul(_LightMatrix, worldPos);

                float depth = lightSpacePos.z / _ShadowTexScale.z;
                //return depth;
                float2 uv = lightSpacePos.xy;
                uv += _ShadowTexScale.xy / 2;
                uv /= _ShadowTexScale.xy;
                
                float4 samp = tex2D(_ShadowTex, uv);
				float3 v = 1- _ShadowIntensity * step( samp.r, depth - _Delta);
				
#ifdef PCF_ON
				v = PCFSample(depth, uv);
#endif
				
#ifdef VSM_ON
				v = VSMSample(depth, samp);
#endif				
				return float4(v.rgb, 1) ;

			}  
            ENDCG
        }
    }
}
