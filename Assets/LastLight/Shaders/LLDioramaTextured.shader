// Last Light diorama shader, textured variant for imported (Tripo) models.
// Same lighting, disk clipping, scout-light ghost and dither fade as LastLight/Diorama,
// but albedo comes from a base map (UV0) instead of vertex colours, so FBX characters
// and props with baked textures sit in the same night lighting as the procedural meshes.
// Works on skinned meshes (URP skins on the GPU before this vertex stage runs).
Shader "LastLight/DioramaTextured"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _Emission ("Emission", Float) = 0
        _ClipToDisk ("Clip To Disk (0/1)", Float) = 1
        _Fade ("Fade (1 solid, 0 gone)", Float) = 1
        _UseVertexColor ("Multiply Vertex Colour (0/1)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _Tint;
                half _Emission;
                half _ClipToDisk;
                half _Fade;
                half _UseVertexColor;
            CBUFFER_END

            // Globals set by Diorama.cs every frame (same as LastLight/Diorama).
            float4 _LL_Disk;
            float4 _LL_Scout;
            float  _LL_ScoutIntensity;
            float4 _LL_HeadPos;
            float4 _LL_HeadDir;
            float4 _LL_Moon;
            float4 _LL_Ambient;
            float4 _LL_ScoutColor;
            float4 _LL_GhostColor;
            float4 _LL_Reveal;
            float4 _LL_Weather;     // x fog over the scouting region, y wet surfaces, z lightning flash

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                half4  color      : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.color = v.color;
                return o;
            }

            float Bayer4(float2 px)
            {
                const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
                int2 p = int2(fmod(px, 4.0));
                return (m[p.y * 4 + p.x] + 0.5) / 16.0;
            }

            half4 frag (Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                if (_Fade < 0.999 && Bayer4(floor(i.positionCS.xy)) >= _Fade) discard;

                half3 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                half3 vcol = lerp(half3(1, 1, 1), i.color.rgb, _UseVertexColor);
                half3 albedo = tex * vcol * _Tint.rgb;

                float2 toDisk = i.positionWS.xz - _LL_Disk.xz;
                float diskDist = length(toDisk);
                float2 toScout = i.positionWS.xz - _LL_Scout.xz;
                float scoutDist = length(toScout);
                float scoutMask = saturate(1.0 - scoutDist / max(_LL_Scout.w, 1e-4)) * _LL_ScoutIntensity;

                bool outside = (_ClipToDisk > 0.5) && (diskDist > _LL_Disk.w);
                if (outside)
                {
                    float ahead = step(0.0, dot(toDisk, _LL_Reveal.xz));
                    float inRange = step(diskDist, _LL_Reveal.w);
                    float ghostMask = max(scoutMask * saturate(1.0 - _LL_Weather.x * 1.25), _LL_Reveal.y * ahead * inRange * 0.8);
                    if (ghostMask <= 0.02) discard;
                    float2 px = floor(i.positionCS.xy);
                    float pattern = fmod(px.x + px.y, 2.0);
                    if (pattern < 1.0 && ghostMask < 0.85) discard;
                    float3 n = normalize(i.normalWS);
                    float rim = 0.35 + 0.65 * saturate(n.y);
                    float lum = dot(albedo, half3(0.3, 0.6, 0.1));
                    half3 ghost = _LL_GhostColor.rgb * (0.35 + 0.65 * ghostMask) * rim * lerp(0.45, 1.6, saturate(lum * 5.0));
                    ghost *= 1.0 - 0.85 * saturate((_LL_Disk.y - i.positionWS.y) / (_LL_Disk.w * 0.2));
                    return half4(ghost, 1);
                }

                float3 N = normalize(i.normalWS);
                half3 light = _LL_Ambient.rgb + _LL_Moon.w * saturate(dot(N, normalize(_LL_Moon.xyz))) * half3(0.55, 0.62, 0.8);
                light += _LL_ScoutColor.rgb * scoutMask * (0.5 + 0.5 * saturate(N.y)) * 1.4;

                float3 toFrag = i.positionWS - _LL_HeadPos.xyz;
                float hd = length(toFrag);
                float3 hdir = toFrag / max(hd, 1e-4);
                float cone = saturate((dot(hdir, normalize(_LL_HeadDir.xyz)) - 0.80) / 0.15);
                float fall = saturate(1.0 - hd / max(_LL_HeadPos.w, 1e-4));
                light += half3(1.0, 0.92, 0.75) * cone * fall * fall * 1.6;

                half wet = _LL_Weather.y;
                albedo *= lerp(1.0, 0.7, wet);
                light += half3(0.75, 0.82, 1.0) * _LL_Weather.z * 0.7;
                half3 col = albedo * light + albedo * _Emission;
                if (wet > 0.01)
                {
                    float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                    float3 Hm = normalize(normalize(_LL_Moon.xyz) + V);
                    half spec = pow(saturate(dot(N, Hm)), 32.0) * _LL_Moon.w * 0.7 + pow(saturate(dot(N, normalize(-hdir + V))), 24.0) * cone * fall;
                    col += half3(0.8, 0.86, 1.0) * min(spec, 0.3) * wet * 0.5;
                }

                float edge = saturate((diskDist / max(_LL_Disk.w, 1e-4) - 0.82) / 0.18);
                col *= lerp(1.0, 0.55, edge * (_ClipToDisk > 0.5 ? 1.0 : 0.0));

                if (_ClipToDisk > 0.5)
                {
                    float depth = saturate((_LL_Disk.y - i.positionWS.y) / (_LL_Disk.w * 0.35));
                    col *= (1.0 - depth) * (1.0 - depth);
                }
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
