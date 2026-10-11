// Lit by the Standard model, coloured by the mesh's vertex colours: lets one
// combined terrain mesh carry a tint per hex cell and per face.
Shader "OrcHorde/VertexLit"
{
    Properties
    {
        _Glossiness ("Smoothness", Range(0, 1)) = 0.08
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0

        struct Input
        {
            float4 vcol;
        };

        half _Glossiness;

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.vcol = v.color;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            o.Albedo = IN.vcol.rgb;
            o.Smoothness = _Glossiness;
            o.Metallic = 0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
