Shader "Assigment/A2LightModel"
{
    Properties{
        _MainTex ("Texture", 2D) = "white" {}
        [NoScaleOffset]_NormalMap ("Normal Map", 2D) = "bump" {}
        _P ("Gloss", Range(0,1)) = 1
        _NormalFactor("Normal Factor" ,Range(0,1)) = 1
        _AmbientColor ("Ambient", Color) = (1, 1, 1, 1)
        _DiffuseColor ("Diffuse", Color) = (1, 1, 1, 1)
        _SpecularColor ("Specular", Color) = (1, 1, 1, 1)
    }
    
    CGINCLUDE
        #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            samplerCUBE _CubeMap;
            float _RotationY;
            float _P;
            float4 _AmbientColor;
            float4 _DiffuseColor;
            float4 _SpecularColor;
            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _NormalMap;
            float _NormalFactor;
            
            struct appdata{
                float4 vertex : POSITION;
                float4 uv : TEXCOORD0;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                //Vector4, with x,y,z components defining the vector, and w used to flip the binormal if needed.
                //W stores handedness and should always be 1 or -1.

            };

            struct v2f{
                float2 uv : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
                float3 normal : TEXCOORD3;
                float4 vertex : SV_POSITION;
                float4 wPos : TEXCOORD4;
                float3 tangent :TEXCOORD5;
                float3 bitangent : TEXCOORD6;
                LIGHTING_COORDS(7,8)
                
            };
            
            v2f vert (appdata v){
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.wPos = mul(unity_ObjectToWorld,v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.tangent = UnityObjectToWorldDir( v.tangent.xyz);
                o.bitangent = cross( o.normal , o.tangent )*( v.tangent.w * unity_WorldTransformParams.w );//unity_WorldTransformParams.w 应对负缩放
                TRANSFER_VERTEX_TO_FRAGMENT(o); 
                return o;
            }

            fixed4 frag (v2f i) : SV_Target{

                fixed3 N = normalize(i.normal);
                fixed3 L = normalize(UnityWorldSpaceLightDir(i.wPos));
                fixed3 V = normalize(UnityWorldSpaceViewDir(i.wPos));

                float3x3 TBN = {
                    i.tangent.x , i.bitangent.x , i.normal.x,
                    i.tangent.y , i.bitangent.y , i.normal.y,
                    i.tangent.z , i.bitangent.z , i.normal.z,
                };
                
                N = normalize(mul(TBN , normalize(lerp(float3(0,0,1),UnpackNormal( tex2D( _NormalMap , i.uv )),_NormalFactor)) ));
                
                fixed3 ambient = _AmbientColor;

                fixed3 diffuse = _LightColor0.xyz * _DiffuseColor * tex2D(_MainTex, i.uv) * saturate(dot(N,L)) ;

                fixed3 specular = _LightColor0.xyz * _SpecularColor * pow(saturate(dot(N, normalize(L+V))), exp2( _P * 11 ) + 2)*_P;//(2,1900)=>(0,1),2^11

                float attenuation = LIGHT_ATTENUATION(i);
                
                return fixed4(ambient + (diffuse + specular)*attenuation, 1.0);
                
            }
    ENDCG
    
    SubShader{
        
        Tags { "RenderType"="Opaque" }

        Pass{
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
        
        Pass{
            Tags { "LightMode" = "ForwardAdd" }
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdadd
            /*
             * #pragma multi_compile_fwdadd
             * 这个也是unity为forwardadd pass定制的multi_compile，打开Viriants看到如下：
             * POINT
             * DIRECTIONAL
             * SPOT
             * POINT_COOKIE
             * DIRECTIONAL_COOKIE
             * unity会把forwardadd pass分别编译成适用于上述5种不同光源类型的版本。如果缺少这一行代码，那么unity只会编译DIRECTIONAL这一种情况。
             */

            
            ENDCG
        }
    }
}
