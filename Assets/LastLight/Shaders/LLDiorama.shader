// Last Light diorama shader.
// Vertex-coloured, flat-lit. Geometry inside the floating disk renders solid.
// Geometry beyond the rim is discarded unless the scout light patch covers it,
// in which case it renders as a dithered amber "ghost" preview.
// The ground variant (_LL_GROUND, used by LL_Ground) paints the road chunks with four blended layers: road, dirt
// verge, grass and rock. The vertex colour carries the blend weights (r road, g dirt, b rock, grass the rest) and
// a = large-scale brightness, see TrackGenerator.BuildTerrainMesh. Each layer is its target colour times its texture's
// relief around the texture's own average, so any texture sits on the same palette; layers meet by texture height,
// and rock is projected from three sides so cliff walls don't stretch.
Shader "LastLight/Diorama"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _Emission ("Emission", Float) = 0
        _ClipToDisk ("Clip To Disk (0/1)", Float) = 1
        _Fade ("Fade (1 solid, 0 gone)", Float) = 1
        [Toggle(_LL_GROUND)] _Ground ("Ground Texture Layers", Float) = 0
        [NoScaleOffset] _RoadMap ("Road", 2D) = "gray" {}
        [NoScaleOffset] _DirtMap ("Dirt Verge", 2D) = "gray" {}
        [NoScaleOffset] _GrassMap ("Grass", 2D) = "gray" {}
        [NoScaleOffset] _RockMap ("Rock (triplanar)", 2D) = "gray" {}
        _GroundTile ("Tile Size (game units, divides 12)", Float) = 6
        _RockTile ("Rock Tile Size (game units, divides 12)", Float) = 4
        _RoadColor ("Road Colour", Color) = (0.55, 0.42, 0.29, 1)
        _DirtColor ("Dirt Colour", Color) = (0.33, 0.24, 0.17, 1)
        _GrassColor ("Grass Colour", Color) = (0.27, 0.38, 0.2, 1)
        _RockColor ("Rock Colour", Color) = (0.42, 0.39, 0.35, 1)
        _RoadMean ("Road Texture Average (linear, set by the builder)", Vector) = (0.5, 0.5, 0.5, 1)
        _DirtMean ("Dirt Texture Average", Vector) = (0.5, 0.5, 0.5, 1)
        _GrassMean ("Grass Texture Average", Vector) = (0.5, 0.5, 0.5, 1)
        _RockMean ("Rock Texture Average", Vector) = (0.5, 0.5, 0.5, 1)
        _Detail ("Texture Relief", Range(0, 2)) = 1
        _BlendContrast ("Layer Edge Sharpness (height blend)", Range(0.02, 1)) = 0.3
        _ScanY ("Scan Height (object Y)", Float) = 0
        _ScanMode ("Scan (0 off, 1 keep below the line, -1 keep above)", Float) = 0
        _ScanColor ("Scan Edge Colour", Color) = (0.35, 0.9, 1, 1)
        _ScanWidth ("Scan Edge Width (object units)", Float) = 0.3
        _SnowCover ("Snow Settles On This (0/1)", Range(0, 1)) = 1
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
            #pragma shader_feature_local _LL_GROUND
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Emission;
                half _ClipToDisk;
                half _Fade;
                float _GroundTile;
                float _RockTile;
                half4 _RoadColor;
                half4 _DirtColor;
                half4 _GrassColor;
                half4 _RockColor;
                float4 _RoadMean;
                float4 _DirtMean;
                float4 _GrassMean;
                float4 _RockMean;
                half _Detail;
                half _BlendContrast;
                float _ScanY;
                half _ScanMode;
                half4 _ScanColor;
                float _ScanWidth;
                half _SnowCover;
            CBUFFER_END

            TEXTURE2D(_RoadMap);
            TEXTURE2D(_DirtMap);
            TEXTURE2D(_GrassMap);
            TEXTURE2D(_RockMap);
            SAMPLER(sampler_RoadMap);

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
            float4 _LL_Reveal;
            float4 _LL_Weather;     // x fog over the scouting region, y wet surfaces, z lightning flash, w settled snow      // xz forward direction of the disk, y reveal strength, w outer range (world)

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
                float  objY       : TEXCOORD4;
            #if defined(_LL_GROUND)
                float2 groundUV   : TEXCOORD2;
                float3 objPos     : TEXCOORD3;  // object space (game units), for the triplanar rock
                half3  objN       : TEXCOORD5;
            #endif
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
                o.objY = v.positionOS.y;
            #if defined(_LL_GROUND)
                // Object space, so the pattern rides along with each scrolling road chunk. Chunks are 12 units long,
                // so a tile size that divides 12 keeps the seams between chunks invisible.
                o.groundUV = v.positionOS.xz / max(_GroundTile, 1e-3);
                o.objPos = v.positionOS.xyz;
                o.objN = v.normalOS;
            #endif
                return o;
            }

        #if defined(_LL_GROUND)
            // One layer: its target colour times the texture's relief around the texture's own average. h is that
            // relief's brightness (about 1), used to let the bumpier layer win where two meet.
            half3 Layer(half3 tex, float4 mean, half4 tint, out half h)
            {
                half3 relief = tex / max(mean.rgb, 0.02);
                h = dot(relief, half3(0.333, 0.334, 0.333));
                return tint.rgb * lerp(half3(1, 1, 1), relief, _Detail);
            }

            half3 GroundAlbedo(Varyings i)
            {
                // Blend weights from the mesh: road, dirt, grass (the rest), rock.
                half4 w = half4(i.color.r, i.color.g, saturate(1.0 - i.color.r - i.color.g - i.color.b), i.color.b);
                // Gradients taken outside the branches, so layer borders don't break mip selection.
                float2 dx = ddx(i.groundUV), dy = ddy(i.groundUV);
                float3 rp = i.objPos / max(_RockTile, 1e-3);
                float3 rdx = ddx(rp), rdy = ddy(rp);

                half3 road = 0, dirt = 0, grass = 0, rock = 0;
                half4 h = 1;
                [branch] if (w.x > 0.01) road = Layer(SAMPLE_TEXTURE2D_GRAD(_RoadMap, sampler_RoadMap, i.groundUV, dx, dy).rgb, _RoadMean, _RoadColor, h.x);
                [branch] if (w.y > 0.01) dirt = Layer(SAMPLE_TEXTURE2D_GRAD(_DirtMap, sampler_RoadMap, i.groundUV, dx, dy).rgb, _DirtMean, _DirtColor, h.y);
                [branch] if (w.z > 0.01) grass = Layer(SAMPLE_TEXTURE2D_GRAD(_GrassMap, sampler_RoadMap, i.groundUV, dx, dy).rgb, _GrassMean, _GrassColor, h.z);
                [branch] if (w.w > 0.01)
                {
                    // Triplanar: each axis-aligned projection weighted by how squarely the surface faces it.
                    half3 bw = pow(abs(normalize(i.objN)), 4.0);
                    bw /= max(bw.x + bw.y + bw.z, 1e-4);
                    half3 t = SAMPLE_TEXTURE2D_GRAD(_RockMap, sampler_RoadMap, rp.zy, rdx.zy, rdy.zy).rgb * bw.x
                            + SAMPLE_TEXTURE2D_GRAD(_RockMap, sampler_RoadMap, rp.xz, rdx.xz, rdy.xz).rgb * bw.y
                            + SAMPLE_TEXTURE2D_GRAD(_RockMap, sampler_RoadMap, rp.xy, rdx.xy, rdy.xy).rgb * bw.z;
                    rock = Layer(t, _RockMean, _RockColor, h.w);
                }

                // Height blend: where layers overlap, the one whose texture stands higher shows through first.
                half4 b = (w + (h - 1.0) * _BlendContrast) * step(0.005, w);
                half top = max(max(b.x, b.y), max(b.z, b.w)) - _BlendContrast;
                half4 f = max(b - top, 0.0);
                f /= max(f.x + f.y + f.z + f.w, 1e-4);
                half3 albedo = road * f.x + dirt * f.y + grass * f.z + rock * f.w;
                return albedo * lerp(0.8, 1.2, i.color.a);   // large-scale brightness drift breaks up the tiling
            }
        #endif

            // 4x4 ordered dither threshold, so fading pieces dissolve while staying opaque and sorted.
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

                // Garage swap: a ragged scan line eats the old vehicle away and builds the new one,
                // with a bright band riding on the line and faint hologram stripes on what's been built.
                half scanGlow = 0;
                half scanStripes = 0;
                if (abs(_ScanMode) > 0.5)
                {
                    float d = (i.objY - _ScanY) * _ScanMode;
                    d += (Bayer4(floor(i.positionCS.xy * 0.5)) - 0.5) * _ScanWidth * 0.5;
                    if (d > 0) discard;
                    scanGlow = saturate(1.0 + d / max(_ScanWidth, 1e-3));
                    scanGlow *= scanGlow;
                    scanStripes = step(0.8, frac(i.objY * 9.0 + _Time.y * 2.0)) * 0.35;
                }

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
                    float ghostMask = max(scoutMask * saturate(1.0 - _LL_Weather.x * 1.25), _LL_Reveal.y * ahead * inRange * 0.8);
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
            #if defined(_LL_GROUND)
                albedo = GroundAlbedo(i) * _Tint.rgb;
            #endif

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

                // Snow: it settles white on upward-facing surfaces, a little patchy, and dulls the moonlight sheen.
                half snow = _LL_Weather.w * _SnowCover;
                if (snow > 0.01)
                {
                    float2 cell = floor(i.positionWS.xz * 40.0);
                    half grain = frac(sin(dot(cell, float2(12.9898, 78.233))) * 43758.5453);
                    half cover = saturate((N.y - 0.42) * 2.4) * snow * (0.82 + 0.18 * grain);
                    albedo = lerp(albedo, half3(0.78, 0.83, 0.9), cover);
                }

                // Rain: wet surfaces darken and pick up glints from the moon and headlights. Lightning flashes everything.
                half wet = _LL_Weather.y;
                albedo *= lerp(1.0, 0.62, wet);
                light += half3(0.75, 0.82, 1.0) * _LL_Weather.z * 0.7;
                half3 col = albedo * light + albedo * _Emission;
                if (wet > 0.01)
                {
                    float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                    float3 Hm = normalize(normalize(_LL_Moon.xyz) + V);
                    float3 Hh = normalize(-hdir + V);
                    float flatness = saturate(N.y * 1.4 - 0.2);
                    half spec = pow(saturate(dot(N, Hm)), 64.0) * _LL_Moon.w * 0.22 + pow(saturate(dot(N, Hh)), 32.0) * cone * fall * 0.8;
                    spec += _LL_Weather.z * 0.12;
                    col += half3(0.8, 0.86, 1.0) * min(spec, 0.3) * wet * flatness;
                }

                // Darken toward the rim so the disk edge reads as a frame.
                float edge = saturate((diskDist / max(_LL_Disk.w, 1e-4) - 0.82) / 0.18);
                col *= lerp(1.0, 0.55, edge * (_ClipToDisk > 0.5 ? 1.0 : 0.0));

                // The abyss: anything below the road fades to black with depth.
                if (_ClipToDisk > 0.5)
                {
                    float depth = saturate((_LL_Disk.y - i.positionWS.y) / (_LL_Disk.w * 0.35));
                    col *= (1.0 - depth) * (1.0 - depth);
                }
                col += _ScanColor.rgb * (scanGlow * 3.0 + scanStripes);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
