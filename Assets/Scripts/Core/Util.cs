using System.Collections.Generic;
using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>Physics layers. The editor setup names them; the indices are what matter.</summary>
    public static class Layers
    {
        public const int Rock = 8, BuildGround = 9, Enemy = 10, Tower = 11, Wall = 12, Player = 13, Road = 14;

        public const int RockMask = 1 << Rock;
        public const int WallMask = 1 << Wall;
        public const int EnemyMask = 1 << Enemy;
        public const int TowerMask = 1 << Tower;
        public const int GroundMask = (1 << Road) | (1 << BuildGround) | (1 << Rock);
        /// <summary>The land itself, road and ledges: what hides an orc behind a rise.</summary>
        public const int TerrainMask = (1 << Road) | (1 << BuildGround);

        /// <summary>What a bullet can strike. Your own towers do not stop your fire.</summary>
        public const int ShotMask = GroundMask | WallMask | EnemyMask;

        public static void SetRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetRecursive(child.gameObject, layer);
        }
    }

    public static class Mats
    {
        static readonly Dictionary<(Color, float), Material> lit = new Dictionary<(Color, float), Material>();
        static readonly Dictionary<Color, Material> unlit = new Dictionary<Color, Material>();

        public static Material Lit(Color c, float emission = 0f)
        {
            if (lit.TryGetValue((c, emission), out var m)) return m;
            m = new Material(Shader.Find("Standard")) { color = c };
            m.SetFloat("_Glossiness", 0.15f);
            if (emission > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * emission);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            lit[(c, emission)] = m;
            return m;
        }

        static readonly Dictionary<(string, Color, Vector2), Material> textured = new Dictionary<(string, Color, Vector2), Material>();
        static readonly Dictionary<(string, Color), Material> terrain = new Dictionary<(string, Color), Material>();

        public static Texture2D Tex(string name) => Resources.Load<Texture2D>("Art/" + name);

        /// <summary>
        /// A Standard material with one of the baked textures and its normal map,
        /// tinted. <paramref name="tiling"/> repeats it across a primitive whose
        /// one-tile-per-face UVs would otherwise stretch it.
        /// </summary>
        public static Material Textured(string texture, Color tint, Vector2 tiling = default, float smoothness = 0.25f, float metallic = 0f)
        {
            if (tiling == default) tiling = Vector2.one;
            if (textured.TryGetValue((texture, tint, tiling), out var m)) return m;
            m = new Material(Shader.Find("Standard")) { color = tint, mainTexture = Tex(texture) };
            m.mainTextureScale = tiling;
            var normal = Tex(texture + "_n");
            if (normal != null)
            {
                m.SetTexture("_BumpMap", normal);
                m.SetTextureScale("_BumpMap", tiling);
                m.EnableKeyword("_NORMALMAP");
            }
            m.SetFloat("_Glossiness", smoothness);
            m.SetFloat("_Metallic", metallic);
            textured[(texture, tint, tiling)] = m;
            return m;
        }

        /// <summary>Terrain: a baked texture with its normal map, tinted, and varied per cell by the mesh's vertex colours.</summary>
        public static Material Terrain(string texture, Color tint, float bump = 1f)
        {
            if (terrain.TryGetValue((texture, tint), out var m)) return m;
            m = new Material(Shader.Find("OrcHorde/TexturedVertexLit")) { color = tint, mainTexture = Tex(texture) };
            var normal = Tex(texture + "_n");
            if (normal != null) m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", bump);
            terrain[(texture, tint)] = m;
            return m;
        }

        static Material vertexLit;

        /// <summary>Lit, coloured by the mesh's vertex colours: for the combined terrain.</summary>
        public static Material VertexLit() =>
            vertexLit != null ? vertexLit : vertexLit = new Material(Shader.Find("OrcHorde/VertexLit"));

        /// <summary>Unlit, alpha-blended and vertex-coloured: for effects, beams and ghosts.</summary>
        public static Material Unlit(Color c)
        {
            if (unlit.TryGetValue(c, out var m)) return m;
            m = new Material(Shader.Find("Sprites/Default")) { color = c };
            unlit[c] = m;
            return m;
        }
    }

    public static class Shapes
    {
        public static GameObject Prim(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale,
            Material mat, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        public static void StripColliders(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        }

        public static void Tint(GameObject go, Material mat)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r is LineRenderer) continue;
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
    }

    /// <summary>Throwaway visual effects built from primitives and line renderers.</summary>
    public static class Fx
    {
        public static void Burst(Vector3 pos, float radius, Color c, float life = 0.35f)
        {
            var go = Shapes.Prim(PrimitiveType.Sphere, null, pos, Vector3.one * radius * 0.4f, Mats.Unlit(Color.white));
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<FxFade>().Init(life, c, Vector3.one * radius * 2f, Vector3.zero);
        }

        public static void Puff(Vector3 pos, Vector3 velocity, float size, Color c, float life)
        {
            var go = Shapes.Prim(PrimitiveType.Sphere, null, pos, Vector3.one * size * 0.5f, Mats.Unlit(Color.white));
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<FxFade>().Init(life, c, Vector3.one * size * 1.6f, velocity);
        }

        public static void Line(Vector3 a, Vector3 b, Color c, float width, float life)
        {
            Polyline(new[] { a, b }, c, width, life);
        }

        public static void Polyline(Vector3[] points, Color c, float width, float life)
        {
            var go = new GameObject("fx-line");
            var lr = NewLine(go.transform, c, width);
            lr.positionCount = points.Length;
            lr.SetPositions(points);
            go.AddComponent<FxFade>().Init(life, c, Vector3.one, Vector3.zero);
        }

        /// <summary>A small cylinder flung out and falling: a spent cartridge case.</summary>
        public static void Casing(Vector3 pos, Vector3 velocity, Color c, float size, float life, float glow = 0f)
        {
            var go = Shapes.Prim(PrimitiveType.Cylinder, null, pos, new Vector3(size, size * 2f, size), Mats.Lit(c, glow));
            go.transform.rotation = Random.rotation;
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<FxFade>().Init(life, c, go.transform.localScale, velocity, gravity: 9.8f, fade: false, spin: 900f);
        }

        /// <summary>A jagged bolt of lightning between two points, with a thinner fork beside it.</summary>
        public static void Lightning(Vector3 a, Vector3 b, Color c)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                const int steps = 9;
                var pts = new Vector3[steps + 1];
                var d = b - a;
                var side = Vector3.Cross(d, Vector3.up).normalized;
                var up = Vector3.Cross(side, d).normalized;
                float jag = d.magnitude * (pass == 0 ? 0.07f : 0.12f);
                for (int i = 0; i <= steps; i++)
                {
                    float t = (float)i / steps;
                    float edge = Mathf.Sin(t * Mathf.PI);
                    pts[i] = a + d * t + (side * Random.Range(-1f, 1f) + up * Random.Range(-1f, 1f)) * jag * edge;
                }
                Polyline(pts, pass == 0 ? Color.Lerp(c, Color.white, 0.5f) : c, pass == 0 ? 0.06f : 0.025f, 0.14f);
            }
        }

        /// <summary>A flat ring that expands from its centre out to <paramref name="radius"/>.</summary>
        public static void Ring(Vector3 center, float radius, Color c, float life)
        {
            var go = new GameObject("fx-ring");
            go.transform.position = center;
            var lr = NewLine(go.transform, c, 0.12f);
            lr.useWorldSpace = false;
            SetCircle(lr, Vector3.zero, 1f);
            go.transform.localScale = Vector3.one * radius * 0.2f;
            go.AddComponent<FxFade>().Init(life, c, Vector3.one * radius, Vector3.zero);
        }

        public static LineRenderer NewLine(Transform parent, Color c, float width)
        {
            var lr = parent.gameObject.AddComponent<LineRenderer>();
            lr.sharedMaterial = Mats.Unlit(Color.white);
            lr.startColor = lr.endColor = c;
            lr.startWidth = lr.endWidth = width;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCapVertices = 2;
            return lr;
        }

        /// <summary>A circle laid on whatever ground lies under it, so a range ring runs up and down the hills.</summary>
        public static void SetGroundCircle(LineRenderer lr, Vector3 center, float radius, int segments = 48)
        {
            lr.loop = true;
            lr.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var p = center + new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius);
                // Road and ledges only: through rock the ring runs on at the land's height, hidden, rather than up the cliff.
                p.y = Physics.Raycast(new Vector3(p.x, 60f, p.z), Vector3.down, out var hit, 120f, Layers.TerrainMask, QueryTriggerInteraction.Ignore)
                    ? hit.point.y : Relief.Ground(p);
                lr.SetPosition(i, p + Vector3.up * 0.08f);
            }
        }

        /// <summary>A height note for a range: "+12% range from height", or empty on level ground.</summary>
        public static string HeightNote(float factor) =>
            Mathf.Abs(factor - 1f) < 0.02f ? "" : factor > 1f
                ? $"<color=#8fe08f>+{factor - 1f:P0} range from height</color>"
                : $"<color=#ff9a7a>{factor - 1f:P0} range, low ground</color>";

        public static void SetCircle(LineRenderer lr, Vector3 center, float radius, int segments = 48)
        {
            lr.loop = true;
            lr.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                lr.SetPosition(i, center + new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius));
            }
        }
    }

    public sealed class FxFade : MonoBehaviour
    {
        float life, t;
        Vector3 startScale, endScale, velocity;
        Color color;
        Renderer rend;
        LineRenderer line;
        MaterialPropertyBlock block;

        float gravity, spin;
        bool fade = true;

        public void Init(float life, Color c, Vector3 endScale, Vector3 velocity, float gravity = 0f, bool fade = true, float spin = 0f)
        {
            this.gravity = gravity;
            this.fade = fade;
            this.spin = spin;
            this.life = life;
            color = c;
            startScale = transform.localScale;
            this.endScale = endScale;
            this.velocity = velocity;
            line = GetComponent<LineRenderer>();
            rend = line == null ? GetComponent<Renderer>() : null;
            block = new MaterialPropertyBlock();
            Apply(0f);
        }

        void Update()
        {
            t += Time.deltaTime;
            float k = t / life;
            if (k >= 1f) { Destroy(gameObject); return; }
            velocity += Vector3.down * gravity * Time.deltaTime;
            transform.position += velocity * Time.deltaTime;
            // Lands on the land, wherever the hills put it.
            if (gravity > 0 && velocity.y < 0 && transform.position.y < Relief.Ground(transform.position) + 0.02f)
            {
                var p = transform.position;
                transform.position = new Vector3(p.x, Relief.Ground(p) + 0.02f, p.z);
                velocity = Vector3.zero; gravity = 0; spin = 0;
            }
            if (spin != 0) transform.Rotate(spin * Time.deltaTime, 0, spin * 0.6f * Time.deltaTime);
            if (fade) Apply(k);
        }

        void Apply(float k)
        {
            var c = color;
            c.a *= 1f - k;
            if (line != null)
            {
                line.startColor = line.endColor = c;
                if (startScale != endScale) transform.localScale = Vector3.Lerp(startScale, endScale, Mathf.Sqrt(k));
                return;
            }
            transform.localScale = Vector3.Lerp(startScale, endScale, Mathf.Sqrt(k));
            block.SetColor("_Color", c);
            rend.SetPropertyBlock(block);
        }
    }

    /// <summary>Armour as Orc Horde has it: subtracted per hit, but a hit always lands at least a quarter.</summary>
    public static class Damage
    {
        public static float AfterArmour(float raw, float armour) => Mathf.Max(raw - armour, raw * 0.25f);

        /// <summary>Hurts every orc within <paramref name="radius"/> of a point, on the flat.</summary>
        public static void Splash(Vector3 center, float radius, float damage, HashSet<Enemy> skip = null, float armourPierce = 0f)
        {
            // Backwards, so a kill removing itself (or a Splitter appending its brood) never skips anyone.
            for (int i = Enemy.All.Count - 1; i >= 0; i--)
            {
                if (i >= Enemy.All.Count) continue;
                var e = Enemy.All[i];
                // Blasts don't reach through the rock round a tunnel.
                if (!e.Targetable || e.InTunnel || (skip != null && skip.Contains(e))) continue;
                var d = e.transform.position - center;
                d.y = 0;
                float reach = radius + e.Def.Radius;
                if (d.sqrMagnitude > reach * reach) continue;
                skip?.Add(e);
                e.TakeDamage(damage, armourPierce: armourPierce);
            }
        }
    }
}
