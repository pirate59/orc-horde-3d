using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// The weight of a fight: light thrown by every blast and muzzle, scorch
    /// marks and blood left on the ground, sparks and grit off whatever a shot
    /// strikes, the camera shaken by blasts nearby, and a moment's hit-stop on a
    /// killing headshot. Everything is pooled, so a full horde under the guns
    /// costs no more than a quiet road.
    /// </summary>
    public static class Combat
    {
        const int Lights = 16, MostScorches = 70, MostBlood = 400;
        const float ScorchLife = 25f, MarkFade = 3f;
        /// <summary>Blood stays for the whole map: only the oldest pools give way, past the most kept.</summary>
        const float BloodLife = float.PositiveInfinity;

        static Light[] lights;
        static float[] lightEnds, lightLife, lightPeak;
        static int nextLight;
        static Runner runner;

        sealed class Mark { public GameObject Go; public Renderer Renderer; public float Born, Life; }
        static readonly Queue<Mark> scorches = new Queue<Mark>(), blood = new Queue<Mark>();
        static Material scorchMaterial, bloodMaterial;
        static Mesh quad;
        static MaterialPropertyBlock block;

        /// <summary>Drives the pools: fades the lights and the marks. Made with the first effect a scene asks for.</summary>
        sealed class Runner : MonoBehaviour
        {
            void Update()
            {
                float now = Time.time;
                for (int i = 0; i < Lights; i++)
                {
                    var l = lights[i];
                    if (!l.enabled) continue;
                    float left = (lightEnds[i] - now) / lightLife[i];
                    if (left <= 0) { l.enabled = false; continue; }
                    l.intensity = lightPeak[i] * left * left;
                }
                Fade(scorches, now);
                Fade(blood, now);
            }

            static void Fade(Queue<Mark> marks, float now)
            {
                foreach (var m in marks)
                {
                    if (m.Go == null || !m.Go.activeSelf) continue;
                    float left = m.Born + m.Life - now;
                    if (left <= 0) { m.Go.SetActive(false); continue; }
                    if (left < MarkFade)
                    {
                        block.SetColor("_Color", new Color(1, 1, 1, left / MarkFade));
                        m.Renderer.SetPropertyBlock(block);
                    }
                }
            }
        }

        static void Ensure()
        {
            if (runner != null) return;
            var go = new GameObject("Combat effects");
            runner = go.AddComponent<Runner>();
            lights = new Light[Lights];
            lightEnds = new float[Lights];
            lightLife = new float[Lights];
            lightPeak = new float[Lights];
            for (int i = 0; i < Lights; i++)
            {
                var l = new GameObject("Flash").AddComponent<Light>();
                l.transform.SetParent(go.transform, false);
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.enabled = false;
                lights[i] = l;
            }
            scorches.Clear();
            blood.Clear();
            block ??= new MaterialPropertyBlock();
            if (quad == null) quad = Quad();
            if (scorchMaterial == null) scorchMaterial = Decal(ScorchTexture());
            if (bloodMaterial == null) bloodMaterial = Decal(BloodTexture());
        }

        // --- Light ------------------------------------------------------------

        /// <summary>A flash of light that dies away: a blast, a muzzle, a mine.</summary>
        public static void Flash(Vector3 at, Color color, float range, float intensity, float life)
        {
            Ensure();
            // The oldest flash gives way: sixteen at once is plenty to light a battle.
            int i = nextLight;
            nextLight = (nextLight + 1) % Lights;
            var l = lights[i];
            l.transform.position = at;
            l.color = color;
            l.range = range;
            l.intensity = intensity;
            l.enabled = true;
            lightPeak[i] = intensity;
            lightLife[i] = life;
            lightEnds[i] = Time.time + life;
        }

        // --- Marks on the ground ------------------------------------------------

        /// <summary>A blackened patch where something blew up.</summary>
        public static void Scorch(Vector3 at, float radius)
        {
            Ensure();
            Place(scorches, MostScorches, scorchMaterial, at, radius * 2f, ScorchLife);
        }

        /// <summary>A splash of blood where an orc was hit hard or fell.</summary>
        public static void Blood(Vector3 at, float size)
        {
            Ensure();
            Place(blood, MostBlood, bloodMaterial, at, size, BloodLife);
        }

        static void Place(Queue<Mark> marks, int most, Material material, Vector3 at, float size, float life)
        {
            Ensure();
            // Laid on whatever is underneath: road, ledge or rock top, tilted to its slope.
            if (!Physics.Raycast(at + Vector3.up * 1.5f, Vector3.down, out var hit, 4f, Layers.GroundMask, QueryTriggerInteraction.Ignore)) return;
            Mark m;
            if (marks.Count >= most) m = marks.Dequeue();
            else
            {
                var go = new GameObject("Mark");
                go.transform.SetParent(runner.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var r = go.AddComponent<MeshRenderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                m = new Mark { Go = go, Renderer = r };
            }
            m.Renderer.sharedMaterial = material;
            block.SetColor("_Color", Color.white);
            m.Renderer.SetPropertyBlock(block);
            m.Go.SetActive(true);
            m.Go.transform.SetPositionAndRotation(hit.point + hit.normal * (0.02f + marks.Count * 0.00015f),
                Quaternion.FromToRotation(Vector3.up, hit.normal) * Quaternion.Euler(0, Random.Range(0f, 360f), 0));
            m.Go.transform.localScale = Vector3.one * size * Random.Range(0.85f, 1.15f);
            m.Born = Time.time;
            m.Life = life;
            marks.Enqueue(m);
        }

        // --- Bits in the air ---------------------------------------------------------

        /// <summary>Sparks off something hard: bright enough to glow, gone in a blink.</summary>
        public static void Sparks(Vector3 at, Vector3 normal, Color color, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var v = (normal + Random.insideUnitSphere * 0.9f).normalized * Random.Range(3f, 7f);
                Fx.Casing(at, v, color, 0.025f, Random.Range(0.12f, 0.3f), glow: 4f);
            }
        }

        /// <summary>Grit and chips thrown up by a hit or a blast, tumbling down.</summary>
        public static void Debris(Vector3 at, float force, Color color, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var v = (Vector3.up * 1.2f + Random.insideUnitSphere).normalized * force * Random.Range(0.5f, 1f);
                Fx.Casing(at + Vector3.up * 0.1f, v, color, Random.Range(0.04f, 0.12f), Random.Range(0.6f, 1.1f));
            }
        }

        /// <summary>A puff of dust where a shot meets the ground.</summary>
        public static void Dust(Vector3 at, Vector3 normal, Color color)
        {
            Fx.Puff(at + normal * 0.05f, normal * 0.8f + Random.insideUnitSphere * 0.3f, Random.Range(0.18f, 0.3f), color, 0.45f);
        }

        // --- Feel -------------------------------------------------------------------------

        /// <summary>Shakes the player's view by a blast: hard close up, nothing past its reach.</summary>
        public static void Shake(Vector3 at, float strength, float reach)
        {
            var cam = Camera.main;
            if (cam == null) return;
            float d = Vector3.Distance(cam.transform.position, at);
            if (d >= reach) return;
            CameraShake.Add(strength * (1f - d / reach));
        }

        /// <summary>The world holds still for a moment: a killing headshot, a boss going down.</summary>
        public static void HitStop(float seconds)
        {
            Ensure();
            // Only when time runs normally: not while paused, and not under the smoke test's fast-forward.
            if (!Mathf.Approximately(Time.timeScale, 1f)) return;
            runner.StartCoroutine(Hold(seconds));
        }

        static IEnumerator Hold(float seconds)
        {
            Time.timeScale = 0.06f;
            yield return new WaitForSecondsRealtime(seconds);
            if (Mathf.Approximately(Time.timeScale, 0.06f)) Time.timeScale = 1f;
        }

        // --- Making the pieces ----------------------------------------------------------

        static Mesh Quad()
        {
            var m = new Mesh { name = "Decal" };
            m.SetVertices(new List<Vector3> { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static Material Decal(Texture2D texture)
        {
            var m = new Material(Shader.Find("Sprites/Default")) { mainTexture = texture };
            m.renderQueue = 2450;
            return m;
        }

        /// <summary>A soot stain: black at the heart, broken and ragged at the rim.</summary>
        static Texture2D ScorchTexture()
        {
            const int N = 128;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (N - 1f) * 2 - 1, v = y / (N - 1f) * 2 - 1;
                float r = Mathf.Sqrt(u * u + v * v);
                float a = Mathf.Atan2(v, u);
                float edge = 0.72f + 0.22f * Mathf.PerlinNoise(Mathf.Cos(a) * 2 + 5, Mathf.Sin(a) * 2 + 5);
                float grain = Mathf.PerlinNoise(x * 0.09f, y * 0.09f);
                float alpha = Mathf.Clamp01((edge - r) / 0.35f) * (0.65f + 0.35f * grain);
                float ember = Mathf.Clamp01(0.25f - r) * 2f;
                px[y * N + x] = new Color(0.05f + ember * 0.35f, 0.04f + ember * 0.1f, 0.035f, alpha * 0.92f);
            }
            t.SetPixels(px);
            t.Apply(true);
            return t;
        }

        /// <summary>A splash of dark blood: a main pool with droplets thrown out round it.</summary>
        static Texture2D BloodTexture()
        {
            const int N = 128;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[N * N];
            var drops = new List<Vector3>();
            var rng = new System.Random(7);
            drops.Add(new Vector3(0, 0, 0.42f));
            for (int i = 0; i < 14; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2, d = 0.35f + (float)rng.NextDouble() * 0.5f;
                drops.Add(new Vector3(Mathf.Cos(a) * d, Mathf.Sin(a) * d, 0.04f + (float)rng.NextDouble() * 0.08f));
            }
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (N - 1f) * 2 - 1, v = y / (N - 1f) * 2 - 1;
                float field = 0;
                foreach (var d in drops)
                {
                    float dx = u - d.x, dy = v - d.y;
                    field += d.z * d.z / (dx * dx + dy * dy + 1e-4f);
                }
                field *= 0.8f + 0.4f * Mathf.PerlinNoise(x * 0.12f, y * 0.12f);
                float alpha = Mathf.Clamp01((field - 0.9f) * 3f) * Mathf.Clamp01((1 - Mathf.Sqrt(u * u + v * v)) * 4f);
                px[y * N + x] = new Color(0.28f, 0.02f, 0.02f, alpha * 0.9f);
            }
            t.SetPixels(px);
            t.Apply(true);
            return t;
        }
    }

    /// <summary>On the player's camera: the shake blasts add, worked into the view after the look is set.</summary>
    public sealed class CameraShake : MonoBehaviour
    {
        static float trauma;
        const float Decay = 1.6f, MostDegrees = 2.2f;

        public static void Add(float amount) => trauma = Mathf.Min(1f, trauma + amount);

        void LateUpdate()
        {
            if (trauma <= 0) return;
            trauma = Mathf.Max(0, trauma - Decay * Time.unscaledDeltaTime);
            // Squared, so a small knock is a tremor and a near miss really throws the view.
            float s = trauma * trauma, t = Time.unscaledTime * 22f;
            transform.localRotation *= Quaternion.Euler(
                (Mathf.PerlinNoise(t, 1) - 0.5f) * 2 * MostDegrees * s,
                (Mathf.PerlinNoise(t, 2) - 0.5f) * 2 * MostDegrees * s,
                (Mathf.PerlinNoise(t, 3) - 0.5f) * 2 * MostDegrees * 0.6f * s);
        }
    }
}
