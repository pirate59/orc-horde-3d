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
            if (show && !rangeRing.enabled) Fx.SetGroundCircle(rangeRing, transform.position, Range, 64);
            rangeRing.enabled = show;
        }
        /// <summary>This tower's numbers with the skill tree applied.</summary>
        public TowerStats Stats => Progression.Tower(Def);

        /// <summary>What height adds to (or takes from) its reach: see <see cref="Relief.RangeFactor"/>.</summary>
        public float RangeFactor { get; private set; } = 1f;
        /// <summary>Its reach: the upgraded range, stretched or shrunk by the ground it stands on.</summary>
        public float Range => Stats.Range * RangeFactor * (1f + AbilityFields.Rally.Range);

        /// <summary>Its hit, with the Rallying Cry and, for a sentry, the drop's own multiplier.</summary>
        public float Damage => Stats.Damage * damageMultiplier * (1f + AbilityFields.Rally.Damage);

        /// <summary>Seconds between shots, quicker under a rally with fire rate in it.</summary>
        public float Interval => Stats.FireInterval / (1f + AbilityFields.Rally.FireRate);

        /// <summary>
        /// Dropped by Sentry Drop: on the field for a while, then gone. It isn't one
        /// of the player's towers: it counts against no limit, isn't saved with the
        /// layout and can't be picked up.
        /// </summary>
        public bool IsSentry { get; private set; }
        public static readonly List<Tower> Sentries = new List<Tower>();
        float damageMultiplier = 1f, expiresAt = float.MaxValue;

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

        public static Tower Sentry(TowerDef def, Vector3 position, float lifetime, float damageMultiplier)
        {
            var t = Create(def, position, sentry: true);
            t.expiresAt = Time.time + lifetime;
            t.damageMultiplier = damageMultiplier;
            return t;
        }

        public static Tower Create(TowerDef def, Vector3 position, bool sentry = false)
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
            if (!sentry && GameManager.I != null && GameManager.I.Grid != null)
                t.RangeFactor = Relief.RangeFactor(GameManager.I.Grid.CellAt(position), def.Range);
            var col = go.AddComponent<CapsuleCollider>();
            col.radius = def.Footprint;
            col.height = 2f;
            col.center = new Vector3(0, 1f, 0);
            Layers.SetRecursive(go, Layers.Tower);
            t.IsSentry = sentry;
            if (sentry) Sentries.Add(t);
            else All.Add(t);
            return t;
        }

        /// <summary>The tower's look on its own: shared with the placement ghost.</summary>
        public static void BuildVisual(TowerDef def, Transform root) => TowerModels.Build(def, root);

        void Update()
        {
            if (Time.time >= expiresAt)
            {
                // Powers down: a puff of smoke and it's gone.
                Fx.Burst(transform.position + Vector3.up, 1.2f, new Color(0.6f, 0.6f, 0.6f), 0.4f);
                Sfx.At("ui_remove", transform.position);
                Destroy(gameObject);
                return;
            }
            Animate();
            if (GameManager.I.Phase != Phase.Combat && !AlwaysArmed) { target = null; return; }
            cooldown -= Time.deltaTime;

            if (Def.Kind == AttackKind.PulseAoe)
            {
                if (cooldown <= 0f && AnyInRange()) { cooldown = Interval; Pulse(); }
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
            cooldown = Interval;
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
            if (Def.Lobbed) pitch = -(12f + 33f * Mathf.Clamp01(FlatDistance(target.transform.position) / Range));
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

        /// <summary>What blocks its view: rock, for some the barricades too, and always the land, so an orc over a crest is hidden.</summary>
        int SightMask => Layers.TerrainMask | (Def.Sight == SightBlockers.Rock ? Layers.RockMask : Layers.RockMask | Layers.WallMask);

        bool CanSee(Enemy e) => Def.SightPreferred ||
            !Physics.Linecast(muzzle.position, e.AimPoint, SightMask, QueryTriggerInteraction.Ignore);

        bool Valid(Enemy e) => e != null && e.Alive && e.Targetable && FlatDistance(e.transform.position) <= Range + e.Def.Radius;

        /// <summary>Within reach but behind the land: for the smoke test's count of blind spots.</summary>
        public bool HiddenByLand(Enemy e) => Valid(e) &&
            Physics.Linecast(muzzle.position, e.AimPoint, Layers.TerrainMask, QueryTriggerInteraction.Ignore);

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
            Projectile.Fire(muzzle.position, (aim - muzzle.position).normalized, Def, Damage, SightMask);
            Sfx.At("mg_shot", muzzle.position);
            if (Random.value < 0.35f) Combat.Flash(muzzle.position, U.Hex(0xffd27a), 4f, 1.6f, 0.06f);
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
            Shell.Lob(muzzle.position, aim, Def.ProjectileSpeed, Stats.Splash, Damage, Def.Color);
            Sfx.At("cannon_fire", muzzle.position);
            Combat.Flash(muzzle.position, U.Hex(0xffb050), 7f, 3f, 0.12f);
            Fx.Burst(muzzle.position, 0.6f, U.Hex(0xffd27a), 0.1f);
            kick = 0.24f;
            // A gout of smoke from the muzzle, drifting up.
            for (int i = 0; i < 5; i++)
                Fx.Puff(muzzle.position, muzzle.forward * Random.Range(1f, 2.5f) + Random.insideUnitSphere * 0.6f + Vector3.up * 0.6f,
                    Random.Range(0.4f, 0.8f), new Color(0.55f, 0.53f, 0.5f, 0.7f), Random.Range(0.6f, 1.1f));
        }

        /// <summary>
        /// The laser: down to chest height, then flat along the ground, piercing
        /// every orc on its line and glancing off rock and barricades. On the
        /// hills it keeps to chest height over the land rather than one level,
        /// so it runs up and down the road instead of into it.
        /// </summary>
        void FireBeam()
        {
            struck.Clear();
            var flat = new Vector3(muzzle.position.x, 0, muzzle.position.z);
            var dir = target.transform.position - flat;
            dir.y = 0;
            dir = Quaternion.Euler(0, Random.Range(-Def.AimDither, Def.AimDither) * Mathf.Rad2Deg, 0) * dir.normalized;

            Vector3 Chest(Vector3 p) => new Vector3(p.x, Relief.Ground(p) + BeamHeight, p.z);
            var points = new List<Vector3> { muzzle.position, Chest(flat) };
            float budget = Range * Def.PathBudgetMultiplier;
            var p = flat;
            for (int bounce = 0; bounce <= 3 && budget > 0.05f; bounce++)
            {
                bool surface = Physics.Raycast(Chest(p), dir, out var hit, budget, Layers.RockMask | Layers.WallMask,
                    QueryTriggerInteraction.Ignore);
                float len = surface ? hit.distance : budget;
                var end = p + dir * len;
                foreach (var e in Enemy.All.ToArray())
                {
                    if (!e.Alive || !e.Targetable || struck.Contains(e)) continue;
                    if (DistanceToSegment(e.transform.position, p, end) > e.Def.Radius + 0.15f) continue;
                    struck.Add(e);
                    e.TakeDamage(Damage, from: Chest(p));
                }
                // Laid along the land in short steps, so the beam reads over a rise.
                int steps = Mathf.Max(1, Mathf.CeilToInt(len / 1.5f));
                for (int k = 1; k <= steps; k++) points.Add(Chest(Vector3.Lerp(p, end, k / (float)steps)));
                p = end;
                budget -= len;
                if (!surface) break;
                dir = Vector3.Reflect(dir, hit.normal);
                dir.y = 0;
                dir.Normalize();
                p += dir * 0.02f;
            }
            Fx.Polyline(points.ToArray(), Def.Color, 0.07f, 0.09f);
            Fx.Polyline(points.ToArray(), Color.white, 0.025f, 0.06f);
            Sfx.At("laser_zap", muzzle.position);
            Combat.Flash(points[points.Count - 1], Def.Color, 5f, 2f, 0.1f);
            Combat.Sparks(points[points.Count - 1], -dir, Def.Color, 3);
            Fx.Burst(points[points.Count - 1], 0.25f, Def.Color, 0.12f);
            flash = 1f;
        }

        /// <summary>Distance on the ground from a point to a segment.</summary>
        static float DistanceToSegment(Vector3 x, Vector3 a, Vector3 b)
        {
            var p = new Vector2(x.x, x.z);
            var u = new Vector2(a.x, a.z);
            var v = new Vector2(b.x, b.z);
            var d = v - u;
            float t = d.sqrMagnitude < 1e-6f ? 0 : Mathf.Clamp01(Vector2.Dot(p - u, d) / d.sqrMagnitude);
            return Vector2.Distance(p, u + d * t);
        }

        void Pulse()
        {
            foreach (var e in Enemy.All.ToArray())
            {
                // The pulse arcs over rock as it always has, but not through a hill.
                if (!Valid(e) || Physics.Linecast(muzzle.position, e.AimPoint, Layers.TerrainMask, QueryTriggerInteraction.Ignore)) continue;
                e.Slow(Def.SlowFactor, Stats.SlowDuration);
                e.TakeDamage(Damage, from: transform.position);
                // A bolt from the crystal to every orc caught in the pulse.
                if (e.Targetable) Fx.Lightning(muzzle.position, e.AimPoint, Def.Color);
            }
            Fx.Ring(transform.position + Vector3.up * 0.4f, Range, Def.Color, 0.45f);
            Sfx.At("arc_zap", muzzle.position);
            Combat.Flash(muzzle.position, Def.Color, Range * 0.8f, 2.5f, 0.15f);
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
                e.TakeDamage(Damage, from: transform.position);
                e.Burn(0.8f);
            }
            Sfx.At("flame_burst", muzzle.position);
            if (Random.value < 0.5f) Combat.Flash(muzzle.position + head.forward * 2f, U.Hex(0xff7a20), Range * 0.9f, 2.2f, 0.15f);
            // The jet: many small, see-through tongues, hotter (yellower) nearer the nozzle.
            for (int i = 0; i < 6; i++)
            {
                var spread = Quaternion.Euler(Random.Range(-4f, 4f), Random.Range(-half, half) * 0.8f, 0);
                var v = spread * muzzle.forward * Range * Random.Range(1.8f, 2.6f);
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

        void OnDestroy()
        {
            All.Remove(this);
            Sentries.Remove(this);
        }
    }
}
