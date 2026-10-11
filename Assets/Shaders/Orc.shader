// Orcs: colour from the mesh's vertex colours, surface detail from a shared
// hide texture, and per vertex in the second UV set how much it glows (x) and
// how smooth it is (y). Two-sided, for open shells like pauldrons and hoods.
// Per orc, a property block sets _Tint (a little variety in a horde) and _Flash (on a hit).
Shader "OrcHorde/Orc"
{
    Properties
    {
        _Detail ("Detail", 2D) = "white" {}
        [Normal] _DetailNormal ("Detail normal", 2D) = "bump" {}
        _DetailStrength ("Detail strength", Range(0, 1)) = 0.55
        _Flash ("Flash", Color) = (0, 0, 0, 0)
        _Tint ("Tint", Color) = (1, 1, 1, 1)
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
        half _DetailStrength;
        fixed4 _Flash, _Tint;

        struct Input
        {
            float2 uv_Detail;
            float4 vcol;
            float2 extra;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.vcol = v.color;
            o.extra = v.texcoord1.xy;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            half detail = tex2D(_Detail, IN.uv_Detail).r;
            o.Albedo = IN.vcol.rgb * _Tint.rgb * lerp(1, detail, _DetailStrength);
            o.Normal = UnpackScaleNormal(tex2D(_DetailNormal, IN.uv_Detail), _DetailStrength);
            o.Smoothness = IN.extra.y;
            o.Metallic = saturate((IN.extra.y - 0.3) * 1.5);
            o.Emission = IN.vcol.rgb * IN.extra.x + _Flash.rgb;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
