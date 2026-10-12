// The sky, painted procedurally: a gradient from zenith to horizon, a sun or
// moon with its halo, drifting clouds, and stars that come out as the light
// goes. Every colour is set by the map's lighting mood (World/Sky.cs), and the
// horizon matches the fog so the land fades into it without a seam.
Shader "OrcHorde/Sky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.2, 0.35, 0.6, 1)
        _Horizon ("Horizon", Color) = (0.6, 0.7, 0.8, 1)
        _Ground ("Below the horizon", Color) = (0.2, 0.2, 0.22, 1)
        _Curve ("Gradient curve", Range(0.1, 4)) = 0.6
        _SunDir ("Sun or moon direction (towards it)", Vector) = (0, 0.5, 0.8, 0)
        _SunColor ("Disc colour", Color) = (1, 0.95, 0.8, 1)
        _SunSize ("Disc size, radians", Range(0.005, 0.2)) = 0.03
        _HaloColor ("Halo colour", Color) = (1, 0.8, 0.5, 1)
        _HaloPower ("Halo tightness", Range(1, 2000)) = 60
        _HaloStrength ("Halo strength", Range(0, 4)) = 0.6
        _Moon ("Moon (craters, no glare)", Range(0, 1)) = 0
        _Stars ("Stars", Range(0, 1)) = 0
        _CloudColor ("Clouds lit", Color) = (1, 1, 1, 1)
        _CloudShade ("Clouds in shadow", Color) = (0.6, 0.65, 0.7, 1)
        _CloudCover ("Cloud cover", Range(0, 1)) = 0.4
        _CloudSpeed ("Cloud drift", Float) = 0.004
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Zenith, _Horizon, _Ground, _SunColor, _HaloColor, _CloudColor, _CloudShade;
            float4 _SunDir;
            float _Curve, _SunSize, _HaloPower, _HaloStrength, _Moon, _Stars, _CloudCover, _CloudSpeed;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float hash2(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(hash2(i), hash2(i + float2(1, 0)), f.x),
                            lerp(hash2(i + float2(0, 1)), hash2(i + float2(1, 1)), f.x), f.y);
            }

            float fbm(float2 p)
            {
                float v = 0, a = 0.5;
                for (int k = 0; k < 5; k++) { v += a * noise(p); p = p * 2.03 + 17.1; a *= 0.5; }
                return v;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;

                // The dome: horizon up to zenith, and below the horizon a fall to the ground colour.
                float3 col = h >= 0
                    ? lerp(_Horizon.rgb, _Zenith.rgb, pow(saturate(h), _Curve))
                    : lerp(_Horizon.rgb, _Ground.rgb, saturate(-h * 6));

                // Stars: one cell in a few hundred holds one, twinkling, fading into the haze low down.
                if (_Stars > 0 && h > 0)
                {
                    float3 cell = floor(d * 260);
                    float r = hash(cell);
                    if (r > 0.9965)
                    {
                        float3 centre = (cell + 0.5) / 260;
                        float spot = saturate(1 - length(d - normalize(centre)) * 520);
                        float twinkle = 0.65 + 0.35 * sin(_Time.y * (2 + r * 9) + r * 60);
                        col += spot * spot * twinkle * _Stars * saturate(h * 5) * lerp(float3(0.75, 0.85, 1), float3(1, 0.95, 0.85), frac(r * 97));
                    }
                }

                // The sun or the moon, and the glow around it.
                float3 s = normalize(_SunDir.xyz);
                float cosAngle = dot(d, s);
                float angle = acos(clamp(cosAngle, -1, 1));
                float disc = smoothstep(_SunSize, _SunSize * 0.85, angle);
                float halo = pow(saturate(cosAngle), _HaloPower) * _HaloStrength;
                col += _HaloColor.rgb * halo;

                // Clouds on a flat sheet overhead, thinning towards the horizon.
                if (_CloudCover > 0 && h > 0.01)
                {
                    float2 uv = d.xz / (h + 0.12) * 0.9 + _Time.y * _CloudSpeed * float2(1, 0.4);
                    float n = fbm(uv * 1.6);
                    float cloud = smoothstep(1 - _CloudCover, 1.15 - _CloudCover * 0.6, n) * saturate(h * 4);
                    // Lit on the side towards the light, a brighter rim where they pass in front of it.
                    float lit = saturate(0.5 + 0.5 * cosAngle) + halo * 0.6;
                    float3 cloudCol = lerp(_CloudShade.rgb, _CloudColor.rgb, saturate(lit * (1.1 - n * 0.5)));
                    col = lerp(col, cloudCol, cloud * 0.92);
                    disc *= 1 - cloud * 0.85;
                }

                // A moon has seas and craters; a sun is plain hot light.
                float3 discCol = _SunColor.rgb;
                if (_Moon > 0)
                {
                    float3 up = abs(s.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
                    float3 tx = normalize(cross(up, s)), ty = cross(s, tx);
                    float2 m = float2(dot(d, tx), dot(d, ty)) / _SunSize;
                    float seas = smoothstep(0.45, 0.75, fbm(m * 2.2 + 3.7));
                    float rim = saturate(1 - length(m)) ;
                    discCol *= lerp(1, 0.72, seas * _Moon) * lerp(0.82, 1.0, sqrt(rim));
                }
                col = lerp(col, discCol, disc);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
