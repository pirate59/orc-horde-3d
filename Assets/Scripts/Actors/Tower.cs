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
        TowerRig rig;
        float cooldown, nextRetarget;
        Enemy target;
        readonly HashSet<Enemy> struck = new HashSet<Enemy>();

        // Animation state.
        float spin, kick, flash, lastFired = -10f, idleSeed;
        MaterialPropertyBlock glowBlock;

        /// <summary>Fire whatever the phase: for the smoke test's armoury, which shows towers working between rounds.</summary>
        public static bool AlwaysArmed;

        public static Tower Create(TowerDef def, Vector3 position)
        {
            var go = new GameObject(def.Name);
            go.transform.position = position;
            var t = go.AddComponent<Tower>();
            t.Def = def;
            t.rig = TowerModels.Build(def, go.transform);
            t.head = t.rig.Yaw;
            t.muzzle = t.rig.Muzzle;
            t.idleSeed = Random.value * 10f;
            t.glowBlock = new MaterialPropertyBlock();
            var col = go.AddComponent<CapsuleCollider>();
            col.radius = def.Footprint;
            col.height = 2f;
            col.center = new Vector3(0, 1f, 0);
            Layers.SetRecursive(go, Layers.Tower);
            All.Add(t);
            return t;
        }

        /// <summary>The tower's look on its own: shared with the placement ghost.</summary>
        public static void BuildVisual(TowerDef def, Transform root) => TowerModels.Build(def, root);

        void Update()
        {
            Animate();
            if (GameManager.I.Phase != Phase.Combat && !AlwaysArmed) { target = null; return; }
            cooldown -= Time.deltaTime;

            if (Def.Kind == AttackKind.PulseAoe)
            {
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
            AimPitch();
            if (cooldown > 0f || Vector3.Angle(head.forward, to) > 12f) return;
            cooldown = Stats.FireInterval;
            lastFired = Time.time;

            switch (Def.Kind)
            {
                case AttackKind.Projectile when Def.Lobbed: FireShell(); break;
                case AttackKind.Projectile: FireBullet(); break;
                case AttackKind.Hitscan: FireBeam(); break;
                case AttackKind.ConeAoe: FireCone(); break;
            }
        }

        /// <summary>
        /// Tilts the weapon. The Cannon elevates to lob, the farther the higher,
        /// so its shot reads as clearing walls; the rest point straight at the orc.
        /// </summary>
        void AimPitch()
        {
            if (rig.Pitch == null || target == null) return;
            float pitch;
            if (Def.Lobbed) pitch = -(12f + 33f * Mathf.Clamp01(FlatDistance(target.transform.position) / Stats.Range));
            else
            {
                var d = target.AimPoint - rig.Pitch.position;
                float flat = new Vector2(d.x, d.z).magnitude;
                pitch = Mathf.Clamp(-Mathf.Atan2(d.y, flat) * Mathf.Rad2Deg, -30f, 40f);
            }
            rig.Pitch.localRotation = Quaternion.RotateTowards(rig.Pitch.localRotation, Quaternion.Euler(pitch, 0, 0), 180f * Time.deltaTime);
        }

        /// <summary>
        /// What makes each tower read as working: the gun's barrels spin up, the
        /// cannon's barrel slides back and returns, the laser's crystal charges,
        /// the coil's toroid turns and flares, the pilot light flickers. With
        /// nothing to shoot, the turret sweeps slowly to and fro.
        /// </summary>
        void Animate()
        {
            float dt = Time.deltaTime;
            bool busy = Time.time - lastFired < 0.4f;
            flash = Mathf.MoveTowards(flash, 0f, dt * 4f);

            if (rig.Spinner != null)
            {
                spin = Mathf.MoveTowards(spin, busy ? 1500f : 0f, (busy ? 3000f : 900f) * dt);
                rig.Spinner.Rotate(0, 0, spin * dt, Space.Self);
            }
            if (rig.Recoil != null)
            {
                kick = Mathf.MoveTowards(kick, 0f, dt * 0.9f);
                rig.Recoil.localPosition = new Vector3(0, 0, -kick);
            }
            if (rig.Coil != null) rig.Coil.Rotate(0, (40f + flash * 400f) * dt, 0, Space.Self);

            if (rig.Glow != null)
            {
                float level = Def.Kind switch
                {
                    AttackKind.Hitscan => 0.3f + 1.6f * (1f - Mathf.Clamp01(cooldown / Mathf.Max(Stats.FireInterval, 0.01f))) + flash * 2f,
                    AttackKind.PulseAoe => 0.5f + 0.3f * Mathf.Sin(Time.time * 7f + idleSeed) + flash * 3f,
                    _ => 0.6f + Random.Range(0f, 0.5f) + (busy ? 1.2f : 0f),
                };
                glowBlock.SetColor("_Flash", rig.GlowColor * level);
                rig.Glow.SetPropertyBlock(glowBlock);
            }

            if (target == null && Def.Kind != AttackKind.PulseAoe)
            {
                var idle = Quaternion.Euler(0, Mathf.Sin(Time.time * 0.35f + idleSeed) * 55f, 0);
                head.localRotation = Quaternion.RotateTowards(head.localRotation, idle, 40f * dt);
                if (rig.Pitch != null)
                    rig.Pitch.localRotation = Quaternion.RotateTowards(rig.Pitch.localRotation, Quaternion.Euler(Def.Lobbed ? -10f : 4f, 0, 0), 30f * dt);
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
            Fx.Burst(muzzle.position, 0.28f, U.Hex(0xffd27a), 0.05f);
            // Spent brass, out of the right side and onto the ground.
            if (rig.Ejector != null && Random.value < 0.6f)
                Fx.Casing(rig.Ejector.position, rig.Yaw.right * Random.Range(1.2f, 2f) + Vector3.up * Random.Range(1.5f, 2.5f) - rig.Yaw.forward * 0.5f,
                    U.Hex(0xc8a050), 0.03f, 0.9f);
        }

        void FireShell()
        {
            var aim = Lead(target, Def.ProjectileSpeed);
            aim.y = target.transform.position.y;
            Shell.Lob(muzzle.position, aim, Def.ProjectileSpeed, Stats.Splash, Stats.Damage, Def.Color);
            Fx.Burst(muzzle.position, 0.6f, U.Hex(0xffd27a), 0.1f);
            kick = 0.24f;
            // A gout of smoke from the muzzle, drifting up.
            for (int i = 0; i < 5; i++)
                Fx.Puff(muzzle.position, muzzle.forward * Random.Range(1f, 2.5f) + Random.insideUnitSphere * 0.6f + Vector3.up * 0.6f,
                    Random.Range(0.4f, 0.8f), new Color(0.55f, 0.53f, 0.5f, 0.7f), Random.Range(0.6f, 1.1f));
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
            Fx.Polyline(points.ToArray(), Color.white, 0.025f, 0.06f);
            for (int i = 2; i < points.Count; i++) Fx.Burst(points[i], 0.25f, Def.Color, 0.12f);
            flash = 1f;
        }

        void Pulse()
        {
            foreach (var e in Enemy.All.ToArray())
            {
                if (!Valid(e)) continue;
                e.Slow(Def.SlowFactor, Stats.SlowDuration);
                e.TakeDamage(Stats.Damage, from: transform.position);
                // A bolt from the crystal to every orc caught in the pulse.
                if (e.Targetable) Fx.Lightning(muzzle.position, e.AimPoint, Def.Color);
            }
            Fx.Ring(transform.position + Vector3.up * 0.4f, Stats.Range, Def.Color, 0.45f);
            flash = 1f;
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
                e.Burn(0.8f);
            }
            // The jet: many small, see-through tongues, hotter (yellower) nearer the nozzle.
            for (int i = 0; i < 6; i++)
            {
                var spread = Quaternion.Euler(Random.Range(-4f, 4f), Random.Range(-half, half) * 0.8f, 0);
                var v = spread * muzzle.forward * Stats.Range * Random.Range(1.8f, 2.6f);
                var c = Color.Lerp(U.Hex(0xffe060), U.Hex(0xff3a10), Random.value);
                c.a = 0.55f;
                Fx.Puff(muzzle.position + v * 0.02f, v, Random.Range(0.15f, 0.35f), c, Random.Range(0.3f, 0.45f));
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
