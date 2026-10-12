using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// The frame's post-processing for the built-in pipeline (Shaders/PostFx.shader),
    /// all of it plain fragment shaders, so it runs on any GPU the game does:
    ///
    /// - Ambient occlusion: soft darkening where surfaces meet, worked out at half
    ///   size from the depth buffer and blurred without bleeding over edges.
    /// - Light shafts: rays from the sun or moon, streaming past rock and orcs
    ///   from the open sky round the light.
    /// - Bloom: whatever is brighter than the mood's threshold bleeds a soft halo.
    /// - The grade: the mood's tint and a vignette.
    ///
    /// <see cref="Sky"/> sets <see cref="Settings"/> for the map's mood.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class PostFx : MonoBehaviour
    {
        public struct Grade
        {
            public float Threshold, Intensity, Vignette;
            public Color Tint;
            public float Occlusion, ShaftStrength;
            public Color ShaftColor;
        }

        public static Grade Settings = new Grade
        {
            Threshold = 1f, Intensity = 0.5f, Vignette = 0.6f, Tint = Color.white,
            Occlusion = 0.7f, ShaftStrength = 0.4f, ShaftColor = Color.white,
        };

        /// <summary>Each part can be switched off: for the smoke test's before-and-after shots.</summary>
        public static bool BloomOn = true, OcclusionOn = true, ShaftsOn = true;

        /// <summary>For checking a part on its own: 1 the occlusion, 2 the shafts, 3 the depth.</summary>
        public static int DebugView;

        const int Levels = 5;
        const float AORadius = 1.3f, AOFade = 70f;
        static Material material;
        readonly RenderTexture[] chain = new RenderTexture[Levels];
        Camera cam;

        void Start()
        {
            cam = GetComponent<Camera>();
            cam.allowHDR = true;
            // The occlusion and the shafts read depth.
            cam.depthTextureMode |= DepthTextureMode.Depth;
        }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (material == null)
            {
                var shader = Shader.Find("Hidden/OrcHorde/PostFx");
                if (shader == null || !shader.isSupported) { Graphics.Blit(source, destination); return; }
                material = new Material(shader);
            }
            var s = Settings;
            var format = source.format;
            int halfW = source.width / 2, halfH = source.height / 2;

            float tanY = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            material.SetVector("_Proj", new Vector4(tanY * cam.aspect, tanY, 0, 0));

            // Ambient occlusion, at half size, blurred both ways.
            RenderTexture ao = null;
            if (OcclusionOn && s.Occlusion > 0)
            {
                material.SetFloat("_AOStrength", s.Occlusion);
                material.SetFloat("_AORadius", AORadius);
                material.SetFloat("_AOFade", AOFade);
                ao = RenderTexture.GetTemporary(halfW, halfH, 0, RenderTextureFormat.R8);
                var blurred = RenderTexture.GetTemporary(halfW, halfH, 0, RenderTextureFormat.R8);
                Graphics.Blit(source, ao, material, 4);
                material.SetVector("_BlurDir", new Vector4(1f / halfW, 0, 0, 0));
                Graphics.Blit(ao, blurred, material, 5);
                material.SetVector("_BlurDir", new Vector4(0, 1f / halfH, 0, 0));
                Graphics.Blit(blurred, ao, material, 5);
                RenderTexture.ReleaseTemporary(blurred);
            }
            material.SetTexture("_AO", ao != null ? (Texture)ao : Texture2D.whiteTexture);

            // Light shafts, at quarter size, only while the light is somewhere ahead.
            RenderTexture shafts = null;
            var sun = RenderSettings.sun;
            if (ShaftsOn && s.ShaftStrength > 0 && sun != null)
            {
                var toward = -sun.transform.forward;
                var screen = cam.WorldToViewportPoint(cam.transform.position + toward * 1000f);
                float facing = Vector3.Dot(cam.transform.forward, toward);
                if (screen.z > 0 && facing > 0.1f)
                {
                    // Fade as the light swings off the edge of the view.
                    float edge = Mathf.Clamp01(1.5f - Mathf.Max(Mathf.Abs(screen.x - 0.5f), Mathf.Abs(screen.y - 0.5f)) * 2f);
                    material.SetVector("_LightPos", new Vector4(screen.x, screen.y, 0, 0));
                    material.SetColor("_ShaftColor", s.ShaftColor * Mathf.Clamp01((facing - 0.1f) * 2f) * edge);
                    int qw = source.width / 4, qh = source.height / 4;
                    shafts = RenderTexture.GetTemporary(qw, qh, 0, format);
                    var pass = RenderTexture.GetTemporary(qw, qh, 0, format);
                    Graphics.Blit(source, shafts, material, 6);
                    // Three widening passes: sixteen taps each, smeared towards the light.
                    foreach (var step in new[] { 0.35f, 0.7f, 1f })
                    {
                        material.SetFloat("_ShaftStep", step);
                        Graphics.Blit(shafts, pass, material, 7);
                        (shafts, pass) = (pass, shafts);
                    }
                    RenderTexture.ReleaseTemporary(pass);
                }
            }
            material.SetTexture("_Shafts", shafts != null ? (Texture)shafts : Texture2D.blackTexture);
            material.SetFloat("_ShaftStrength", s.ShaftStrength);

            // Bloom.
            material.SetFloat("_Threshold", s.Threshold);
            material.SetFloat("_Knee", s.Threshold * 0.5f);
            material.SetFloat("_Intensity", BloomOn ? s.Intensity : 0f);
            material.SetFloat("_Vignette", s.Vignette);
            material.SetColor("_Tint", s.Tint);
            int w = halfW, h = halfH, used = 0;
            if (BloomOn)
            {
                chain[0] = RenderTexture.GetTemporary(w, h, 0, format);
                Graphics.Blit(source, chain[0], material, 0);
                for (used = 1; used < Levels; used++)
                {
                    w /= 2;
                    h /= 2;
                    if (w < 2 || h < 2) break;
                    chain[used] = RenderTexture.GetTemporary(w, h, 0, format);
                    Graphics.Blit(chain[used - 1], chain[used], material, 1);
                }
                for (int i = used - 1; i > 0; i--) Graphics.Blit(chain[i], chain[i - 1], material, 2);
            }
            material.SetTexture("_Bloom", used > 0 ? (Texture)chain[0] : Texture2D.blackTexture);

            material.SetFloat("_Debug", DebugView);
            Graphics.Blit(source, destination, material, 3);

            for (int i = 0; i < used; i++) { RenderTexture.ReleaseTemporary(chain[i]); chain[i] = null; }
            if (ao != null) RenderTexture.ReleaseTemporary(ao);
            if (shafts != null) RenderTexture.ReleaseTemporary(shafts);
        }
    }
}
