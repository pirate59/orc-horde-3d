// The frame's post-processing for the built-in pipeline, in one shader:
//   0 bloom prefilter: what's brighter than the threshold, with a soft knee
//   1 bloom down: a 4-tap box to half size
//   2 bloom up: a 9-tap tent back up, added to the level above
//   3 combine: ambient occlusion and shafts and bloom over the frame, the mood's tint, a vignette
//   4 ambient occlusion: how much of the hemisphere above each point nearby geometry hides
//   5 blur: a depth-aware 5-tap blur along _BlurDir, so occlusion doesn't bleed over edges
//   6 shaft mask: the open sky round the sun or moon
//   7 shaft blur: that mask smeared out radially from the light
Shader "Hidden/OrcHorde/PostFx"
{
    Properties
    {
        _MainTex ("", 2D) = "black" {}
        _Bloom ("", 2D) = "black" {}
        _AO ("", 2D) = "white" {}
        _Shafts ("", 2D) = "black" {}
    }
    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex, _Bloom, _AO, _Shafts;
    UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
    float4 _MainTex_TexelSize, _CameraDepthTexture_TexelSize;
    float _Threshold, _Knee, _Intensity, _Vignette;
    float4 _Tint;
    // Ambient occlusion: strength, reach in metres, and the view's tan(half fov) on x and y.
    float _AOStrength, _AORadius, _AOFade;
    float4 _Proj;
    float4 _BlurDir;
    // Shafts: the light's place on screen, its colour, and how far and strongly they reach.
    float4 _LightPos, _ShaftColor;
    float _ShaftStrength, _ShaftStep;
    // For checking the parts on their own: 1 shows the occlusion, 2 the shafts, 3 the depth.
    float _Debug;

    struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

    v2f vert(appdata_img v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = v.texcoord;
        return o;
    }

    float EyeDepth(float2 uv) { return LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv)); }

    float3 ViewPos(float2 uv)
    {
        float z = EyeDepth(uv);
        return float3((uv * 2 - 1) * _Proj.xy * z, z);
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
        if (_Debug > 0.5 && _Debug < 1.5) return tex2D(_AO, i.uv).rrrr;
        if (_Debug > 1.5 && _Debug < 2.5) return tex2D(_Shafts, i.uv);
        if (_Debug > 2.5) return frac(EyeDepth(i.uv) / 20);
        half3 c = tex2D(_MainTex, i.uv).rgb;
        c *= tex2D(_AO, i.uv).r;
        c += tex2D(_Shafts, i.uv).rgb * _ShaftStrength;
        c += tex2D(_Bloom, i.uv).rgb * _Intensity;
        c *= _Tint.rgb;
        float2 d = i.uv - 0.5;
        c *= 1 - dot(d, d) * _Vignette;
        return half4(c, 1);
    }

    // Ambient occlusion from the depth buffer alone: twelve taps on a spiral, each
    // counting by how steep an angle it rises above the surface. Only angles past a
    // floor count, so the depth buffer's own fine steps on flat ground don't.
    half4 AO(v2f i) : SV_Target
    {
        float3 p = ViewPos(i.uv);
        if (p.z > _ProjectionParams.z * 0.95) return 1;
        // The surface's facing from its neighbours in the depth buffer: on each axis the nearer
        // of the two, so an edge behind doesn't bend it.
        float2 t = _CameraDepthTexture_TexelSize.xy * 2;
        float3 r = ViewPos(i.uv + float2(t.x, 0)) - p, l = p - ViewPos(i.uv - float2(t.x, 0));
        float3 u = ViewPos(i.uv + float2(0, t.y)) - p, b = p - ViewPos(i.uv - float2(0, t.y));
        float3 dx = abs(r.z) < abs(l.z) ? r : l, dy = abs(u.z) < abs(b.z) ? u : b;
        float3 n = normalize(cross(dy, dx));
        // Whatever way the screen's axes run on this GPU, the surface faces the camera.
        n *= dot(n, p) > 0 ? -1 : 1;
        // Per-pixel rotation of the spiral, so twelve taps don't band.
        float spin = frac(sin(dot(i.uv * _ScreenParams.xy, float2(12.9898, 78.233))) * 43758.5453) * 6.2831853;
        float reach = _AORadius / (p.z * _Proj.x * 2);
        float occlusion = 0;
        const int Taps = 12;
        for (int k = 0; k < Taps; k++)
        {
            float t = (k + 0.5) / Taps;
            float a = spin + t * 7 * 6.2831853 / Taps * 1.7;
            float2 uv = i.uv + float2(cos(a), sin(a)) * reach * t;
            float3 v = ViewPos(uv) - p;
            float d2 = dot(v, v);
            float rise = dot(v, n) * rsqrt(d2 + 1e-5);
            occlusion += saturate((rise - 0.3) * 1.5) * saturate(1 - d2 / (_AORadius * _AORadius * 4));
        }
        occlusion = saturate(occlusion * 3.6 / Taps);
        // Gone into the fog at range, where it would only grey the haze.
        occlusion *= saturate(1 - p.z / _AOFade);
        return 1 - occlusion * _AOStrength;
    }

    half4 Blur(v2f i) : SV_Target
    {
        float z = EyeDepth(i.uv);
        float total = 0, weight = 0;
        for (int k = -2; k <= 2; k++)
        {
            float2 uv = i.uv + _BlurDir.xy * k;
            float w = exp(-k * k * 0.3) * saturate(1 - abs(EyeDepth(uv) - z) / (0.3 + z * 0.05));
            total += tex2D(_MainTex, uv).r * w;
            weight += w;
        }
        return total / max(weight, 1e-4);
    }

    half4 ShaftMask(v2f i) : SV_Target
    {
        // Only open sky lets light through; land, towers and orcs cut it.
        float sky = step(_ProjectionParams.z * 0.98, EyeDepth(i.uv));
        float2 d = (i.uv - _LightPos.xy) * float2(_ScreenParams.x / _ScreenParams.y, 1);
        // The sky's own light, strongest at the sun: rays carry what is really there.
        float near = saturate(1 - length(d) / 0.4);
        half3 light = tex2D(_MainTex, i.uv).rgb;
        return half4(light * _ShaftColor.rgb * sky * near * near * near, 1);
    }

    half4 ShaftBlur(v2f i) : SV_Target
    {
        float2 toLight = (_LightPos.xy - i.uv) * _ShaftStep;
        float2 uv = i.uv;
        half3 c = 0;
        half fall = 1;
        const int Taps = 16;
        for (int k = 0; k < Taps; k++)
        {
            c += tex2D(_MainTex, uv).rgb * fall;
            uv += toLight / Taps;
            fall *= 0.94;
        }
        return half4(c / Taps, 1);
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
        Pass { CGPROGRAM
               #pragma vertex vert
               #pragma fragment AO
               #pragma target 3.0
               ENDCG }
        Pass { CGPROGRAM
               #pragma vertex vert
               #pragma fragment Blur
               ENDCG }
        Pass { CGPROGRAM
               #pragma vertex vert
               #pragma fragment ShaftMask
               ENDCG }
        Pass { CGPROGRAM
               #pragma vertex vert
               #pragma fragment ShaftBlur
               ENDCG }
    }
}
