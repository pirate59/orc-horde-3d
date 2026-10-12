// Lit by the Standard model: a texture with its normal map, tinted by the
// material colour and by the mesh's vertex colours. Lets one combined terrain
// mesh carry a texture and still vary cell by cell.
Shader "OrcHorde/TexturedVertexLit"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _MainTex ("Albedo", 2D) = "white" {}
        [Normal] _BumpMap ("Normal map", 2D) = "bump" {}
        _BumpScale ("Normal strength", Range(0, 2)) = 1
        _Glossiness ("Smoothness", Range(0, 1)) = 0.1
        _Bands ("Strata bands", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0

        sampler2D _MainTex, _BumpMap;
        fixed4 _Color;
        half _BumpScale, _Glossiness, _Bands;

        struct Input
        {
            float2 uv_MainTex;
            float4 vcol;
            float3 worldPos;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.vcol = v.color;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            o.Albedo = tex2D(_MainTex, IN.uv_MainTex).rgb * _Color.rgb * IN.vcol.rgb;
            // Strata on cliff faces: light and dark beds by height, wavering a little along the rock.
            if (_Bands > 0)
            {
                float y = IN.worldPos.y + sin(IN.worldPos.x * 0.21 + IN.worldPos.z * 0.17) * 0.35;
                float bed = sin(y * 2.1) * 0.6 + sin(y * 5.3 + 1.7) * 0.25 + sin(y * 0.7) * 0.4;
                o.Albedo *= lerp(1, 0.86 + 0.14 * bed + (frac(y * 0.37) < 0.08 ? -0.12 : 0), _Bands);
                o.Albedo = lerp(o.Albedo, o.Albedo * float3(1.06, 0.98, 0.9), _Bands * saturate(sin(y * 0.9) * 0.5 + 0.5));
            }
            o.Normal = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_MainTex), _BumpScale);
            o.Smoothness = _Glossiness;
            o.Metallic = 0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
