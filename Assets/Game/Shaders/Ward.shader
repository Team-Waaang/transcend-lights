// Boss ward: an additive force field. Fresnel rim, a hex lattice wrapped round the sphere, a scan band sweeping up,
// per-cell shimmer, a ripple from the last hit point, cells going dark and cracking as the ward weakens, and a
// cell-by-cell dissolve when it breaks. Driven by WardFx.
Shader "AiHackathon/Ward"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.55, 0.25, 1.2, 1)
        [HDR] _RimColor ("Rim Color", Color) = (1.3, 0.75, 2.6, 1)
        [HDR] _HitColor ("Hit Color", Color) = (2.5, 2.0, 3.0, 1)
        _RimPower ("Rim Power", Float) = 2.2
        _HexScale ("Hex Scale", Float) = 7
        _HexLine ("Hex Line Width", Range(0.005, 0.3)) = 0.07
        _Strength ("Strength (ward HP 0..1)", Range(0, 1)) = 1
        _Hit ("Hit", Range(0, 1)) = 0
        _HitPos ("Hit Position (world)", Vector) = (0, 0, 0, 0)
        _Dissolve ("Dissolve", Range(0, 1)) = 0
        _Alpha ("Alpha", Range(0, 2)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Ward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color, _RimColor, _HitColor, _HitPos;
                float _RimPower, _HexScale, _HexLine, _Strength, _Hit, _Dissolve, _Alpha;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionOS = v.positionOS.xyz;
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            // Distance to the edge of the hexagon the point falls in (0 on the border) and that cell's id.
            float HexEdge(float2 uv, out float2 id)
            {
                const float2 r = float2(1.0, 1.7320508);
                const float2 h = r * 0.5;
                float2 a = (frac(uv / r) - 0.5) * r;
                float2 b = (frac((uv - h) / r) - 0.5) * r;
                float2 g = dot(a, a) < dot(b, b) ? a : b;
                id = uv - g;
                float2 p = abs(g);
                float d = max(dot(p, normalize(r)), p.x);
                return 0.5 - d;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetCameraPositionWS() - i.positionWS);
                float facing = abs(dot(n, v));
                float fresnel = pow(1.0 - facing, _RimPower);

                // Hex lattice in latitude/longitude of the object-space sphere.
                float3 s = normalize(i.positionOS);
                float2 sph = float2(atan2(s.z, s.x) / 6.2831853 + 0.5, asin(clamp(s.y, -1, 1)) / 3.1415927 + 0.5);
                float2 id;
                float edge = HexEdge(sph * float2(_HexScale * 2.0, _HexScale), id);
                float cell = Hash(id);
                float t = _Time.y;

                // Cells fall away one by one on a dissolve.
                clip(cell * 0.98 + 0.01 - _Dissolve);

                float lineW = _HexLine * (1.0 + 0.6 * (1.0 - _Strength));
                float lines = 1.0 - smoothstep(lineW * 0.4, lineW, edge);
                // Weakened: dead cells stop glowing, the rest flicker and their borders crack bright.
                float alive = step(cell, 0.35 + 0.65 * _Strength);
                float flicker = 0.75 + 0.25 * sin(t * (6.0 + cell * 9.0) + cell * 40.0);
                float crack = (1.0 - _Strength) * lines * step(0.6, Hash(id + 7.3)) * (0.6 + 0.4 * sin(t * 23.0 + cell * 17.0));

                // A scan band rolling up the dome, and a slow shimmer through the cells.
                float band = pow(saturate(1.0 - abs(frac(sph.y * 1.2 - t * 0.35) - 0.5) * 5.0), 3.0);
                float shimmer = pow(saturate(sin(t * 1.7 + cell * 6.2831)), 8.0);

                // Ripple from the last hit, expanding as _Hit decays.
                float d = distance(i.positionWS, _HitPos.xyz);
                float radius = (1.0 - _Hit) * 1.2;
                float ripple = _Hit * saturate(1.0 - abs(d - radius) * 7.0) + _Hit * _Hit * saturate(1.0 - d * 3.0);

                float3 col = _Color.rgb * (0.05 + lines * 0.75 * alive * flicker + band * 0.3 + shimmer * 0.25 * alive)
                           + _RimColor.rgb * fresnel * (0.55 + 0.45 * _Strength)
                           + _HitColor.rgb * (ripple + crack * 1.5 + _Hit * 0.15);
                // The rim of the dissolve burns bright.
                col += _HitColor.rgb * _Dissolve * 3.0 * step(cell * 0.98 + 0.01 - _Dissolve, 0.06);
                float alpha = saturate(fresnel * 0.9 + lines * 0.45 * alive + band * 0.25 + ripple + crack + 0.05);
                alpha *= front ? 1.0 : 0.55;
                return half4(col, alpha * _Alpha);
            }
            ENDHLSL
        }
    }
}
