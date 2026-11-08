// Last Light diorama shader.
// Vertex-coloured, flat-lit. Geometry inside the floating disk renders solid.
// Geometry beyond the rim is discarded unless the scout light patch covers it,
// in which case it renders as a dithered amber "ghost" preview.
Shader "LastLight/Diorama"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _Emission ("Emission", Float) = 0
        _ClipToDisk ("Clip To Disk (0/1)", Float) = 1
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

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Emission;
                half _ClipToDisk;
            CBUFFER_END

            // Globals set by Diorama.cs every frame.
            float4 _LL_Disk;        // xyz centre (world), w radius (world)
            float4 _LL_Scout;       // xyz patch centre (world), w patch radius (world)
            float  _LL_ScoutIntensity;
            float4 _LL_HeadPos;     // xyz headlight origin (world), w range (world)
            float4 _LL_HeadDir;     // xyz headlight direction (world)
            float4 _LL_Moon;        // xyz direction towards moon, w intensity
            float4 _LL_Ambient;     // rgb ambient
            float4 _LL_ScoutColor;  // rgb amber
            float4 _LL_GhostColor;  // rgb ghost tint
            float4 _LL_Reveal;      // xz forward direction of the disk, y reveal strength, w outer range (world)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
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
                o.color = v.color;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float2 toDisk = i.positionWS.xz - _LL_Disk.xz;
                float diskDist = length(toDisk);
                float2 toScout = i.positionWS.xz - _LL_Scout.xz;
                float scoutDist = length(toScout);
                float scoutMask = saturate(1.0 - scoutDist / max(_LL_Scout.w, 1e-4)) * _LL_ScoutIntensity;

                bool outside = (_ClipToDisk > 0.5) && (diskDist > _LL_Disk.w);
                if (outside)
                {
                    // The flashlight powerup lights the whole scouting region ahead of the disk.
                    float ahead = step(0.0, dot(toDisk, _LL_Reveal.xz));
                    float inRange = step(diskDist, _LL_Reveal.w);
                    float ghostMask = max(scoutMask, _LL_Reveal.y * ahead * inRange * 0.8);
                    if (ghostMask <= 0.02) discard;
                    // Screen-door dither so the preview reads as translucent without sorting issues.
                    float2 px = floor(i.positionCS.xy);
                    float pattern = fmod(px.x + px.y, 2.0);
                    if (pattern < 1.0 && ghostMask < 0.85) discard;
                    float3 n = normalize(i.normalWS);
                    float rim = 0.35 + 0.65 * saturate(n.y);
                    // Keep the road, rocks and hazards brighter than the forest so routes read in the preview.
                    float lum = dot(i.color.rgb * _Tint.rgb, half3(0.3, 0.6, 0.1));
                    half3 ghost = _LL_GhostColor.rgb * (0.35 + 0.65 * ghostMask) * rim * lerp(0.45, 1.6, saturate(lum * 5.0));
                    // Drops read as dark gaps in the preview, so cliff roads can be scouted.
                    ghost *= 1.0 - 0.85 * saturate((_LL_Disk.y - i.positionWS.y) / (_LL_Disk.w * 0.2));
                    return half4(ghost, 1);
                }

                float3 N = normalize(i.normalWS);
                half3 albedo = i.color.rgb * _Tint.rgb;

                // Moonlight + ambient.
                half3 light = _LL_Ambient.rgb + _LL_Moon.w * saturate(dot(N, normalize(_LL_Moon.xyz))) * half3(0.55, 0.62, 0.8);

                // Scout light: warm pool on whatever it touches.
                light += _LL_ScoutColor.rgb * scoutMask * (0.5 + 0.5 * saturate(N.y)) * 1.4;

                // Vehicle headlights keep the immediate road readable.
                float3 toFrag = i.positionWS - _LL_HeadPos.xyz;
                float hd = length(toFrag);
                float3 hdir = toFrag / max(hd, 1e-4);
                float cone = saturate((dot(hdir, normalize(_LL_HeadDir.xyz)) - 0.80) / 0.15);
                float fall = saturate(1.0 - hd / max(_LL_HeadPos.w, 1e-4));
                light += half3(1.0, 0.92, 0.75) * cone * fall * fall * 1.6;

                half3 col = albedo * light + albedo * _Emission;

                // Darken toward the rim so the disk edge reads as a frame.
                float edge = saturate((diskDist / max(_LL_Disk.w, 1e-4) - 0.82) / 0.18);
                col *= lerp(1.0, 0.55, edge * (_ClipToDisk > 0.5 ? 1.0 : 0.0));

                // The abyss: anything below the road fades to black with depth.
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
