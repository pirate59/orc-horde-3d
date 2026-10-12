// Disturbed air: what a bullet's passing, a blast's shock or a flame's heat does to the
// light behind it. The scene behind is grabbed once a frame and drawn back here a little
// displaced, rippling over time, with only a faint pale edge of its own. The vertex
// colour's alpha sets how strongly a particle or trail bends the light.
Shader "OrcHorde/AirWake"
{
    Properties
    {
        _MainTex ("Shape", 2D) = "white" {}
        _Strength ("Bend", Float) = 0.018
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+5" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        Cull Off

        // One grab, shared by every disturbance drawn this frame.
        GrabPass { "_AirGrab" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex, _AirGrab;
            float4 _MainTex_ST;
            half _Strength;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 grab : TEXCOORD1; fixed4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.grab = ComputeGrabScreenPos(o.pos);
                o.color = v.color;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half shape = tex2D(_MainTex, i.uv).a * i.color.a;
                // A ripple that runs along the shape and through time.
                float2 wave = float2(sin(i.uv.x * 18 + _Time.y * 23), cos(i.uv.y * 15 - _Time.y * 19));
                float2 uv = i.grab.xy / i.grab.w + wave * _Strength * shape;
                half4 behind = tex2D(_AirGrab, uv);
                // The faintest pale sheen, so a fast stream of disturbances still reads at a glance.
                behind.rgb += shape * 0.06;
                return half4(behind.rgb, 1);
            }
            ENDCG
        }
    }
}
