// Orcs and towers: colour from the mesh's vertex colours, surface detail from a
// shared texture, and per vertex in the second UV set how much it glows (x) and
// how smooth it is (y). Two-sided, for open shells like pauldrons and hoods.
// Per instance, a property block sets _Tint (a little variety in a horde) and
// _Flash (on a hit).
//
// Two biome overlays, set per material: _Cover settles on whatever faces up
// (snow on the shoulders, dust, moss), in patches broken up by the detail
// texture; _Grime climbs from the feet (caked snow, red dust, mud) to
// _GrimeHeight in model units, measured in world space from _FeetY and divided
// by _Scale, which each orc sets in its property block.
Shader "OrcHorde/Orc"
{
    Properties
    {
        _Detail ("Detail", 2D) = "white" {}
        [Normal] _DetailNormal ("Detail normal", 2D) = "bump" {}
        _DetailStrength ("Detail strength", Range(0, 1)) = 0.55
        _Flash ("Flash", Color) = (0, 0, 0, 0)
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _CoverColor ("Cover colour", Color) = (1, 1, 1, 1)
        _CoverAmount ("Cover amount", Range(0, 1)) = 0
        _CoverSmooth ("Cover smoothness", Range(0, 1)) = 0.3
        _GrimeColor ("Grime colour", Color) = (0.3, 0.22, 0.15, 1)
        _GrimeAmount ("Grime amount", Range(0, 1)) = 0
        _GrimeHeight ("Grime height", Float) = 0.4
        _FeetY ("Feet height (world)", Float) = 0
        _Scale ("Model scale", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow
        #pragma target 3.0

        sampler2D _Detail, _DetailNormal;
        half _DetailStrength, _CoverAmount, _CoverSmooth, _GrimeAmount, _GrimeHeight;
        float _FeetY, _Scale;
        fixed4 _Flash, _Tint, _CoverColor, _GrimeColor;

        // Packed tight: the forward pass has ten interpolators to spare.
        struct Input
        {
            float2 uv_Detail;
            float4 vcol;
            // x glow, y smoothness, z how much the surface faces up, w its height in the world
            float4 extra;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.vcol = v.color;
            o.extra = float4(v.texcoord1.xy, UnityObjectToWorldNormal(v.normal).y, mul(unity_ObjectToWorld, v.vertex).y);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            half detail = tex2D(_Detail, IN.uv_Detail).r;
            half3 albedo = IN.vcol.rgb * _Tint.rgb * lerp(1, detail, _DetailStrength);
            half smooth = IN.extra.y;
            half3 normal = UnpackScaleNormal(tex2D(_DetailNormal, IN.uv_Detail), _DetailStrength);

            // Cover: on what faces the sky, in clumps with a crisp edge rather than a wash. A coarser
            // read of the detail texture breaks it into patches; a half-blended cover over skin just
            // looks like a tint.
            half facing = IN.extra.z;
            half clumps = (tex2D(_Detail, IN.uv_Detail * 0.31).r - 0.78) * 5;
            half reach = (facing - (1 - _CoverAmount * 1.3)) * 5 + clumps;
            half cover = smoothstep(0.35, 0.65, reach) * step(0.001, _CoverAmount);
            // Grime: up from the feet, uneven.
            float height = (IN.extra.w - _FeetY) / max(_Scale, 0.01);
            half grime = saturate((_GrimeHeight - height) / max(_GrimeHeight * 0.5, 0.01) + (detail - 0.5)) * _GrimeAmount;
            grime *= 1 - IN.extra.x;   // never over a glow

            albedo = lerp(albedo, _GrimeColor.rgb * (0.8 + detail * 0.4), grime);
            albedo = lerp(albedo, _CoverColor.rgb * (0.9 + detail * 0.15), cover * (1 - IN.extra.x));
            smooth = lerp(smooth, _CoverSmooth, cover);
            normal = lerp(normal, half3(0, 0, 1), cover * 0.6);

            o.Albedo = albedo;
            o.Normal = normalize(normal);
            o.Smoothness = smooth;
            o.Metallic = saturate((IN.extra.y - 0.3) * 1.5) * (1 - cover) * (1 - grime);
            o.Emission = IN.vcol.rgb * IN.extra.x + _Flash.rgb;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
