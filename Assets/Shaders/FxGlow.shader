// Glowing effects: beams, flames, sparks, tracers, lightning. Added to the scene (so they
// brighten what's behind them and never darken it), bright enough to reach the bloom, and
// faded by the vertex colour's alpha. Fog dims them with distance rather than greying them.
Shader "OrcHorde/FxGlow"
{
    Properties
    {
        _MainTex ("Shape", 2D) = "white" {}
        _Boost ("Brightness", Float) = 1.8
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            half _Boost;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; UNITY_FOG_COORDS(1) };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 c = tex2D(_MainTex, i.uv) * i.color;
                half3 col = c.rgb * c.a * _Boost;
                UNITY_APPLY_FOG_COLOR(i.fogCoord, col, half3(0, 0, 0));
                return half4(col, 0);
            }
            ENDCG
        }
    }
}
