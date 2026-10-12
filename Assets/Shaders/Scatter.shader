// Ground scatter: grass, bushes, pebbles, trees, cacti, drifts. Drawn instanced,
// thousands to a call, each instance with its own colour. Vertex colour carries
// the mesh's own shading in rgb and, in alpha, how much a vertex sways in the
// wind: blade tips fully, roots and stones not at all.
Shader "OrcHorde/Scatter"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Wind ("Wind strength", Range(0, 0.5)) = 0.08
        _Glossiness ("Smoothness", Range(0, 1)) = 0.1
        _UpNormals ("Light as the ground (grass)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off

        CGPROGRAM
        #pragma surface surf Standard vertex:vert addshadow
        #pragma multi_compile_instancing
        #pragma target 3.0

        half _Wind, _Glossiness, _UpNormals;

        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
        UNITY_INSTANCING_BUFFER_END(Props)

        struct Input
        {
            float4 vcol;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            float3 world = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
            float sway = v.color.a;
            float t = _Time.y * 1.7 + world.x * 0.35 + world.z * 0.27;
            float gust = sin(t) * 0.7 + sin(t * 2.3 + 1.3) * 0.3;
            v.vertex.xz += float2(gust, gust * 0.4) * sway * _Wind;
            o.vcol = v.color;
            // Blades are single sheets seen from both sides: lit as the ground they grow from, not by their face.
            v.normal = normalize(lerp(v.normal, float3(0, 1, 0), _UpNormals));
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
            o.Albedo = IN.vcol.rgb * tint.rgb;
            o.Smoothness = _Glossiness;
            o.Metallic = 0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
