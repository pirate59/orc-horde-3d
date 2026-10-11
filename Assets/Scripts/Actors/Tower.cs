using System.Collections.Generic;
using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// One of the five Orc Horde towers. Each attack model follows the original:
    /// bullets stop at the first unit, shells lob over barricades and splash,
    /// the laser pierces and ricochets, the arc pulses a slow, the flame burns a cone.
    /// </summary>
    public sealed class Tower : MonoBehaviour
    {
        public static readonly List<Tower> All = new List<Tower>();

        const float RetargetInterval = 0.2f;
        const float TurnDegreesPerSecond = 540f;
        /// <summary>The laser's plane: chest height on every orc, above the shoulders' lip.</summary>
        const float BeamHeight = 1.1f;

        public TowerDef Def { get; private set; }
        /// <summary>The tower-ground cell it stands on, for saving the layout.</summary>
        public Vector2Int Cell { get; set; }

        LineRenderer rangeRing;

        /// <summary>Shows or hides this tower's reach on the ground, drawn at its current upgraded range.</summary>
        public void ShowRange(bool show)
        {
            if (rangeRing == null)
            {
                if (!show) return;
                var go = new GameObject("Range");
                go.transform.SetParent(transform, false);
                var c = Def.Color;
                c.a = 0.35f;
                rangeRing = Fx.NewLine(go.transform, c, 0.05f);
                rangeRing.enabled = false;
            }
            if (show && !rangeRing.enabled) Fx.SetCircle(rangeRing, transform.position + Vector3.up * 0.06f, Stats.Range, 64);
            rangeRing.enabled = show;
        }
        /// <summary>This tower's numbers with the skill tree applied.</summary>
        public TowerStats Stats => Progression.Tower(Def);

        Transform head, muzzle;
        float cooldown, nextRetarget;
        Enemy target;
        readonly HashSet<Enemy> struck = new HashSet<Enemy>();

        public static Tower Create(TowerDef def, Vector3 position)
        {
            var go = new GameObject(def.Name);
            go.transform.position = position;
            var t = go.AddComponent<Tower>();
            t.Def = def;
            BuildVisual(def, go.transform, out t.head, out t.muzzle);
            var col = go.AddComponent<CapsuleCollider>();
            col.radius = def.Footprint;
            col.height = 2.4f;
            col.center = new Vector3(0, 1.2f, 0);
            Layers.SetRecursive(go, Layers.Tower);
            All.Add(t);
            return t;
        }

        /// <summary>The tower's look on its own: shared with the placement ghost.</summary>
        public static void BuildVisual(TowerDef def, Transform root, out Transform head, out Transform muzzle)
        {
            float r = def.Footprint;
            var metal = Mats.Lit(U.Hex(0x3b4047));
            var paint = Mats.Lit(def.Color);
            var glow = Mats.Lit(def.Color, 2f);

            Shapes.Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0.15f, 0), new Vector3(r * 2, 0.15f, r * 2), metal);
            Shapes.Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0.7f, 0), new Vector3(r * 1.1f, 0.4f, r * 1.1f), paint);
            Shapes.Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0.32f, 0), new Vector3(r * 2.05f, 0.02f, r * 2.05f), glow);

            head = new GameObject("Head").transform;
            head.SetParent(root, false);
            head.localPosition = new Vector3(0, 1.35f, 0);
            Shapes.Prim(PrimitiveType.Cube, head, Vector3.zero, new Vector3(r * 1.3f, 0.5f, r * 1.3f), paint);

            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(head, false);

            switch (def.Kind)
            {
                case AttackKind.Projectile when def.Lobbed:
                    var barrel = Shapes.Prim(PrimitiveType.Cylinder, head, new Vector3(0, 0.15f, 0.5f), new Vector3(0.35f, 0.5f, 0.35f), metal);
                    barrel.transform.localRotation = Quaternion.Euler(60, 0, 0);
                    muzzle.localPosition = new Vector3(0, 0.55f, 0.85f);
                    break;
                case AttackKind.Projectile:
                    foreach (var x in new[] { -0.12f, 0.12f })
                    {
                        var b = Shapes.Prim(PrimitiveType.Cylinder, head, new Vector3(x, 0.05f, 0.6f), new Vector3(0.1f, 0.45f, 0.1f), metal);
                        b.transform.localRotation = Quaternion.Euler(90, 0, 0);
                    }
                    muzzle.localPosition = new Vector3(0, 0.05f, 1.05f);
                    break;
                case AttackKind.Hitscan:
                    var lens = Shapes.Prim(PrimitiveType.Cylinder, head, new Vector3(0, 0.05f, 0.45f), new Vector3(0.22f, 0.3f, 0.22f), metal);
                    lens.transform.localRotation = Quaternion.Euler(90, 0, 0);
                    Shapes.Prim(PrimitiveType.Sphere, head, new Vector3(0, 0.05f, 0.76f), Vector3.one * 0.18f, glow);
                    muzzle.localPosition = new Vector3(0, 0.05f, 0.8f);
                    break;
                case AttackKind.PulseAoe:
                    Shapes.Prim(PrimitiveType.Cylinder, head, new Vector3(0, 0.45f, 0), new Vector3(0.12f, 0.35f, 0.12f), metal);
                    Shapes.Prim(PrimitiveType.Sphere, head, new Vector3(0, 0.9f, 0), Vector3.one * 0.45f, glow);
                    muzzle.localPosition = new Vector3(0, 0.9f, 0);
                    break;
                case AttackKind.ConeAoe:
                    var nozzle = Shapes.Prim(PrimitiveType.Cylinder, head, new Vector3(0, 0, 0.5f), new Vector3(0.28f, 0.3f, 0.28f), metal);
                    nozzle.transform.localRotation = Quaternion.Euler(90, 0, 0);
                    Shapes.Prim(PrimitiveType.Cube, head, new Vector3(0, 0.2f, -0.35f), new Vector3(0.45f, 0.5f, 0.3f), glow);
                    muzzle.localPosition = new Vector3(0, 0, 0.82f);
                    break;
            }
        }

        void Update()
        {
            if (GameManager.I.Phase != Phase.Combat) return;
            cooldown -= Time.deltaTime;

            if (Def.Kind == AttackKind.PulseAoe)
            {
                head.Rotate(0, 90 * Time.deltaTime, 0);
                if (cooldown <= 0f && AnyInRange()) { cooldown = Stats.FireInterval; Pulse(); }
                return;
            }

            if (Time.time >= nextRetarget || !Valid(target))
            {
                nextRetarget = Time.time + RetargetInterval;
                target = Acquire();
            }
            if (target == null) return;

            var to = target.transform.position - head.position;
            to.y = 0;
            if (to.sqrMagnitude > 0.01f)
                head.rotation = Quaternion.RotateTowards(head.rotation, Quaternion.LookRotation(to), TurnDegreesPerSecond * Time.deltaTime);
            if (cooldown > 0f || Vector3.Angle(head.forward, to) > 12f) return;
            cooldown = Stats.FireInterval;

            switch (Def.Kind)
            {
                case AttackKind.Projectile when Def.Lobbed: FireShell(); break;
                case AttackKind.Projectile: FireBullet(); break;
                case AttackKind.Hitscan: FireBeam(); break;
                case AttackKind.ConeAoe: FireCone(); break;
            }
        }

        float FlatDistance(Vector3 p)
        {
            var d = p - transform.position;
            d.y = 0;
            return d.magnitude;
        }

        int SightMask => Def.Sight == SightBlockers.Rock ? Layers.RockMask : Layers.RockMask | Layers.WallMask;

        bool CanSee(Enemy e) => Def.SightPreferred ||
            !Physics.Linecast(muzzle.position, e.AimPoint, SightMask, QueryTriggerInteraction.Ignore);

        bool Valid(Enemy e) => e != null && e.Alive && e.Targetable && FlatDistance(e.transform.position) <= Stats.Range + e.Def.Radius;

        bool AnyInRange()
        {
            foreach (var e in Enemy.All)
                if (Valid(e)) return true;
            return false;
        }

        Enemy Acquire()
        {
            Enemy best = null;
            float bestScore = float.MaxValue;
            foreach (var e in Enemy.All)
            {
                if (!Valid(e) || !CanSee(e)) continue;
                float score = Def.TargetFirst ? e.Remaining : FlatDistance(e.transform.position);
                if (score < bestScore) { bestScore = score; best = e; }
            }
            return best;
        }

        void FireBullet()
        {
            var aim = Lead(target, Def.ProjectileSpeed);
            Projectile.Fire(muzzle.position, (aim - muzzle.position).normalized, Def, Stats.Damage, SightMask);
            Fx.Burst(muzzle.position, 0.25f, Def.Color, 0.06f);
        }

        void FireShell()
        {
            var aim = Lead(target, Def.ProjectileSpeed);
            aim.y = target.transform.position.y;
            Shell.Lob(muzzle.position, aim, Def.ProjectileSpeed, Stats.Splash, Stats.Damage, Def.Color);
            Fx.Burst(muzzle.position, 0.5f, U.Hex(0xffd27a), 0.12f);
        }

        void FireBeam()
        {
            struck.Clear();
            var origin = new Vector3(muzzle.position.x, BeamHeight, muzzle.position.z);
            var dir = target.transform.position - origin;
            dir.y = 0;
            dir = Quaternion.Euler(0, Random.Range(-Def.AimDither, Def.AimDither) * Mathf.Rad2Deg, 0) * dir.normalized;

            var points = new List<Vector3> { muzzle.position, origin };
            float budget = Stats.Range * Def.PathBudgetMultiplier;
            var p = origin;
            for (int bounce = 0; bounce <= 3 && budget > 0.05f; bounce++)
            {
                bool surface = Physics.Raycast(p, dir, out var hit, budget, Layers.RockMask | Layers.WallMask,
                    QueryTriggerInteraction.Ignore);
                float len = surface ? hit.distance : budget;
                foreach (var h in Physics.SphereCastAll(p, 0.15f, dir, len, Layers.EnemyMask, QueryTriggerInteraction.Ignore))
                {
                    var e = h.collider.GetComponentInParent<Enemy>();
                    if (e != null && struck.Add(e)) e.TakeDamage(Stats.Damage, from: p);
                }
                p += dir * len;
                points.Add(p);
                budget -= len;
                if (!surface) break;
                dir = Vector3.Reflect(dir, hit.normal);
                dir.y = 0;
                dir.Normalize();
                p += dir * 0.02f;
            }
            Fx.Polyline(points.ToArray(), Def.Color, 0.07f, 0.09f);
        }

        void Pulse()
        {
            foreach (var e in Enemy.All.ToArray())
            {
                if (!Valid(e)) continue;
                e.Slow(Def.SlowFactor, Stats.SlowDuration);
                e.TakeDamage(Stats.Damage, from: transform.position);
            }
            Fx.Ring(transform.position + Vector3.up * 0.6f, Stats.Range, Def.Color, 0.45f);
        }

        void FireCone()
        {
            float half = Stats.ConeDegrees / 2f;
            foreach (var e in Enemy.All.ToArray())
            {
                if (!Valid(e)) continue;
                var to = e.transform.position - transform.position;
                to.y = 0;
                if (Vector3.Angle(head.forward, to) > half || !CanSee(e)) continue;
                e.TakeDamage(Stats.Damage, from: transform.position);
            }
            for (int i = 0; i < 3; i++)
            {
                var spread = Quaternion.Euler(Random.Range(-4f, 4f), Random.Range(-half, half) * 0.8f, 0);
                var v = spread * head.forward * Stats.Range * Random.Range(1.6f, 2.4f);
                Fx.Puff(muzzle.position, v, Random.Range(0.3f, 0.6f),
                    Color.Lerp(U.Hex(0xffd23a), U.Hex(0xff4a1a), Random.value), 0.45f);
            }
        }

        /// <summary>Where to aim so a shot of this speed meets the target: one refinement is plenty at these ranges.</summary>
        Vector3 Lead(Enemy e, float speed)
        {
            var v = e.Velocity;
            float t = Vector3.Distance(muzzle.position, e.AimPoint) / speed;
            t = Vector3.Distance(muzzle.position, e.AimPoint + v * t) / speed;
            return e.AimPoint + v * t;
        }

        void OnDestroy() => All.Remove(this);
    }
}
