using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// Glow for the built-in pipeline: whatever is brighter than the mood's
    /// threshold (muzzle flashes, tower lights, the beacon, the moon) bleeds a
    /// soft halo, then the frame is tinted for the mood and its corners
    /// darkened a little. See Shaders/Bloom.shader.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class Bloom : MonoBehaviour
    {
        public struct Grade
        {
            public float Threshold, Intensity, Vignette;
            public Color Tint;
        }

        /// <summary>Set by <see cref="Sky"/> for the map's mood.</summary>
        public static Grade Settings = new Grade { Threshold = 1f, Intensity = 0.5f, Vignette = 0.6f, Tint = Color.white };

        /// <summary>Off for the smoke test's before-and-after shots.</summary>
        public static bool Enabled = true;

        const int Levels = 5;
        static Material material;
        readonly RenderTexture[] chain = new RenderTexture[Levels];

        void Start() => GetComponent<Camera>().allowHDR = true;

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (material == null)
            {
                var shader = Shader.Find("Hidden/OrcHorde/Bloom");
                if (shader == null || !shader.isSupported) { Graphics.Blit(source, destination); return; }
                material = new Material(shader);
            }
            if (!Enabled) { Graphics.Blit(source, destination); return; }

            var s = Settings;
            material.SetFloat("_Threshold", s.Threshold);
            material.SetFloat("_Knee", s.Threshold * 0.5f);
            material.SetFloat("_Intensity", s.Intensity);
            material.SetFloat("_Vignette", s.Vignette);
            material.SetColor("_Tint", s.Tint);

            int w = source.width / 2, h = source.height / 2;
            var format = source.format;
            chain[0] = RenderTexture.GetTemporary(w, h, 0, format);
            Graphics.Blit(source, chain[0], material, 0);
            int used = 1;
            for (; used < Levels; used++)
            {
                w /= 2;
                h /= 2;
                if (w < 2 || h < 2) break;
                chain[used] = RenderTexture.GetTemporary(w, h, 0, format);
                Graphics.Blit(chain[used - 1], chain[used], material, 1);
            }
            for (int i = used - 1; i > 0; i--) Graphics.Blit(chain[i], chain[i - 1], material, 2);
            material.SetTexture("_Bloom", chain[0]);
            Graphics.Blit(source, destination, material, 3);
            for (int i = 0; i < used; i++) { RenderTexture.ReleaseTemporary(chain[i]); chain[i] = null; }
        }
    }
}
