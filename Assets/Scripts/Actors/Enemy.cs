using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace OrcHordeFps
{
    /// <summary>Marks the collider that takes headshot damage.</summary>
    public sealed class EnemyHead : MonoBehaviour { }

    public sealed class Enemy : MonoBehaviour
    {
        public static readonly List<Enemy> All = new List<Enemy>();

        const float HeadshotMultiplier = 2f;
        const float BreachDistance = 3.5f;
        /// <summary>How hard an orc hits you when you stand in its way, per metre of its size.</summary>
        const float PlayerDpsPerMetre = 14f;
        /// <summary>
        /// DEVIATION: Orc Horde keeps a route open for units that avoid walls. Here
        /// a fully sealed road makes even those chew through, slowly, rather than
        /// stand still forever.
        /// </summary>
        const float SealedRoadDps = 5f;

        public EnemyDef Def { get; private set; }
        public float Health { get; private set; }
        /// <summary>The definition's health times the round's toughness, fixed at spawn.</summary>
        public float MaxHealth { get; private set; }
        /// <summary>The round's toughness when this orc spawned; heals scale with it too.</summary>
        public float Toughness { get; private set; } = 1f;
        public bool Alive => Health > 0f;
        public Vector3 AimPoint => transform.position + Vector3.up * Def.Height * 0.5f;
        public Vector3 Velocity => agent.velocity;
        /// <summary>False while inside a tunnel: out of sight and out of reach.</summary>
        public bool Targetable { get; private set; } = true;
        /// <summary>Crossings made by every orc this session, for the smoke test.</summary>
        public static int LinkCrossings;
        /// <summary>Orcs removed for being stranded with no way to the base, for the smoke test.</summary>
        public static int Stranded;

        /// <summary>How long an orc may stand still with no route to the base and no wall to break before it is removed.</summary>
        const float StrandedSeconds = 10f;

        /// <summary>Path left to the base, for "first" targeting. Smaller is further along.</summary>
        public float Remaining =>
            agent.isOnNavMesh && !agent.pathPending && agent.hasPath && !float.IsInfinity(agent.remainingDistance)
                ? agent.remainingDistance
                : Vector3.Distance(transform.position, GameManager.I.Map.Base) * 1.5f;

        NavMeshAgent agent;
        Renderer[] bodyRenderers;
        float slowUntil, slowFactor = 1f;
        float flashUntil;
        bool flashing;
        float nextThink, nextHeal;
        Barricade wallTarget;
        MaterialPropertyBlock block;
        Collider[] colliders;
        OrcAnimator orc;
        bool puppet;
        bool hitPlayerThisFrame;
        Color tint = Color.white;

        /// <summary>The dead lie a while before they sink; past this many, the oldest go at once.</summary>
        const int MaxCorpses = 40;
        static readonly Queue<GameObject> corpses = new Queue<GameObject>();

        // A bridge or tunnel crossing in progress.
        Links.Info crossing;
        Vector3 crossFrom, crossTo;
        float crossDistance, crossTravelled;

        float stuckSince = -1f;

        // Boss state.
        float lastHurt, nextCharge, chargeEnds, windupEnds, pausedUntil;
        int summonsDone;

        /// <param name="toughness">Health multiplier. Only orcs drawn from the round's budget carry the round's;
        /// set pieces (bosses, guaranteed arrivals, escorts) are fixed in Orc Horde and stay at 1.</param>
        public static Enemy Spawn(EnemyDef def, Vector3 near, float scatter = 3f, float toughness = 1f)
        {
            var p = near + new Vector3(Random.Range(-scatter, scatter), 0, Random.Range(-scatter, scatter));
            if (!NavMesh.SamplePosition(p, out var hit, 6f, NavMesh.AllAreas)) return null;

            var go = new GameObject(def.Name);
            go.transform.position = hit.position;
            var e = go.AddComponent<Enemy>();
            e.Init(def, toughness);
            return e;
        }

        void Init(EnemyDef def, float toughness)
        {
            Def = def;
            Toughness = toughness;
            MaxHealth = Health = def.MaxHealth * Toughness;
            BuildBody();

            agent = gameObject.AddComponent<NavMeshAgent>();
            agent.radius = Mathf.Max(def.Radius, 0.25f);
            agent.height = def.Height;
            agent.speed = def.Speed;
            agent.acceleration = 30f;
            agent.angularSpeed = def.TurnDegreesPerSecond;
            agent.autoBraking = false;
            agent.stoppingDistance = 0f;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            // Crossed by hand, so an orc climbs onto a bridge or vanishes into a tunnel.
            agent.autoTraverseOffMeshLink = false;
            // Big units shoulder through; small ones give way.
            agent.avoidancePriority = Mathf.Clamp(70 - Mathf.RoundToInt(def.Radius * 40), 10, 90);
            if (agent.isOnNavMesh) agent.SetDestination(GameManager.I.Map.Base);

            nextThink = Time.time + Random.value;
            nextCharge = Time.time + def.ChargeInterval;
            All.Add(this);
        }

        void BuildBody()
        {
            orc = OrcAnimator.Create(Def, transform, GameManager.I.Map.biome);

            // The body hitbox is a plain capsule on the orc's root; the head has its own, riding the head bone.
            var hitbox = new GameObject("Body hitbox");
            hitbox.transform.SetParent(transform, false);
            var capsule = hitbox.AddComponent<CapsuleCollider>();
            capsule.radius = Mathf.Max(Def.Radius, 0.25f) * 1.15f;
            capsule.height = Def.Height * 0.82f;
            capsule.center = new Vector3(0, Def.Height * 0.41f, 0);

            Layers.SetRecursive(gameObject, Layers.Enemy);
            bodyRenderers = GetComponentsInChildren<Renderer>();
            colliders = GetComponentsInChildren<Collider>();
            block = new MaterialPropertyBlock();

            // No two orcs quite alike: a little lighter or darker, a little warmer or cooler.
            float shade = Random.Range(0.82f, 1.05f), warmth = Random.Range(-0.05f, 0.05f);
            tint = new Color(shade * (1 + warmth), shade, shade * (1 - warmth));
            block.SetColor("_Tint", tint);
            block.SetColor("_Flash", Color.black);
            block.SetFloat("_Scale", Def.Height / OrcRig.ModelHeight);
            feetY = transform.position.y;
            block.SetFloat("_FeetY", feetY);
            foreach (var r in bodyRenderers) r.SetPropertyBlock(block);
        }

        float feetY;

        /// <summary>The grime overlay climbs from the feet: keep it with them when they climb onto a bridge.</summary>
        void LateUpdate()
        {
            if (Mathf.Abs(transform.position.y - feetY) < 0.05f || block == null) return;
            feetY = transform.position.y;
            block.SetFloat("_FeetY", feetY);
            foreach (var r in bodyRenderers) if (r != null) r.SetPropertyBlock(block);
        }

        /// <summary>
        /// For showing off, not fighting: stands still facing a way, walking in
        /// place at its own pace, with no AI. Used by the smoke test's gallery.
        /// </summary>
        public void Puppet(Vector3 facing, bool walk)
        {
            puppet = true;
            if (agent.isOnNavMesh) agent.isStopped = true;
            agent.updateRotation = false;
            facing.y = 0;
            if (facing.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(facing);
            orc.OverrideSpeed = walk ? Def.Speed : 0f;
        }

        public OrcAnimator Animator => orc;

        /// <summary>The death fall without the kill: for the gallery.</summary>
        public void DieForGallery()
        {
            if (!All.Remove(this)) return;
            Health = 0f;
            LeaveCorpse();
        }

        float burnUntil, nextEmber;

        /// <summary>Set alight by a Flame Thrower: it flickers orange and sheds embers while it burns.</summary>
        public void Burn(float duration) => burnUntil = Mathf.Max(burnUntil, Time.time + duration);

        public void Slow(float factor, float duration)
        {
            slowFactor = Mathf.Min(factor, Time.time < slowUntil ? slowFactor : 1f);
            slowUntil = Mathf.Max(slowUntil, Time.time + duration);
        }

        /// <summary>Returns the damage actually dealt, after armour.</summary>
        /// <param name="from">Where the hit came from; null for blows from above, which no shield stops.</param>
        public float TakeDamage(float raw, bool headshot = false, Vector3? from = null)
        {
            if (!Alive || !Targetable) return 0f;
            if (puppet) { flashUntil = Time.time + 0.06f; orc?.Flinch(0.3f); return 0f; }
            if (headshot) raw *= HeadshotMultiplier;
            if (Def.ShieldArcDegrees > 0 && from.HasValue)
            {
                var toSource = from.Value - transform.position;
                toSource.y = 0;
                if (toSource.sqrMagnitude > 0.01f && Vector3.Angle(transform.forward, toSource) <= Def.ShieldArcDegrees / 2)
                    raw *= 1f - Def.ShieldReduction;
            }
            lastHurt = Time.time;
            float dealt = Damage.AfterArmour(raw, Def.Armour);
            Health -= dealt;
            flashUntil = Time.time + 0.06f;
            orc?.Flinch(Mathf.Clamp01(dealt / Mathf.Max(MaxHealth, 1f) * 6f + 0.15f));
            if (Health <= 0f) Die(killed: true);
            return dealt;
        }

        public void Heal(float amount) => Health = Mathf.Min(MaxHealth, Health + amount);

        public void Die(bool killed)
        {
            if (!All.Remove(this)) return;
            Health = 0f;
            if (killed)
            {
                GameManager.I.OnKilled(this);
                Fx.Burst(AimPoint, Def.Radius * 1.5f + 0.2f, new Color(0.35f, 0.05f, 0.04f), 0.35f);
                if (Def.SplitInto != null)
                    for (int i = 0; i < Def.SplitCount; i++) Spawn(Def.SplitInto, transform.position, 1.2f, Toughness);
                LeaveCorpse();
                return;
            }
            Destroy(gameObject);
        }

        /// <summary>Stops everything but the fall: no AI, no navigation, nothing to hit; the body goes after a few seconds.</summary>
        void LeaveCorpse()
        {
            if (agent != null) agent.enabled = false;
            foreach (var c in colliders) if (c != null) c.enabled = false;
            block.SetColor("_Flash", Color.black);
            foreach (var r in bodyRenderers) r.SetPropertyBlock(block);
            orc.Die();
            enabled = false;
            corpses.Enqueue(gameObject);
            while (corpses.Count > MaxCorpses)
            {
                var old = corpses.Dequeue();
                if (old != null) Destroy(old);
            }
            Destroy(gameObject, 3.6f);
        }

        void Update()
        {
            if (!Alive) return;
            if (puppet) { UpdateFlash(); return; }
            orc.Attacking = (wallTarget != null && agent.isOnNavMesh && agent.isStopped) || hitPlayerThisFrame;
            orc.Holding = Def.IsBoss && Time.time < pausedUntil || (Def.ChargeInterval > 0 && Time.time < windupEnds);
            hitPlayerThisFrame = false;
            if (crossing != null || (agent.isOnNavMesh && agent.isOnOffMeshLink)) { Cross(); return; }
            if (!agent.isOnNavMesh) return;
            var gm = GameManager.I;

            bool slowed = Time.time < slowUntil;
            agent.speed = Def.Speed * (slowed ? slowFactor : 1f) * BossSpeed();
            if (Def.IsBoss && Boss()) { UpdateFlash(); return; }

            var toBase = gm.Map.Base - transform.position;
            toBase.y = 0;
            if (toBase.magnitude < BreachDistance)
            {
                gm.OnBreach(this);
                Die(killed: false);
                return;
            }

            if (Def.HealRadius > 0 && Time.time >= nextHeal)
            {
                nextHeal = Time.time + Def.HealInterval;
                foreach (var other in All)
                    if ((other.transform.position - transform.position).sqrMagnitude < Def.HealRadius * Def.HealRadius)
                        other.Heal(Def.HealPerPulse * Toughness);
                Fx.Ring(transform.position + Vector3.up * 0.2f, Def.HealRadius, U.Hex(0x4fbf7a), 0.5f);
            }

            if (Time.time >= nextThink)
            {
                nextThink = Time.time + 0.5f + Random.value * 0.3f;
                Think();
            }

            if (wallTarget != null) AttackWall();
            else if (agent.isStopped) agent.isStopped = false;

            HitPlayer();
            UpdateFlash();
        }

        /// <summary>
        /// Crosses a bridge or tunnel under its own control: straight across the
        /// span keeping its place across the width, up onto the deck or out of
        /// sight in the rock, then back onto the navmesh on the far side.
        ///
        /// The link is let go the moment the orc steps on: navmesh links carry one
        /// agent at a time, and holding it for the whole crossing would queue the
        /// horde behind it one orc every few seconds. The agent stands still,
        /// detached from the transform, while the body is carried across.
        /// </summary>
        void Cross()
        {
            if (crossing == null)
            {
                crossing = Links.Nearest(transform.position);
                if (crossing == null) { agent.CompleteOffMeshLink(); return; }
                var across = Vector3.Cross(Vector3.up, crossing.Direction);
                float offset = Mathf.Clamp(Vector3.Dot(transform.position - crossing.Entry, across), -crossing.HalfWidth, crossing.HalfWidth);
                crossFrom = transform.position;
                crossTo = crossing.Exit;
                // Keep its place across the width only where that lands on the road the exit opens onto:
                // a clear navmesh line back to the exit rules out the tower ground beside it.
                if (NavMesh.SamplePosition(crossing.Exit + across * offset, out var hit, 1f, NavMesh.AllAreas) &&
                    !NavMesh.Raycast(crossing.Exit, hit.position, out _, NavMesh.AllAreas))
                    crossTo = hit.position;
                crossDistance = Vector3.Distance(crossFrom, crossTo);
                crossTravelled = 0;
                if (crossing.Tunnel) SetTargetable(false);
                LinkCrossings++;
                crossCount++;
                if (agent.isOnOffMeshLink) agent.CompleteOffMeshLink();
                agent.isStopped = true;
                agent.updatePosition = false;
            }

            float slow = Time.time < slowUntil ? slowFactor : 1f;
            crossTravelled += Def.Speed * slow * BossSpeed() * Time.deltaTime;
            float k = Mathf.Clamp01(crossTravelled / Mathf.Max(crossDistance, 0.01f));
            // On a bridge, up the ramp over the first stretch, along the deck, down over the last.
            float lift = crossing.Tunnel ? 0f : Links.DeckHeight * Mathf.Clamp01(Mathf.Min(k, 1f - k) / 0.18f);
            var pos = Vector3.Lerp(crossFrom, crossTo, k) + Vector3.up * lift;
            var heading = crossTo - crossFrom;
            heading.y = 0;
            transform.SetPositionAndRotation(pos, heading.sqrMagnitude > 0.01f ? Quaternion.LookRotation(heading) : transform.rotation);
            if (k < 1f) return;

            if (crossing.Tunnel) SetTargetable(true);
            crossing = null;
            agent.Warp(crossTo);
            agent.updatePosition = true;
            agent.isStopped = false;
            agent.SetDestination(GameManager.I.Map.Base);
        }

        void SetTargetable(bool value)
        {
            Targetable = value;
            foreach (var r in bodyRenderers) r.enabled = value;
            foreach (var c in colliders) c.enabled = value;
        }

        float BossSpeed()
        {
            float m = 1f;
            if (Def.EnrageThreshold > 0 && Health <= MaxHealth * Def.EnrageThreshold) m *= Def.EnrageSpeedMultiplier;
            if (Time.time < chargeEnds && Time.time >= windupEnds) m *= Def.ChargeSpeedMultiplier;
            return m;
        }

        /// <summary>Runs the boss's set behaviours. Returns true while one holds it still this frame.</summary>
        bool Boss()
        {
            if (Def.RegenPerSecond > 0 && Time.time - lastHurt > Def.RegenIdle)
                Heal(Def.RegenPerSecond * Toughness * Time.deltaTime);

            if (Def.SummonThresholds != null && summonsDone < Def.SummonThresholds.Length &&
                Health <= MaxHealth * Def.SummonThresholds[summonsDone])
            {
                summonsDone++;
                pausedUntil = Time.time + Def.SummonPause;
                foreach (var (def, count) in Def.SummonEscort)
                    for (int i = 0; i < count; i++) Spawn(def, transform.position, 3f);
                Fx.Ring(transform.position + Vector3.up * 0.3f, 6f, U.Hex(0xffd166), 0.8f);
                GameManager.I.Announce($"The {Def.Name} calls its war band!", U.Hex(0xffd166), 3f);
            }

            if (Def.ChargeInterval > 0 && Time.time >= nextCharge)
            {
                nextCharge = Time.time + Def.ChargeInterval;
                windupEnds = Time.time + Def.ChargeWindup;
                chargeEnds = windupEnds + Def.ChargeDuration;
            }
            if (Time.time < chargeEnds && Time.time >= windupEnds)
            {
                // The first barricade in its path takes the full blow, and the charge ends there.
                var wall = Barricade.Nearest(transform.position, Def.Radius + 1.2f);
                if (wall != null)
                {
                    wall.Damage(Def.ChargeWallDamage);
                    Fx.Burst(wall.ClosestPoint(transform.position) + Vector3.up, 3f, U.Hex(0xff9a5c), 0.4f);
                    chargeEnds = Time.time;
                }
            }

            // Gathering a war band, or winding up a charge.
            bool hold = Time.time < pausedUntil || (Def.ChargeInterval > 0 && Time.time < windupEnds);
            if (hold) agent.isStopped = true;
            return hold;
        }

        /// <summary>A one-word account of what this orc is doing, for the smoke test's stall report.</summary>
        public string DebugState =>
            crossing != null ? (crossing.Tunnel ? "tunnel" : "bridge")
            : !agent.isOnNavMesh ? "offmesh"
            : agent.isOnOffMeshLink ? "onlink"
            : wallTarget != null ? "wall"
            : agent.isStopped ? "stopped"
            : agent.pathPending ? "pathing"
            : agent.pathStatus != NavMeshPathStatus.PathComplete ? "partial"
            : agent.velocity.sqrMagnitude < 0.04f ? "idle"
            : "moving";

        public string DebugDetail =>
            $"{Def.Id} pos={transform.position:F1} agent={agent.nextPosition:F1} onMesh={agent.isOnNavMesh} " +
            $"status={agent.pathStatus} corners={agent.path.corners.Length} vel={agent.velocity.magnitude:F2} " +
            $"targetable={Targetable} crossings={crossCount}";
        int crossCount;

        bool RoadSealed => !agent.pathPending && agent.pathStatus != NavMeshPathStatus.PathComplete;

        void Think()
        {
            if (wallTarget != null && !wallTarget.Standing) wallTarget = null;

            if (wallTarget == null)
            {
                bool sealedRoad = RoadSealed;
                float reach = Def.WallDetection;
                bool wants = Def.Wall switch
                {
                    WallBehavior.DemolishNearestWall => true,
                    WallBehavior.AttackUnderPressure => sealedRoad || UnderPressure(),
                    _ => sealedRoad,
                };
                // A partial path ends at the wall in the way, so look a little past the usual distance.
                if (sealedRoad) reach = Mathf.Max(reach, 6f);
                if (wants) wallTarget = Barricade.Nearest(transform.position, reach);
            }

            // A failsafe, not a behaviour: an orc cut off from the base with nothing to attack would
            // otherwise hold its round open forever. It is removed without paying or breaching. Judged
            // before re-pathing below, which would leave the path pending and hide the answer.
            bool stranded = wallTarget == null && RoadSealed && agent.velocity.sqrMagnitude < 0.04f;
            if (!stranded) stuckSince = -1f;
            else if (stuckSince < 0f) stuckSince = Time.time;
            else if (Time.time - stuckSince > StrandedSeconds)
            {
                Stranded++;
                Debug.LogWarning($"[Enemy] {Def.Name} stranded at {transform.position}, removed");
                Die(killed: false);
                return;
            }

            if (wallTarget == null) agent.SetDestination(GameManager.I.Map.Base);
        }

        /// <summary>A crowd packed against a barricade pushes through it.</summary>
        bool UnderPressure()
        {
            if (Barricade.Nearest(transform.position, Def.WallDetection) == null) return false;
            int near = 0;
            foreach (var other in All)
                if ((other.transform.position - transform.position).sqrMagnitude < 9f && ++near >= 10) return true;
            return false;
        }

        void AttackWall()
        {
            if (!wallTarget.Standing)
            {
                wallTarget = null;
                agent.isStopped = false;
                agent.SetDestination(GameManager.I.Map.Base);
                return;
            }

            var contact = wallTarget.ClosestPoint(transform.position);
            var to = contact - transform.position;
            to.y = 0;
            if (to.magnitude > Def.WallRange + Def.Radius + 0.3f)
            {
                agent.isStopped = false;
                if (!agent.pathPending && (agent.destination - contact).sqrMagnitude > 1f) agent.SetDestination(contact);
                return;
            }

            agent.isStopped = true;
            if (to.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to),
                    Def.TurnDegreesPerSecond * Time.deltaTime);

            if (Def.Wall == WallBehavior.DemolishNearestWall)
            {
                wallTarget.Damage(Def.WallDps);
                Fx.Burst(contact + Vector3.up, 3f, U.Hex(0xff8a2a), 0.5f);
                Die(killed: false);
                return;
            }

            float dps = Def.WallDps > 0 ? Def.WallDps : SealedRoadDps;
            wallTarget.Damage(dps * Time.deltaTime);
            if (!wallTarget.Standing)
            {
                wallTarget = null;
                agent.isStopped = false;
                agent.SetDestination(GameManager.I.Map.Base);
            }
        }

        void HitPlayer()
        {
            var player = PlayerHealth.I;
            if (player == null || !player.Alive) return;
            var d = player.transform.position - transform.position;
            d.y = 0;
            if (d.magnitude < Def.Radius + 0.9f && GameManager.I.Phase == Phase.Combat)
            {
                player.TakeDamage(PlayerDpsPerMetre * Mathf.Max(Def.Radius, 0.3f) * 3f * Time.deltaTime);
                hitPlayerThisFrame = true;
            }
        }

        /// <summary>
        /// Shows what is happening to the orc: white on a hit, an orange flicker
        /// while it burns, a cold blue shimmer while an Arc Tower has it slowed.
        /// </summary>
        void UpdateFlash()
        {
            Color glow = Color.black;
            bool burning = Time.time < burnUntil, slowed = Time.time < slowUntil;
            if (Time.time < flashUntil) glow = new Color(0.28f, 0.25f, 0.22f);
            else if (burning) glow = new Color(0.4f, 0.13f, 0.02f) * (0.5f + 0.5f * Mathf.Sin(Time.time * 31f + transform.position.x));
            else if (slowed) glow = new Color(0.07f, 0.14f, 0.36f) * (0.6f + 0.4f * Mathf.Sin(Time.time * 13f + transform.position.z));

            if (burning && Time.time >= nextEmber)
            {
                nextEmber = Time.time + 0.12f;
                Fx.Puff(AimPoint + Random.insideUnitSphere * Def.Radius, Vector3.up * Random.Range(1f, 2f), Random.Range(0.12f, 0.25f),
                    Color.Lerp(U.Hex(0xffd23a), U.Hex(0xff4a1a), Random.value), 0.35f);
            }

            bool any = glow != Color.black;
            if (!any && !flashing) return;
            flashing = any;
            block.SetColor("_Flash", glow);
            block.SetColor("_Tint", tint);
            foreach (var r in bodyRenderers) r.SetPropertyBlock(block);
        }

        void OnDestroy() => All.Remove(this);
    }
}
