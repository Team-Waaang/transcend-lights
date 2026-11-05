// The visible shaft of a wand light: an open cone mesh (uv.y 0 at the wand, 1 at the far end), additive, fading
// along its length, toward its silhouette (so it reads as a soft volume, not a hollow cone) and where it meets
// geometry (soft intersection against the depth texture).
Shader "AiHackathon/LightCone"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 0.85, 0.6, 1)
        _Intensity ("Intensity", Range(0, 4)) = 1
        _EdgeSoftness ("Edge Softness", Range(0.5, 6)) = 2.2
        _LengthFade ("Length Fade", Range(0.2, 4)) = 1.3
        _NearFade ("Near Fade", Range(0, 0.3)) = 0.04
        _DepthFade ("Depth Fade (m)", Range(0.01, 2)) = 0.35
        _DustScale ("Dust Scale", Float) = 3
        _DustAmount ("Dust", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Cone"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
                half _EdgeSoftness;
                half _LengthFade;
                half _NearFade;
                float _DepthFade;
                float _DustScale;
                half _DustAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.screenPos = ComputeScreenPos(p.positionCS);
                return o;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x), lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x), lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            half4 Frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(i.normalWS) * (front ? 1 : -1);
                float3 view = GetWorldSpaceNormalizeViewDir(i.positionWS);
                // Seen from behind the caster the shaft runs away from the eye and its surface is all grazing, so
                // lift grazing angles (the far rim still fades out on its own along the length).
                half facing = pow(saturate(abs(dot(n, view)) * 2.2), _EdgeSoftness);
                half along = pow(saturate(1.0 - i.uv.y), _LengthFade) * smoothstep(0.0, max(1e-3, _NearFade), i.uv.y);

                float2 suv = i.screenPos.xy / i.screenPos.w;
                float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float self = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                half soft = saturate((scene - self) / _DepthFade);

                half dust = lerp(1.0, Noise(i.positionWS * _DustScale + float3(0, -_Time.y * 0.15, _Time.y * 0.1)) * 1.6, _DustAmount);
                half a = facing * along * soft * dust * _Intensity;
                return half4(_Color.rgb * a, 0);
            }
            ENDHLSL
        }
    }
}
