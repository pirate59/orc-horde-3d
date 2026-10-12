using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OrcHordeFps
{
    /// <summary>
    /// The look of weapons firing. A handful of shared particle systems (flame, smoke, sparks,
    /// disturbed air) that every gun and tower emits into, so a whole battle is a few draw calls
    /// and no objects made or destroyed per shot, plus glowing beams and bolts drawn as lines.
    ///
    /// Glow uses Shaders/FxGlow (added light, bright enough to bloom), smoke Shaders/FxSoft, and
    /// air Shaders/AirWake, which bends the scene behind it rather than drawing anything itself.
    /// </summary>
    public static class WeaponFx
    {
        static ParticleSystem flame, smoke, spark, air;
        static Material glow, soft, wake;
        static Texture2D dot;
        static GameObject root;

        /// <summary>A soft round shape: full in the middle, fading to nothing at the rim.</summary>
        public static Texture2D Dot
        {
            get
            {
                if (dot != null) return dot;
                const int N = 64;
                dot = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color[N * N];
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2 - 1, dy = (y + 0.5f) / N * 2 - 1;
                    float a = Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy));
                    px[y * N + x] = new Color(1, 1, 1, a * a * (3 - 2 * a));
                }
                dot.SetPixels(px);
                dot.Apply(true);
                return dot;
            }
        }

        public static Material Glow => glow != null ? glow : glow = Make("OrcHorde/FxGlow", Dot);
        public static Material Soft => soft != null ? soft : soft = Make("OrcHorde/FxSoft", Dot);
        public static Material Wake => wake != null ? wake : wake = Make("OrcHorde/AirWake", Dot);

        /// <summary>A glow material for lines: a plain white strip, so the line's own width and colour show.</summary>
        static Material lineGlow;
        public static Material LineGlow => lineGlow != null ? lineGlow : lineGlow = Make("OrcHorde/FxGlow", Texture2D.whiteTexture);

        static Material Make(string shader, Texture tex)
        {
            var m = new Material(Shader.Find(shader)) { mainTexture = tex };
            return m;
        }

        static void Ensure()
        {
            if (root != null) return;
            root = new GameObject("Weapon effects");
            // Flame: glowing, growing and rising, burning from yellow-white through orange to a dull red.
            flame = System("Flame", Glow, 6000, ParticleSystemRenderMode.Billboard, g =>
            {
                g.SetKeys(
                    new[] { new GradientColorKey(new Color(1f, 0.95f, 0.7f), 0), new GradientColorKey(new Color(1f, 0.55f, 0.15f), 0.35f), new GradientColorKey(new Color(0.7f, 0.12f, 0.03f), 1) },
                    new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.08f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0, 1) });
            }, grow: 2.2f);
            // Smoke: soft, swelling and fading.
            smoke = System("Smoke", Soft, 3000, ParticleSystemRenderMode.Billboard, g =>
            {
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(0, 1) });
            }, grow: 2.8f);
            // Sparks: hot streaks that fall.
            spark = System("Sparks", Glow, 3000, ParticleSystemRenderMode.Stretch, g =>
            {
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 0.6f), new GradientAlphaKey(0, 1) });
            }, grow: 0.6f, gravity: 1f);
            var sr = spark.GetComponent<ParticleSystemRenderer>();
            sr.velocityScale = 0.03f;
            sr.lengthScale = 1.5f;
            // Air: shimmer that swells and settles.
            air = System("Air", Wake, 2000, ParticleSystemRenderMode.Billboard, g =>
            {
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.2f), new GradientAlphaKey(0, 1) });
            }, grow: 1.8f);
        }

        static ParticleSystem System(string name, Material mat, int max, ParticleSystemRenderMode mode, System.Action<Gradient> fade, float grow, float gravity = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.gravityModifier = gravity;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            fade(g);
            col.color = g;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, grow));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = mode;
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.Distance;
            ps.Play();
            return ps;
        }

        static readonly string[] Args = global::System.Environment.GetCommandLineArgs();
        static readonly bool NoAir = global::System.Array.IndexOf(Args, "-fxnoair") >= 0, NoFlame = global::System.Array.IndexOf(Args, "-fxnoflame") >= 0,
            NoTrail = global::System.Array.IndexOf(Args, "-fxnotrail") >= 0;

        static void Emit(ParticleSystem ps, Vector3 at, Vector3 velocity, float size, Color color, float life, float spin = 0f)
        {
            if ((NoAir && ps == air) || (NoFlame && ps == flame)) return;
            var p = new ParticleSystem.EmitParams
            {
                position = at,
                velocity = velocity,
                startSize = size,
                startColor = color,
                startLifetime = life,
                rotation = Random.Range(0f, 360f),
                angularVelocity = spin,
                applyShapeToPosition = false,
            };
            ps.Emit(p, 1);
        }

        public static void Flame(Vector3 at, Vector3 velocity, float size, float life, Color tint) { Ensure(); Emit(flame, at, velocity, size, tint, life, Random.Range(-90f, 90f)); }
        public static void Flame(Vector3 at, Vector3 velocity, float size, float life) => Flame(at, velocity, size, life, Color.white);
        public static void Smoke(Vector3 at, Vector3 velocity, float size, float life, Color tint) { Ensure(); Emit(smoke, at, velocity, size, tint, life, Random.Range(-40f, 40f)); }
        public static void Spark(Vector3 at, Vector3 velocity, float size, float life, Color tint) { Ensure(); Emit(spark, at, velocity, size, tint, life); }
        public static void Air(Vector3 at, Vector3 velocity, float size, float life, float strength = 1f) { Ensure(); Emit(air, at, velocity, size, new Color(1, 1, 1, strength), life); }

        /// <summary>A burst of sparks out of a point, mostly along a direction.</summary>
        public static void Sparks(Vector3 at, Vector3 along, Color tint, int count, float speed = 6f)
        {
            for (int i = 0; i < count; i++)
                Spark(at, (along.normalized + Random.insideUnitSphere * 0.8f).normalized * speed * Random.Range(0.5f, 1.2f), Random.Range(0.03f, 0.06f), Random.Range(0.15f, 0.4f), tint);
        }

        /// <summary>A fireball: hot flame swelling from a point, black smoke rolling up after it, sparks flung out, and the air shoved aside.</summary>
        public static void Fireball(Vector3 at, float radius)
        {
            for (int i = 0; i < 14; i++)
                Flame(at + Random.insideUnitSphere * radius * 0.25f, Random.insideUnitSphere * radius * 2.2f + Vector3.up * radius, radius * Random.Range(0.5f, 0.9f), Random.Range(0.25f, 0.45f));
            for (int i = 0; i < 8; i++)
                Smoke(at + Random.insideUnitSphere * radius * 0.4f + Vector3.up * radius * 0.3f, Vector3.up * Random.Range(1.5f, 3f) + Random.insideUnitSphere * radius,
                    radius * Random.Range(0.8f, 1.3f), Random.Range(1.4f, 2.4f), new Color(0.16f, 0.14f, 0.13f, 0.75f));
            Sparks(at, Vector3.up, new Color(1f, 0.7f, 0.3f), 18, radius * 7f);
            Air(at, Vector3.zero, radius * 3f, 0.35f, 1f);
        }

        // --- Beams ----------------------------------------------------------------------------------

        /// <summary>
        /// A glowing beam along a path: a wide soft halo, a coloured body and a white-hot core,
        /// each narrowing and dimming over its life.
        /// </summary>
        public static void Beam(Vector3[] points, Color color, float width, float life)
        {
            Layer(points, color * 0.18f, width * 2.2f, life * 1.3f);
            Layer(points, color, width, life);
            Layer(points, Color.Lerp(color, Color.white, 0.6f) * 0.8f, width * 0.35f, life * 0.8f);
        }

        static void Layer(Vector3[] points, Color color, float width, float life)
        {
            var go = new GameObject("fx-beam");
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = LineGlow;
            lr.positionCount = points.Length;
            lr.SetPositions(points);
            lr.startWidth = lr.endWidth = width;
            lr.startColor = lr.endColor = color;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 2;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            go.AddComponent<BeamFade>().Init(lr, color, width, life);
        }

        /// <summary>Narrows and dims a beam over its life, with a flicker in its width.</summary>
        sealed class BeamFade : MonoBehaviour
        {
            LineRenderer lr;
            Color color;
            float width, life, t;

            public void Init(LineRenderer line, Color c, float w, float l)
            {
                lr = line;
                color = c;
                width = w;
                life = l;
            }

            void Update()
            {
                t += Time.deltaTime;
                float k = t / life;
                if (k >= 1f) { Destroy(gameObject); return; }
                float flicker = 0.85f + 0.15f * Mathf.Sin(t * 90f);
                lr.startWidth = lr.endWidth = width * (1f - k * 0.7f) * flicker;
                var c = color;
                c.a = (1f - k) * (1f - k);
                lr.startColor = lr.endColor = c;
            }
        }

        /// <summary>
        /// A lightning bolt between two points, forked, that flickers and re-forks over its short
        /// life, crackling sparks where it lands.
        /// </summary>
        public static void Bolt(Vector3 from, Vector3 to, Color color, float life = 0.18f)
        {
            Ensure();
            var go = new GameObject("fx-bolt");
            var bolt = go.AddComponent<BoltFlicker>();
            bolt.Init(from, to, color, life);
            Sparks(to, Vector3.up, Color.Lerp(color, Color.white, 0.5f), 4, 4f);
        }

        sealed class BoltFlicker : MonoBehaviour
        {
            readonly List<LineRenderer> strands = new List<LineRenderer>();
            Vector3 from, to;
            Color color;
            float life, t, next;

            public void Init(Vector3 a, Vector3 b, Color c, float l)
            {
                from = a;
                to = b;
                color = c;
                life = l;
                // The main bolt with its glow, and a thinner fork wandering off it.
                foreach (var (w, tint) in new[] { (0.16f, c * 0.18f), (0.05f, c * 0.6f), (0.02f, Color.Lerp(c, Color.white, 0.6f) * 0.8f), (0.025f, c * 0.5f) })
                {
                    // One line per object, so each strand is a child.
                    var child = new GameObject("strand");
                    child.transform.SetParent(transform, false);
                    var lr = child.AddComponent<LineRenderer>();
                    lr.sharedMaterial = LineGlow;
                    lr.startWidth = lr.endWidth = w;
                    lr.startColor = lr.endColor = tint;
                    lr.numCapVertices = 2;
                    lr.shadowCastingMode = ShadowCastingMode.Off;
                    strands.Add(lr);
                }
                Reshape();
            }

            void Reshape()
            {
                const int Steps = 11;
                var d = to - from;
                var side = Vector3.Cross(d, Vector3.up).normalized;
                if (side.sqrMagnitude < 0.01f) side = Vector3.right;
                var up = Vector3.Cross(side, d).normalized;
                float jag = d.magnitude * 0.09f;
                var main = new Vector3[Steps + 1];
                for (int i = 0; i <= Steps; i++)
                {
                    float k = (float)i / Steps;
                    main[i] = from + d * k + (side * Random.Range(-1f, 1f) + up * Random.Range(-1f, 1f)) * jag * Mathf.Sin(k * Mathf.PI);
                }
                for (int s = 0; s < 3; s++) { strands[s].positionCount = main.Length; strands[s].SetPositions(main); }
                // The fork leaves the bolt part way along and trails off to one side.
                int at = Random.Range(3, 7);
                var fork = new Vector3[5];
                var dir = (d.normalized + (side * Random.Range(-1f, 1f) + up * Random.Range(-0.5f, 1f)) * 0.9f).normalized;
                for (int i = 0; i < fork.Length; i++)
                    fork[i] = main[at] + dir * d.magnitude * 0.08f * i + Random.insideUnitSphere * jag * 0.4f;
                strands[3].positionCount = fork.Length;
                strands[3].SetPositions(fork);
            }

            void Update()
            {
                t += Time.deltaTime;
                if (t >= life) { Destroy(gameObject); return; }
                if (t >= next) { next = t + 0.04f; Reshape(); }
                float a = 1f - t / life;
                for (int s = 0; s < strands.Count; s++)
                {
                    var c = strands[s].startColor;
                    c.a = a;
                    strands[s].startColor = strands[s].endColor = c;
                }
            }
        }

        // --- Trails ---------------------------------------------------------------------------------

        /// <summary>
        /// A trail of disturbed air behind something fast: the ripple a bullet leaves rather than
        /// the bullet itself. Optionally a glowing tracer streak too.
        /// </summary>
        public static void AirTrail(GameObject on, float width, float time, Color tracer = default)
        {
            if (NoTrail) return;
            var t = on.AddComponent<TrailRenderer>();
            t.sharedMaterial = Wake;
            t.time = time;
            t.startWidth = width;
            t.endWidth = width * 0.2f;
            t.colorGradient = Fade(Color.white, 0.9f);
            t.minVertexDistance = 0.3f;
            t.shadowCastingMode = ShadowCastingMode.Off;
            t.receiveShadows = false;
            if (tracer.a <= 0) return;
            var child = new GameObject("tracer");
            child.transform.SetParent(on.transform, false);
            var g = child.AddComponent<TrailRenderer>();
            g.sharedMaterial = LineGlow;
            g.time = time * 0.5f;
            g.startWidth = width * 0.35f;
            g.endWidth = 0f;
            g.colorGradient = Fade(tracer, 1f);
            g.minVertexDistance = 0.2f;
            g.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>A trail of smoke behind a shell.</summary>
        public static void SmokeTrail(GameObject on, float width, float time)
        {
            var t = on.AddComponent<TrailRenderer>();
            t.sharedMaterial = Soft;
            t.time = time;
            t.startWidth = width * 0.5f;
            t.endWidth = width * 2.5f;
            t.colorGradient = Fade(new Color(0.55f, 0.53f, 0.5f), 0.55f);
            t.minVertexDistance = 0.25f;
            t.shadowCastingMode = ShadowCastingMode.Off;
            t.receiveShadows = false;
        }

        static Gradient Fade(Color c, float alpha)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0), new GradientColorKey(c, 1) },
                new[] { new GradientAlphaKey(alpha, 0), new GradientAlphaKey(0, 1) });
            return g;
        }
    }
}
