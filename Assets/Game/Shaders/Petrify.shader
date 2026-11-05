// A lit surface that turns to stone from the feet up. _Petrify 0..1 raises a jagged front through the model's
// object-space height (_HeightMin.._HeightMax): below it the albedo goes to cracked grey stone, at it a hot seam
// glows, above it the creature keeps its own texture (and its magenta crystals glow faintly in the dark).
// _Lit adds a rim while a wand light is on it; _Flash is the burst when the stone closes over the head.
Shader "AiHackathon/Petrify"
{
    Properties
    {
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.3

        [Header(Stone)]
        _Petrify ("Petrify", Range(0, 1)) = 0
        _HeightMin ("Height Min (object)", Float) = 0
        _HeightMax ("Height Max (object)", Float) = 1
        _UpOS ("Up (object space)", Vector) = (0, 1, 0, 0)
        _StoneColor ("Stone", Color) = (0.56, 0.54, 0.5, 1)
        _StoneDark ("Stone Dark", Color) = (0.26, 0.25, 0.24, 1)
        _NoiseScale ("Noise Scale", Float) = 7
        _EdgeWidth ("Seam Width", Range(0.005, 0.3)) = 0.06
        [HDR] _EdgeColor ("Seam Glow", Color) = (2.4, 1.5, 0.55, 1)
        _CrackDensity ("Crack Density", Float) = 3

        [Header(Glow)]
        [HDR] _CrystalColor ("Crystal Glow", Color) = (1.6, 0.25, 1.8, 1)
        _CrystalPulse ("Crystal Pulse", Range(0, 1)) = 0.5
        [HDR] _RimColor ("Lit Rim", Color) = (1.4, 1.1, 0.7, 1)
        _Lit ("Lit", Range(0, 1)) = 0
        _Flash ("Flash", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Smoothness;
            half _Petrify;
            float _HeightMin;
            float _HeightMax;
            float4 _UpOS;
            half4 _StoneColor;
            half4 _StoneDark;
            float _NoiseScale;
            half _EdgeWidth;
            half4 _EdgeColor;
            float _CrackDensity;
            half4 _CrystalColor;
            half _CrystalPulse;
            half4 _RimColor;
            half _Lit;
            half _Flash;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        float Hash3(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1);
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        float Noise3(float3 x)
        {
            float3 i = floor(x);
            float3 f = frac(x);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(Hash3(i + float3(0, 0, 0)), Hash3(i + float3(1, 0, 0)), f.x),
                             lerp(Hash3(i + float3(0, 1, 0)), Hash3(i + float3(1, 1, 0)), f.x), f.y),
                        lerp(lerp(Hash3(i + float3(0, 0, 1)), Hash3(i + float3(1, 0, 1)), f.x),
                             lerp(Hash3(i + float3(0, 1, 1)), Hash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
        }

        float Fbm(float3 p)
        {
            return Noise3(p) * 0.55 + Noise3(p * 2.03 + 7.1) * 0.3 + Noise3(p * 4.1 + 3.7) * 0.15;
        }

        // Distance to the nearest cell border of a 3D Voronoi: small along the cracks between stone plates.
        float CellEdge(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            float d1 = 8.0, d2 = 8.0;
            for (int z = -1; z <= 1; z++)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                float3 g = float3(x, y, z);
                float3 o = float3(Hash3(i + g), Hash3(i + g + 19.19), Hash3(i + g + 47.3));
                float d = length(g + o - f);
                if (d < d1) { d2 = d1; d1 = d; }
                else if (d < d2) { d2 = d; }
            }
            return d2 - d1;
        }

        // Stone mask (1 = stone) and seam band for an object-space position.
        void PetrifyMask(float3 positionOS, out half stone, out half seam)
        {
            float h = saturate((dot(positionOS, _UpOS.xyz) - _HeightMin) / max(1e-4, _HeightMax - _HeightMin));
            float jag = (Fbm(positionOS * _NoiseScale) - 0.5) * 0.22;
            // Past 1 the front clears the top of the head even where the noise pushes it up.
            float front = lerp(-0.15, 1.2, _Petrify);
            float d = front - (h + jag);
            stone = smoothstep(-0.004, 0.004, d);
            seam = (1.0 - smoothstep(0.0, _EdgeWidth, abs(d))) * step(0.001, _Petrify) * step(_Petrify, 0.999);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
                half fogFactor : TEXCOORD4;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionOS = v.positionOS.xyz;
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                half stone, seam;
                PetrifyMask(i.positionOS, stone, seam);

                // Stone keeps the texture's light and dark (so the shapes still read) in grey, mottled, with dark
                // cracks between plates.
                half lum = dot(tex.rgb, half3(0.3, 0.59, 0.11));
                float3 sp = i.positionOS * _NoiseScale;
                half mottled = Fbm(sp * 1.7);
                half3 stoneAlbedo = lerp(_StoneDark.rgb, _StoneColor.rgb, saturate(mottled * 0.8 + lum * 1.1));
                half crack = 1.0 - smoothstep(0.015, 0.06, CellEdge(i.positionOS * _NoiseScale * _CrackDensity * 0.25));
                stoneAlbedo *= 1.0 - crack * 0.7;
                half3 albedo = lerp(tex.rgb, stoneAlbedo, stone);

                // Magenta crystals: where the texture is strongly purple-pink, glow (and pulse) until the stone
                // closes over them.
                half purple = saturate((min(tex.r, tex.b) - tex.g) * 3.0 - 0.25);
                half pulse = lerp(1.0, 0.55 + 0.45 * sin(_Time.y * 2.3 + i.positionOS.x * 9.0), _CrystalPulse);

                InputData input = (InputData)0;
                input.positionWS = i.positionWS;
                input.positionCS = i.positionCS;
                input.normalWS = normalize(i.normalWS);
                input.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                input.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                input.fogCoord = i.fogFactor;
                input.bakedGI = SampleSH(input.normalWS);
                input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                input.shadowMask = half4(1, 1, 1, 1);

                half ndv = saturate(dot(input.normalWS, input.viewDirectionWS));
                half rim = pow(1.0 - ndv, 3.0);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.alpha = 1;
                s.metallic = 0;
                s.smoothness = lerp(_Smoothness, 0.08, stone);
                s.occlusion = 1;
                s.normalTS = half3(0, 0, 1);
                s.emission = _CrystalColor.rgb * purple * pulse * (1.0 - stone)
                           + _EdgeColor.rgb * seam
                           + _RimColor.rgb * rim * _Lit * (1.0 - stone * 0.6)
                           + _EdgeColor.rgb * _Flash * (0.35 + rim * 1.5) * stone
                           + _EdgeColor.rgb * crack * stone * _Flash * 2.0;

                half4 color = UniversalFragmentPBR(input, s);
                color.rgb = MixFog(color.rgb, input.fogCoord);
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            float4 ShadowVert(float4 positionOS : POSITION, float3 normalOS : NORMAL) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                return ApplyShadowClamping(positionCS);
            }

            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag

            struct NormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            NormalsVaryings NormalsVert(float4 positionOS : POSITION, float3 normalOS : NORMAL)
            {
                NormalsVaryings o;
                o.positionCS = TransformObjectToHClip(positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(normalOS);
                return o;
            }

            half4 NormalsFrag(NormalsVaryings i) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(i.normalWS), 0);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
