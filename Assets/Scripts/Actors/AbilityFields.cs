using System.Collections.Generic;
using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// What the abilities leave behind in the world, after Orc Horde's
    /// AbilitySystem: oil slicks, mines, barrages and the fires they light,
    /// decoy beacons, and the Rallying Cry. Each works from the ability's stats
    /// as the skill tree has upgraded them when it was called in.
    ///
    /// All of it is cleared with the horde, so nothing outlives its round.
    /// </summary>
    public sealed class AbilityFields : MonoBehaviour
    {
        /// <summary>How often a slick or beacon re-applies its hold and rolls for slips, as Orc Horde's SLICK_TICK_MS.</summary>
        const float FieldTick = 0.1f;
        /// <summary>A hold lasts three ticks, so it never lapses between them.</summary>
        const float FieldHold = FieldTick * 3;
        const float MineArming = 0.3f;
        const float MineSpacing = 38 * U.Scale;
        const int MostMines = 24, MostSlicks = 8, MostFires = 40;
        /// <summary>Incendiary shells: how long the ground burns, and what it does a second.</summary>
        const float FireSeconds = 3f, FireDps = 24f, FireTick = 0.25f;
        /// <summary>How far apart a pair of sentries lands.</summary>
        const float SentrySpacing = 64 * U.Scale;

        public struct RallyBuff
        {
            public float Damage, FireRate, Range;
        }

        sealed class Slick { public Vector3 At; public float Radius, Until, Slow, Slip, SlipFor, NextTick; public GameObject Visual; }
        sealed class Mine { public Vector3 At; public float Radius, Trigger, Damage, ArmedAt, Pierce; public bool Chain; public GameObject Visual; public Renderer Lamp; }
        sealed class Barrage { public Vector3 At; public float Radius, Interval, NextShell, Damage, ShellRadius; public int ShellsLeft; public bool Incendiary; }
        sealed class Fire { public Vector3 At; public float Radius, Until, NextTick; }
        sealed class Beacon { public Vector3 At; public float Radius, Until, Slow, Damage, OverloadRadius, NextTick; public bool Overload, Bosses; public GameObject Visual; }

        static AbilityFields I;
        static readonly List<Slick> slicks = new List<Slick>();
        static readonly List<Mine> mines = new List<Mine>();
        static readonly List<Barrage> barrages = new List<Barrage>();
        static readonly List<Fire> fires = new List<Fire>();
        static readonly List<Beacon> beacons = new List<Beacon>();
        static RallyBuff rally;
        static float rallyUntil, rallyLength;

        /// <summary>The rally every tower is under right now: nothing once it has run out.</summary>
        public static RallyBuff Rally => Time.time < rallyUntil ? rally : default;

        /// <summary>The rally's time left, 0 to 1, for the HUD.</summary>
        public static float RallyLeft => Time.time < rallyUntil ? (rallyUntil - Time.time) / rallyLength : 0f;

        static void Ensure()
        {
            if (I == null) I = new GameObject("Ability fields").AddComponent<AbilityFields>();
        }

        /// <summary>Everything gone: the round is over.</summary>
        public static void Clear()
        {
            foreach (var s in slicks) if (s.Visual != null) Destroy(s.Visual);
            foreach (var m in mines) if (m.Visual != null) Destroy(m.Visual);
            foreach (var b in beacons) if (b.Visual != null) Destroy(b.Visual);
            foreach (var t in Tower.Sentries.ToArray()) if (t != null) Destroy(t.gameObject);
            slicks.Clear();
            mines.Clear();
            barrages.Clear();
            fires.Clear();
            beacons.Clear();
            rallyUntil = 0;
        }

        /// <summary>
        /// The beacon an orc is drawn to, if any: the nearest one it stands within
        /// reach of. Bosses aren't fooled unless the beacon has Fool Bosses.
        /// </summary>
        public static Vector3? LureFor(Enemy e)
        {
            Vector3? best = null;
            float bestDistance = float.MaxValue;
            foreach (var b in beacons)
            {
                if (Time.time >= b.Until || (e.Def.IsBoss && !b.Bosses)) continue;
                float d = Flat(e.transform.position - b.At).magnitude;
                if (d <= b.Radius && d < bestDistance) { bestDistance = d; best = b.At; }
            }
            return best;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        /// <summary>Every orc whose edge is within the circle, in reverse so a kill removing itself skips nobody.</summary>
        static IEnumerable<Enemy> Within(Vector3 at, float radius)
        {
            for (int i = Enemy.All.Count - 1; i >= 0; i--)
            {
                if (i >= Enemy.All.Count) continue;
                var e = Enemy.All[i];
                if (!e.Targetable || e.InTunnel) continue;
                float reach = radius + e.Def.Radius;
                if (Flat(e.transform.position - at).sqrMagnitude <= reach * reach) yield return e;
            }
        }

        // --- Calling them in ------------------------------------------------

        /// <summary>Emergency Repairs: patches every barricade in the circle, and with Rebuild raises the fallen.</summary>
        public static void Repair(Vector3 at, AbilityDef a)
        {
            float radius = Progression.AbilityRadius(a), share = Progression.AbilityRepair(a);
            bool rebuild = Progression.AbilityTrait(a, "rebuildWalls") > 0;
            foreach (var b in Barricade.All)
            {
                if (Flat(b.transform.position - at).magnitude > radius + 1f) continue;
                if (b.Standing) b.Repair(share);
                else if (rebuild) b.Restore();
                else continue;
                Fx.Burst(b.transform.position + Vector3.up, 1.5f, a.Color, 0.5f);
            }
            Fx.Ring(at + Vector3.up * 0.2f, radius, a.Color, 0.7f);
            Sfx.At("heal", at, 1.2f, 0.8f);
        }

        /// <summary>Oil Slick: a patch that slows what crosses it, and trips some of it.</summary>
        public static void Spill(Vector3 at, AbilityDef a)
        {
            Ensure();
            if (slicks.Count >= MostSlicks) { Destroy(slicks[0].Visual); slicks.RemoveAt(0); }
            float radius = Progression.AbilityRadius(a);
            slicks.Add(new Slick
            {
                At = at, Radius = radius, Until = Time.time + Progression.AbilityDuration(a),
                Slow = Mathf.Max(0.05f, a.SlowFactor - Progression.AbilityTrait(a, "slickStrength")),
                Slip = Progression.AbilitySlipChance(a), SlipFor = a.SlipDuration,
                Visual = GroundDisc("Oil slick", at, radius, new Color(0.08f, 0.06f, 0.12f, 0.85f), new Color(0.35f, 0.25f, 0.6f, 0.5f)),
            });
            Sfx.At("flame_burst", at, 0.7f, 0.5f);
        }

        /// <summary>Proximity Mines: a ring round the spot, waiting for the rest of the round.</summary>
        public static void LayMines(Vector3 at, AbilityDef a)
        {
            Ensure();
            int count = a.MineCount + Mathf.Max(0, Mathf.FloorToInt(Progression.AbilityTrait(a, "mineCount")));
            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.PI * 2 * i / count - Mathf.PI / 2;
                var spot = count == 1 ? at : at + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * MineSpacing;
                if (!OnRoad(spot)) spot = at;
                spot = Relief.OnGround(spot);
                if (mines.Count >= MostMines) { Destroy(mines[0].Visual); mines.RemoveAt(0); }
                var visual = new GameObject("Mine");
                visual.transform.position = spot;
                Shapes.Prim(PrimitiveType.Cylinder, visual.transform, Vector3.up * 0.04f, new Vector3(0.36f, 0.04f, 0.36f), Mats.Lit(U.Hex(0x30343a)));
                var lamp = Shapes.Prim(PrimitiveType.Sphere, visual.transform, Vector3.up * 0.1f, Vector3.one * 0.09f, Mats.Lit(a.Color, 2f));
                mines.Add(new Mine
                {
                    At = spot, Radius = Progression.AbilityRadius(a), Trigger = a.TriggerRadius, Damage = Progression.AbilityDamage(a),
                    ArmedAt = Time.time + MineArming, Chain = Progression.AbilityTrait(a, "mineChain") > 0,
                    Pierce = Mathf.Min(1f, Progression.AbilityTrait(a, "mineArmourPierce")),
                    Visual = visual, Lamp = lamp.GetComponent<Renderer>(),
                });
            }
            Sfx.At("ui_place", at, 0.8f, 1.3f);
        }

        /// <summary>Artillery Barrage: shells falling at random in the circle for a few seconds.</summary>
        public static void CallBarrage(Vector3 at, AbilityDef a)
        {
            Ensure();
            float interval = Progression.AbilityShellInterval(a);
            barrages.Add(new Barrage
            {
                At = at, Radius = Progression.AbilityRadius(a), Interval = interval, NextShell = Time.time + 0.6f,
                ShellsLeft = Mathf.Max(1, Mathf.RoundToInt(Progression.AbilityDuration(a) / interval)),
                Damage = Progression.AbilityDamage(a), ShellRadius = a.ShellRadius,
                Incendiary = Progression.AbilityTrait(a, "incendiary") > 0,
            });
            Fx.Ring(at + Vector3.up * 0.2f, Progression.AbilityRadius(a), a.Color, 1f);
            Sfx.At("mortar_whistle", at, 0.8f, 0.9f);
        }

        /// <summary>Decoy Beacon: a false base that pulls the horde off its route.</summary>
        public static void PlantBeacon(Vector3 at, AbilityDef a)
        {
            Ensure();
            float radius = Progression.AbilityRadius(a);
            var visual = new GameObject("Decoy beacon");
            visual.transform.position = at;
            Shapes.Prim(PrimitiveType.Cylinder, visual.transform, Vector3.up * 0.15f, new Vector3(1.2f, 0.15f, 1.2f), Mats.Lit(U.Hex(0x2a3038)));
            var beam = Shapes.Prim(PrimitiveType.Cylinder, visual.transform, Vector3.up * 3f, new Vector3(0.35f, 3f, 0.35f), Mats.Unlit(new Color(a.Color.r, a.Color.g, a.Color.b, 0.45f)));
            beam.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Shapes.Prim(PrimitiveType.Sphere, visual.transform, Vector3.up * 6.2f, Vector3.one * 0.7f, Mats.Lit(a.Color, 2.5f));
            var light = new GameObject("Glow").AddComponent<Light>();
            light.transform.SetParent(visual.transform, false);
            light.transform.localPosition = Vector3.up * 5f;
            light.color = a.Color;
            light.range = 9f;
            light.intensity = 1.6f;
            visual.AddComponent<Spin>();
            beacons.Add(new Beacon
            {
                At = at, Radius = radius, Until = Time.time + Progression.AbilityDuration(a),
                Slow = Mathf.Min(0.8f, Progression.AbilityTrait(a, "lureSlow")),
                Overload = Progression.AbilityTrait(a, "lureOverload") > 0,
                Bosses = Progression.AbilityTrait(a, "lurePullsBosses") > 0,
                Damage = Progression.AbilityDamage(a), OverloadRadius = a.OverloadRadius, Visual = visual,
            });
            Fx.Ring(at + Vector3.up * 0.3f, radius, a.Color, 0.9f);
            Sfx.At("heal", at, 1f, 0.6f);
            foreach (var e in Enemy.All) e.Repath();
        }

        /// <summary>Rallying Cry: every tower hits harder, and with upgrades fires faster and reaches further.</summary>
        public static void StartRally(AbilityDef a)
        {
            Ensure();
            rally = new RallyBuff
            {
                Damage = a.DamageBonus + Progression.AbilityTrait(a, "rallyDamage"),
                FireRate = Progression.AbilityTrait(a, "rallyFireRate"),
                Range = Progression.AbilityTrait(a, "rallyRange"),
            };
            rallyLength = Progression.AbilityDuration(a);
            rallyUntil = Time.time + rallyLength;
            foreach (var t in Tower.All) Fx.Ring(t.transform.position + Vector3.up * 0.3f, 1.6f, a.Color, 0.8f);
            Sfx.UI("horn_wave", 0.5f);
        }

        /// <summary>
        /// Sentry Drop: a Machine Gun (a Cannon, with Cannon Sentries) dropped
        /// from the sky, two side by side with Twin Sentries.
        /// </summary>
        public static void DropSentries(Vector3 at, Vector3 forward, AbilityDef a)
        {
            var def = Progression.AbilityTrait(a, "sentryCannon") > 0 ? Towers.Cannon : Towers.MachineGun;
            int count = 1 + Mathf.Max(0, Mathf.FloorToInt(Progression.AbilityTrait(a, "sentryCount")));
            var across = Vector3.Cross(Vector3.up, forward).normalized;
            for (int i = 0; i < count; i++)
            {
                var spot = at + across * (i - (count - 1) / 2f) * SentrySpacing;
                if (!OpenGround(spot)) spot = at;
                spot = GroundAt(spot);
                var t = Tower.Sentry(def, spot, Progression.AbilityDuration(a), 1f + Progression.AbilityBonus(a, "damage"));
                t.gameObject.AddComponent<Drop>();
            }
            Fx.Burst(at + Vector3.up * 0.5f, 1.4f, a.Color, 0.4f);
            Sfx.At("wall_hit", at, 1.2f, 0.7f);
        }

        // --- Where things can go ------------------------------------------

        /// <summary>Road: where the horde walks, and so where mines and slicks belong.</summary>
        public static bool OnRoad(Vector3 p) =>
            Physics.Raycast(new Vector3(p.x, 60f, p.z), Vector3.down, out var hit, 120f, Layers.GroundMask, QueryTriggerInteraction.Ignore) &&
            hit.collider.gameObject.layer == Layers.Road;

        /// <summary>Road or a ledge: anywhere a sentry can stand, but not on top of rock.</summary>
        public static bool OpenGround(Vector3 p) =>
            Physics.Raycast(new Vector3(p.x, 60f, p.z), Vector3.down, out var hit, 120f, Layers.GroundMask, QueryTriggerInteraction.Ignore) &&
            hit.collider.gameObject.layer != Layers.Rock;

        static Vector3 GroundAt(Vector3 p) =>
            Physics.Raycast(new Vector3(p.x, 60f, p.z), Vector3.down, out var hit, 120f, Layers.TerrainMask, QueryTriggerInteraction.Ignore)
                ? hit.point : Relief.OnGround(p);

        // --- Running them ---------------------------------------------------

        void Update()
        {
            if (GameManager.I == null || GameManager.I.Phase != Phase.Combat) return;
            float now = Time.time;
            TickSlicks(now);
            TickMines(now);
            TickBarrages(now);
            TickFires(now);
            TickBeacons(now);
        }

        static void TickSlicks(float now)
        {
            for (int i = slicks.Count - 1; i >= 0; i--)
            {
                var s = slicks[i];
                if (now >= s.Until) { Destroy(s.Visual); slicks.RemoveAt(i); continue; }
                // The last second it soaks away.
                Fade(s.Visual, Mathf.Clamp01(s.Until - now));
                if (now < s.NextTick) continue;
                s.NextTick = now + FieldTick;
                foreach (var e in Within(s.At, s.Radius))
                {
                    if (!e.Def.IsBoss && Random.value < s.Slip * FieldTick)
                    {
                        e.Slow(0f, s.SlipFor);
                        e.Animator?.Flinch(1f);
                    }
                    else e.Slow(s.Slow, FieldHold);
                }
            }
        }

        static void TickMines(float now)
        {
            if (mines.Count == 0) return;
            var going = new HashSet<Mine>();
            foreach (var m in mines)
            {
                // A slow blink, quicker once armed.
                m.Lamp.enabled = Mathf.Repeat(now * (now >= m.ArmedAt ? 2.5f : 8f), 1f) < 0.5f;
                if (now < m.ArmedAt) continue;
                foreach (var _ in Within(m.At, m.Trigger)) { going.Add(m); break; }
            }
            if (going.Count == 0) return;

            // Chain Reaction: a blast sets off every mine it reaches, and those in turn.
            var queue = new List<Mine>(going);
            for (int i = 0; i < queue.Count; i++)
            {
                if (!queue[i].Chain) continue;
                foreach (var other in mines)
                    if (!going.Contains(other) && Flat(other.At - queue[i].At).magnitude <= queue[i].Radius)
                    {
                        going.Add(other);
                        queue.Add(other);
                    }
            }
            foreach (var m in queue)
            {
                Damage.Splash(m.At, m.Radius, m.Damage, armourPierce: m.Pierce);
                Fx.Burst(m.At + Vector3.up * 0.4f, m.Radius, U.Hex(0xffa040), 0.4f);
                Fx.Burst(m.At + Vector3.up * 0.8f, m.Radius * 0.5f, U.Hex(0xfff0a0), 0.2f);
                Sfx.At("explosion", m.At, 0.9f, 1.15f);
                Combat.Flash(m.At + Vector3.up * 0.8f, U.Hex(0xff8a40), m.Radius * 4f, 4.5f, 0.3f);
                Combat.Scorch(m.At, m.Radius * 0.5f);
                Combat.Debris(m.At, 6f, U.Hex(0x6b5a48), 5);
                Combat.Shake(m.At, 0.3f, 16f);
                Destroy(m.Visual);
            }
            mines.RemoveAll(going.Contains);
        }

        static void TickBarrages(float now)
        {
            for (int i = barrages.Count - 1; i >= 0; i--)
            {
                var b = barrages[i];
                while (now >= b.NextShell && b.ShellsLeft > 0)
                {
                    b.NextShell += b.Interval;
                    b.ShellsLeft--;
                    // Uniform over the disc, not bunched at its middle.
                    float angle = Random.value * Mathf.PI * 2, distance = Mathf.Sqrt(Random.value) * b.Radius;
                    var impact = Relief.OnGround(b.At + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * distance);
                    Damage.Splash(impact, b.ShellRadius, b.Damage);
                    Fx.Line(impact + Vector3.up * 18f, impact, new Color(1f, 0.85f, 0.6f, 0.7f), 0.08f, 0.12f);
                    Fx.Burst(impact + Vector3.up * 0.4f, b.ShellRadius, U.Hex(0xffa040), 0.3f);
                    Sfx.At("explosion", impact, 0.7f, 1.1f);
                    Combat.Flash(impact + Vector3.up * 0.8f, U.Hex(0xffa040), b.ShellRadius * 5f, 4f, 0.25f);
                    Combat.Scorch(impact, b.ShellRadius * 0.5f);
                    Combat.Shake(impact, 0.15f, 20f);
                    if (b.Incendiary)
                    {
                        if (fires.Count >= MostFires) fires.RemoveAt(0);
                        fires.Add(new Fire { At = impact, Radius = b.ShellRadius * 0.8f, Until = now + FireSeconds });
                    }
                }
                if (b.ShellsLeft <= 0) barrages.RemoveAt(i);
            }
        }

        static void TickFires(float now)
        {
            for (int i = fires.Count - 1; i >= 0; i--)
            {
                var f = fires[i];
                if (now >= f.Until) { fires.RemoveAt(i); continue; }
                if (Random.value < 0.4f)
                    Fx.Puff(f.At + new Vector3(Random.Range(-f.Radius, f.Radius) * 0.6f, 0.1f, Random.Range(-f.Radius, f.Radius) * 0.6f),
                        Vector3.up * Random.Range(1f, 2f), Random.Range(0.25f, 0.45f), new Color(1f, Random.Range(0.35f, 0.7f), 0.1f, 0.6f), 0.4f);
                if (now < f.NextTick) continue;
                f.NextTick = now + FireTick;
                foreach (var e in Within(f.At, f.Radius))
                {
                    e.TakeDamage(FireDps * FireTick);
                    e.Burn(0.5f);
                }
            }
        }

        static void TickBeacons(float now)
        {
            bool ended = false;
            for (int i = beacons.Count - 1; i >= 0; i--)
            {
                var b = beacons[i];
                if (now >= b.Until)
                {
                    if (b.Overload)
                    {
                        Damage.Splash(b.At, b.OverloadRadius, b.Damage);
                        Fx.Burst(b.At + Vector3.up, b.OverloadRadius, U.Hex(0x5effc4), 0.5f);
                        Sfx.At("explosion", b.At, 1.1f, 0.8f);
                        Combat.Flash(b.At + Vector3.up, U.Hex(0x5effc4), b.OverloadRadius * 4f, 6f, 0.4f);
                        Combat.Scorch(b.At, b.OverloadRadius * 0.5f);
                        Combat.Shake(b.At, 0.45f, 22f);
                    }
                    else Fx.Burst(b.At + Vector3.up * 3f, 1.5f, U.Hex(0x5effc4), 0.4f);
                    Destroy(b.Visual);
                    beacons.RemoveAt(i);
                    ended = true;
                    continue;
                }
                if (b.Slow > 0 && now >= b.NextTick)
                {
                    b.NextTick = now + FieldTick;
                    foreach (var e in Within(b.At, b.Radius))
                        if (!e.Def.IsBoss || b.Bosses) e.Slow(1f - b.Slow, FieldHold);
                }
            }
            // The pull has gone: everything it held heads for the base again.
            if (ended)
                foreach (var e in Enemy.All) e.Repath();
        }

        // --- Visuals ----------------------------------------------------------

        /// <summary>A disc laid over the ground, for slicks: dark in the middle, a sheen at the rim.</summary>
        static GameObject GroundDisc(string name, Vector3 at, float radius, Color middle, Color rim)
        {
            const int Rings = 5, Spokes = 32;
            var verts = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();
            verts.Add(GroundAt(at) + Vector3.up * 0.05f);
            colors.Add(middle);
            for (int r = 1; r <= Rings; r++)
            for (int s = 0; s < Spokes; s++)
            {
                float a = s * Mathf.PI * 2 / Spokes;
                // A ragged edge, so it reads as a spill and not a stamp.
                float wobble = r == Rings ? 0.85f + Mathf.PerlinNoise(at.x + s * 0.7f, at.z) * 0.3f : 1f;
                var p = at + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius * r / Rings * wobble;
                verts.Add(GroundAt(p) + Vector3.up * 0.05f);
                colors.Add(Color.Lerp(middle, rim, Mathf.Pow(r / (float)Rings, 3)));
            }
            for (int s = 0; s < Spokes; s++)
            {
                tris.Add(0); tris.Add(1 + (s + 1) % Spokes); tris.Add(1 + s);
                for (int r = 1; r < Rings; r++)
                {
                    int a = 1 + (r - 1) * Spokes + s, b = 1 + (r - 1) * Spokes + (s + 1) % Spokes;
                    int c = a + Spokes, d = b + Spokes;
                    tris.Add(a); tris.Add(b); tris.Add(d);
                    tris.Add(a); tris.Add(d); tris.Add(c);
                }
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(Mats.Unlit(Color.white));
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        static void Fade(GameObject go, float alpha)
        {
            if (go == null) return;
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial.color = new Color(1, 1, 1, alpha);
        }

        /// <summary>Turns the beacon's head slowly, so the hologram reads as alive.</summary>
        sealed class Spin : MonoBehaviour
        {
            void Update() => transform.Rotate(0, 60f * Time.deltaTime, 0);
        }

        /// <summary>A sentry falls the last few metres and thumps down.</summary>
        sealed class Drop : MonoBehaviour
        {
            Vector3 ground;
            float t;

            void Start()
            {
                ground = transform.position;
                transform.position = ground + Vector3.up * 8f;
            }

            void Update()
            {
                t += Time.deltaTime / 0.35f;
                transform.position = Vector3.Lerp(ground + Vector3.up * 8f, ground, t * t);
                if (t < 1f) return;
                transform.position = ground;
                Fx.Burst(ground + Vector3.up * 0.2f, 1.2f, new Color(0.7f, 0.65f, 0.55f), 0.3f);
                Destroy(this);
            }
        }
    }
}
