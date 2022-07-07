Shader "Assignment/A5Shadowed"
{
    Properties{
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Delta ("Delta", Float) = 0.001
    }
	
    SubShader{
        
    	Tags { "RenderType"="Opaque" }
        
    	Lighting On
    	
        Pass{
        	
	        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile NONE PCF_ON VSM_ON CSM SM 
            #pragma multi_compile NONE1 CSM_PDF_ON DEBUG_MODE CSM_VSM_ON SSSM

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
                
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 wPos : TEXCOORD1;
                float3 normal_object : TEXCOORD2;
                float depth : TEXCOORD3;
                float4 screenPos : TEXCOORD4;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            
			//Shadow Mapping
            sampler2D _ShadowTex;
            float4 _ShadowTexScale;
            float4x4 _LightMatrix;
            //float4x4 _TestMatrix;

            float _Delta;//Bias
            float _ShadowIntensity;
            float _ShadowMapResolution;
            float _VarianceShadowExpansion;

            int _FilterBoxLength;//PCF Filter 

            float3 _LightPos;//For test. from c#

            //CSM
            sampler2D _gShadowMapTexture0;
			sampler2D _gShadowMapTexture1;
			sampler2D _gShadowMapTexture2;
			sampler2D _gShadowMapTexture3;

            float4 _gLightSplitsNear;
			float4 _gLightSplitsFar;
            
            float4x4 _gWorld2Shadow[4];//4 matrix

			sampler2D _CameraDepthTexture;
	        float4x4 _InverseVPMatrix;

	        //SSSM
	        sampler2D _SSShadowMap;

	        float4 _ShadowTexScale_SSSM;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.wPos = mul(unity_ObjectToWorld, v.vertex);
                o.normal_object = v.normal;
            	o.screenPos = ComputeScreenPos(o.vertex);

                COMPUTE_EYEDEPTH(o.depth);
                return o;
            }

            //Color
            float3 CalculateLight(float4 wVertex, float3 normal)
            {
                wVertex = mul(unity_WorldToObject, wVertex);
                float3 viewpos = -mul(UNITY_MATRIX_MV, wVertex).xyz;

                // View vector, light direction, and Normal in model-view space
                float3 toLight = unity_LightPosition[0].xyz; //Lighting ON
                //float3 toLight = mul(UNITY_MATRIX_MV, _LightPos).xyz - wVertex;
                float3 L = normalize(toLight);
                float3 V = normalize(viewpos);//float3(0, 0, 1);
                float3 N = mul(UNITY_MATRIX_MV, float4(normal,0));
                N = normalize(N);

                float3 H = normalize(V + L);

                float NdotL = saturate(dot(N, L)*0.5+0.5);
                float NdotV = max(dot(N, V), 0);
                float NdotH = max(dot(N, H), 1.0e-7);
                float VdotH = max(dot(V, H), 0);

                float3 col = unity_LightColor[0].xyz * NdotL * _Color;
                //float3 col = NdotL * _Color;

                float3 ambient = 0.3;
                return float4(col,1.0);
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
                        shadow += sDepth < depth-_Delta ? 1 : 0 ;
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

            float4 CSMSample(float4 wPos, float4 cascadeWeights)
			{
				float4 shadowCoord0 = mul(_gWorld2Shadow[0], wPos);
				float4 shadowCoord1 = mul(_gWorld2Shadow[1], wPos);
				float4 shadowCoord2 = mul(_gWorld2Shadow[2], wPos);
				float4 shadowCoord3 = mul(_gWorld2Shadow[3], wPos);

				shadowCoord0.xy /= shadowCoord0.w;
				shadowCoord1.xy /= shadowCoord1.w;
				shadowCoord2.xy /= shadowCoord2.w;
				shadowCoord3.xy /= shadowCoord3.w;

				shadowCoord0.xy = shadowCoord0.xy*0.5 + 0.5;
				shadowCoord1.xy = shadowCoord1.xy*0.5 + 0.5;
				shadowCoord2.xy = shadowCoord2.xy*0.5 + 0.5;
				shadowCoord3.xy = shadowCoord3.xy*0.5 + 0.5;

				float4 sampleDepth0 = tex2D(_gShadowMapTexture0, shadowCoord0.xy);
				float4 sampleDepth1 = tex2D(_gShadowMapTexture1, shadowCoord1.xy);
				float4 sampleDepth2 = tex2D(_gShadowMapTexture2, shadowCoord2.xy);
				float4 sampleDepth3 = tex2D(_gShadowMapTexture3, shadowCoord3.xy);

				float depth0 = shadowCoord0.z / shadowCoord0.w;
				float depth1 = shadowCoord1.z / shadowCoord1.w;
				float depth2 = shadowCoord2.z / shadowCoord2.w;
				float depth3 = shadowCoord3.z / shadowCoord3.w;

#if defined (SHADER_TARGET_GLSL)
				depth0 = depth0*0.5 + 0.5; //(-1, 1)-->(0, 1)
				depth1 = depth1*0.5 + 0.5;
				depth2 = depth2*0.5 + 0.5;
				depth3 = depth3*0.5 + 0.5;
#elif defined (UNITY_REVERSED_Z)
				depth0 = 1 - depth0;       //(1, 0)-->(0, 1)
				depth1 = 1 - depth1;
				depth2 = 1 - depth2;
				depth3 = 1 - depth3;
#endif

				float shadow0 = sampleDepth0 < depth0-_Delta ? 1 : 0;
				float shadow1 = sampleDepth1 < depth1-_Delta ? 1 : 0;
				float shadow2 = sampleDepth2 < depth2-_Delta ? 1 : 0;
				float shadow3 = sampleDepth3 < depth3-_Delta ? 1 : 0;

            	float shadow =1-_ShadowIntensity*( shadow0*cascadeWeights[0] + shadow1*cascadeWeights[1] + shadow2*cascadeWeights[2] + shadow3*cascadeWeights[3]);
            					
            	
#ifdef CSM_PDF_ON
				if(_FilterBoxLength == 0)
					return shadow;
            	for(int x=-_FilterBoxLength;x<=_FilterBoxLength;++x){
                    for(int y=-_FilterBoxLength;y<=_FilterBoxLength;++y){

                    	float4 samp0 = tex2D(_gShadowMapTexture0,shadowCoord0.xy + float2(x,y)/_ShadowMapResolution);
                        float4 samp1 = tex2D(_gShadowMapTexture1,shadowCoord1.xy + float2(x,y)/_ShadowMapResolution);
                        float4 samp2 = tex2D(_gShadowMapTexture2,shadowCoord2.xy + float2(x,y)/_ShadowMapResolution);
                        float4 samp3 = tex2D(_gShadowMapTexture3,shadowCoord3.xy + float2(x,y)/_ShadowMapResolution);

                    	float sDepth0 = samp0.r;
                    	float sDepth1 = samp1.r;
                    	float sDepth2 = samp2.r;
                    	float sDepth3 = samp3.r;

                    	shadow0 += sDepth0 < depth0-_Delta ? 1 : 0 ;
                        shadow1 += sDepth1 < depth1-_Delta ? 1 : 0 ;
                        shadow2 += sDepth2 < depth2-_Delta ? 1 : 0 ;
                        shadow3 += sDepth3 < depth3-_Delta ? 1 : 0 ;
                    }
                }
            	float area = (_FilterBoxLength*2+1)*(_FilterBoxLength*2+1);
                shadow0 = 1 -_ShadowIntensity * shadow0/area;
                shadow1 = 1 -_ShadowIntensity * shadow1/area;
                shadow2 = 1 -_ShadowIntensity * shadow2/area;
                shadow3 = 1 -_ShadowIntensity * shadow3/area;
            	
				//return col0;
				shadow = shadow0*cascadeWeights[0] + shadow1*cascadeWeights[1] + shadow2*cascadeWeights[2] + shadow3*cascadeWeights[3];
				//shadow = shadow0;
#endif

#ifdef CSM_VSM_ON
            	float4 samp0 = tex2D(_gShadowMapTexture0,shadowCoord0.xy );
            	float4 samp1 = tex2D(_gShadowMapTexture1,shadowCoord1.xy );
            	float4 samp2 = tex2D(_gShadowMapTexture2,shadowCoord2.xy );
            	float4 samp3 = tex2D(_gShadowMapTexture3,shadowCoord3.xy );

                float2 s0 = samp0.rg;
                float2 s1 = samp1.rg;
                float2 s2 = samp2.rg;
                float2 s3 = samp3.rg;

                // E(x) and E(x^2)
                float x0 = s0.r; 
                float x1 = s1.r; 
                float x2 = s2.r; 
                float x3 = s3.r; 
                float x0_2 = s0.g;
                float x1_2 = s1.g;
                float x2_2 = s2.g;
                float x3_2 = s3.g;
                
                // the formula var = E(x^2) - E(x)^2
                float var0 = x0_2 - x0*x0; 
                float var1 = x1_2 - x1*x1; 
                float var2 = x2_2 - x2*x2; 
                float var3 = x3_2 - x3*x3; 

                float p0 = depth0 <= x0;
                float p1 = depth1 <= x1;
                float p2 = depth2 <= x2;
                float p3 = depth3 <= x3;
                
                // calculate the upper bound of the probability using Chebyshev's inequality
                float delta0 = depth0 - x0;
                float delta1 = depth1 - x1;
                float delta2 = depth2 - x2;
                float delta3 = depth3 - x3;
                float p_max0 = var0 / (var0 + delta0*delta0);
                float p_max1 = var1 / (var1 + delta1*delta1);
                float p_max2 = var2 / (var2 + delta2*delta2);
                float p_max3 = var3 / (var3 + delta3*delta3);

                // To alleviate the light bleeding, expand the shadows to fill in the gaps
                float amount = _VarianceShadowExpansion;
                p_max0 = clamp( (p_max0 - amount) / (1 - amount), 0, 1);
                p_max1 = clamp( (p_max1 - amount) / (1 - amount), 0, 1);
                p_max2 = clamp( (p_max2 - amount) / (1 - amount), 0, 1);
                p_max3 = clamp( (p_max3 - amount) / (1 - amount), 0, 1);
                
            	shadow0 = 1-_ShadowIntensity*(1 - max(p0, p_max0));
            	shadow1 = 1-_ShadowIntensity*(1 - max(p1, p_max1));
            	shadow2 = 1-_ShadowIntensity*(1 - max(p2, p_max2));
            	shadow3 = 1-_ShadowIntensity*(1 - max(p3, p_max3));

            	shadow = shadow0*cascadeWeights[0] + shadow1*cascadeWeights[1] + shadow2*cascadeWeights[2] + shadow3*cascadeWeights[3];

            	//1-_ShadowIntensity*(1 - max(p, p_max));
#endif  	

#ifdef DEBUG_MODE
            	return shadow * cascadeWeights;
#endif
            	return shadow ;
			}

            fixed4 getCascadeWeights(float z){
            	//CSM 计算每张图上阴影的权重
				fixed4 zNear = float4(z >= _gLightSplitsNear);
				fixed4 zFar = float4(z < _gLightSplitsFar);
				fixed4 weights = zNear * zFar;
				return weights;
			}

	        
            float getCompareValue(float4 wPos,float4 clipPos,float4 screenPos){
            	//比较深度  当前 shading point 对应 shandow cam 记录深度图上的深度
            	//与该点在 shandow cam 坐标系下的深度进行比较
                float4 lightSpacePos = mul(_LightMatrix, wPos);

                //float4 shadowCoord0 = mul(_TestMatrix, wPos);
                //shadowCoord0.xy /= shadowCoord0.w;
                //shadowCoord0.xy = shadowCoord0.xy*0.5 + 0.5;
                //float4 samp = tex2D(_ShadowTex, shadowCoord0.xy);
                //float depth = shadowCoord0.z / shadowCoord0.w;
                
                float depth = lightSpacePos.z / _ShadowTexScale.z;
                
                float2 uv = lightSpacePos.xy;
                uv += _ShadowTexScale.xy / 2;
                uv /= _ShadowTexScale.xy;

                float4 samp = tex2D(_ShadowTex, uv);
                float sDepth = samp.r;
                float v = 1;//visibility
#ifdef SM
            	v = (1-_ShadowIntensity*step( sDepth, depth-_Delta));//default
#endif

#ifdef PCF_ON
            	v = PCFSample( depth, uv );
#endif
            	
#ifdef VSM_ON
            	v = VSMSample(depth,samp);
#endif

#ifdef CSM
            	fixed4 weights = getCascadeWeights(clipPos.w);
                v = CSMSample(wPos, weights).r;
#endif
#ifdef SSSM
            	float2 uv_SSShadowMap = screenPos.xy/screenPos.w;//=>[0,1]
				//return float4(uv.xy,0,1);
            	v = (tex2D(_SSShadowMap,uv_SSShadowMap ));
#endif
            	
            	return v;
            	
            
            }

            fixed4 frag (v2f i) : SV_Target
            {
            	float4 color = _Color*tex2D(_MainTex, i.uv);
                color.xyz *= CalculateLight(i.wPos, i.normal_object);
            	
                float shadowIntensity = getCompareValue(i.wPos, i.vertex, i.screenPos);
            	
#ifdef DEBUG_MODE
            	//显示带权值的 CSM 阴影
				float4 weights = getCascadeWeights(i.vertex.w);
                float4 v = CSMSample(i.wPos, weights);
            	return v;
#endif
            	
                color.xyz *=(shadowIntensity);

                return color;
            }
            
            ENDCG
        }
    }
	FallBack "Diffuse"
}
