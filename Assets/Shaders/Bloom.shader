// A small bloom for the built-in pipeline, and the frame's final grade.
// Pass 0 keeps only what is brighter than the threshold, with a soft knee.
// Pass 1 halves the image with a 4-tap box; pass 2 doubles it back with a
// 9-tap tent, adding as it goes. Pass 3 lays the glow over the frame, tints it
// for the mood, and darkens the corners a touch.
Shader "Hidden/OrcHorde/Bloom"
{
    Properties
    {
        _MainTex ("", 2D) = "black" {}
        _Bloom ("", 2D) = "black" {}
    }
    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex, _Bloom;
    float4 _MainTex_TexelSize;
    float _Threshold, _Knee, _Intensity, _Vignette;
    float4 _Tint;

    struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

    v2f vert(appdata_img v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = v.texcoord;
        return o;
    }

    half4 Prefilter(v2f i) : SV_Target
    {
        half3 c = tex2D(_MainTex, i.uv).rgb;
        half bright = max(c.r, max(c.g, c.b));
        half soft = clamp(bright - _Threshold + _Knee, 0, 2 * _Knee);
        soft = soft * soft / (4 * _Knee + 1e-4);
        half keep = max(soft, bright - _Threshold) / max(bright, 1e-4);
        return half4(c * keep, 1);
    }

    half4 Down(v2f i) : SV_Target
    {
        float4 o = _MainTex_TexelSize.xyxy * float4(-1, -1, 1, 1);
        half3 c = tex2D(_MainTex, i.uv + o.xy).rgb + tex2D(_MainTex, i.uv + o.zy).rgb
                + tex2D(_MainTex, i.uv + o.xw).rgb + tex2D(_MainTex, i.uv + o.zw).rgb;
        return half4(c * 0.25, 1);
    }

    half4 Up(v2f i) : SV_Target
    {
        float4 o = _MainTex_TexelSize.xyxy * float4(1, 1, -1, 0);
        half3 c = tex2D(_MainTex, i.uv - o.xy).rgb + tex2D(_MainTex, i.uv - o.wy).rgb * 2 + tex2D(_MainTex, i.uv - o.zy).rgb
                + tex2D(_MainTex, i.uv + o.zw).rgb * 2 + tex2D(_MainTex, i.uv).rgb * 4 + tex2D(_MainTex, i.uv + o.xw).rgb * 2
                + tex2D(_MainTex, i.uv + o.zy).rgb + tex2D(_MainTex, i.uv + o.wy).rgb * 2 + tex2D(_MainTex, i.uv + o.xy).rgb;
        return half4(c / 16, 1);
    }

    half4 Combine(v2f i) : SV_Target
    {
        half3 c = tex2D(_MainTex, i.uv).rgb + tex2D(_Bloom, i.uv).rgb * _Intensity;
        c *= _Tint.rgb;
        float2 d = i.uv - 0.5;
        c *= 1 - dot(d, d) * _Vignette;
        return half4(c, 1);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass { CGPROGRAM
               #pragma vertex vert
               #pragma fragment Prefilter
               ENDCG }
        Pass { CGPROGRAM
               #pragma vertex vert
               #pragma fragment Down
               ENDCG }
        Pass { Blend One One
               CGPROGRAM
               #pragma vertex vert
               #pragma fragment Up
               ENDCG }
        Pass { CGPROGRAM
               #pragma vertex vert
               #pragma fragment Combine
               ENDCG }
    }
}
