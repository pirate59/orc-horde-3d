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
        public bool Alive => Health > 0f;
        public Vector3 AimPoint => transform.position + Vector3.up * Def.Height * 0.5f;
        public Vector3 Velocity => agent.velocity;

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

        public static Enemy Spawn(EnemyDef def, Vector3 near, float scatter = 3f)
        {
            var p = near + new Vector3(Random.Range(-scatter, scatter), 0, Random.Range(-scatter, scatter));
            if (!NavMesh.SamplePosition(p, out var hit, 6f, NavMesh.AllAreas)) return null;

            var go = new GameObject(def.Name);
            go.transform.position = hit.position;
            var e = go.AddComponent<Enemy>();
            e.Init(def);
            return e;
        }

        void Init(EnemyDef def)
        {
            Def = def;
            Health = def.MaxHealth;
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
            // Big units shoulder through; small ones give way.
            agent.avoidancePriority = Mathf.Clamp(70 - Mathf.RoundToInt(def.Radius * 40), 10, 90);
            if (agent.isOnNavMesh) agent.SetDestination(GameManager.I.Map.Base);

            nextThink = Time.time + Random.value;
            All.Add(this);
        }

        void BuildBody()
        {
            float r = Mathf.Max(Def.Radius, 0.25f) * 1.15f;
            float h = Def.Height;
            float headR = r * 0.75f;
            var skin = Mats.Lit(Def.Color);

            var body = Shapes.Prim(PrimitiveType.Capsule, transform, new Vector3(0, (h - headR) / 2, 0),
                new Vector3(r * 2, (h - headR) / 2, r * 2), skin, collider: true);
            var head = Shapes.Prim(PrimitiveType.Sphere, transform, new Vector3(0, h - headR, 0.05f),
                Vector3.one * headR * 2, Mats.Lit(Def.Color * 0.8f), collider: true);
            head.AddComponent<EnemyHead>();

            var eye = Mats.Lit(U.Hex(0xff2a1a), 3f);
            Shapes.Prim(PrimitiveType.Sphere, head.transform, new Vector3(-0.3f, 0.12f, 0.42f), Vector3.one * 0.18f, eye);
            Shapes.Prim(PrimitiveType.Sphere, head.transform, new Vector3(0.3f, 0.12f, 0.42f), Vector3.one * 0.18f, eye);
            var tusk = Mats.Lit(U.Hex(0xe8e0c8));
            Shapes.Prim(PrimitiveType.Cube, head.transform, new Vector3(-0.22f, -0.3f, 0.45f), new Vector3(0.08f, 0.22f, 0.08f), tusk);
            Shapes.Prim(PrimitiveType.Cube, head.transform, new Vector3(0.22f, -0.3f, 0.45f), new Vector3(0.08f, 0.22f, 0.08f), tusk);

            if (Def.Armour > 0)
            {
                var steel = Mats.Lit(U.Hex(0x9aa4ae));
                Shapes.Prim(PrimitiveType.Cylinder, head.transform, new Vector3(0, 0.35f, 0), new Vector3(1.05f, 0.25f, 1.05f), steel);
                Shapes.Prim(PrimitiveType.Cube, body.transform, new Vector3(0, 0.45f, 0), new Vector3(1.4f, 0.25f, 0.9f), steel);
            }
            if (Def.Wall == WallBehavior.DemolishNearestWall)
                Shapes.Prim(PrimitiveType.Cube, body.transform, new Vector3(0, 0.1f, -0.55f), new Vector3(0.7f, 0.5f, 0.4f),
                    Mats.Lit(U.Hex(0xff5a1a), 1.5f));
            if (Def.HealRadius > 0)
                Shapes.Prim(PrimitiveType.Cylinder, transform, new Vector3(r * 1.2f, h * 0.55f, 0),
                    new Vector3(0.08f, h * 0.55f, 0.08f), Mats.Lit(U.Hex(0x4fbf7a), 1.5f));

            Layers.SetRecursive(gameObject, Layers.Enemy);
            bodyRenderers = GetComponentsInChildren<Renderer>();
            block = new MaterialPropertyBlock();
        }

        public void Slow(float factor, float duration)
        {
            slowFactor = Mathf.Min(factor, Time.time < slowUntil ? slowFactor : 1f);
            slowUntil = Mathf.Max(slowUntil, Time.time + duration);
        }

        /// <summary>Returns the damage actually dealt, after armour.</summary>
        public float TakeDamage(float raw, bool headshot = false)
        {
            if (!Alive) return 0f;
            if (headshot) raw *= HeadshotMultiplier;
            float dealt = Damage.AfterArmour(raw, Def.Armour);
            Health -= dealt;
            flashUntil = Time.time + 0.06f;
            if (Health <= 0f) Die(killed: true);
            return dealt;
        }

        public void Heal(float amount) => Health = Mathf.Min(Def.MaxHealth, Health + amount);

        public void Die(bool killed)
        {
            if (!All.Remove(this)) return;
            Health = 0f;
            if (killed)
            {
                GameManager.I.OnKilled(this);
                Fx.Burst(AimPoint, Def.Radius * 3f + 0.4f, Def.Color * 0.8f, 0.4f);
                if (Def.SplitInto != null)
                    for (int i = 0; i < Def.SplitCount; i++) Spawn(Def.SplitInto, transform.position, 1.2f);
            }
            Destroy(gameObject);
        }

        void Update()
        {
            if (!Alive || !agent.isOnNavMesh) return;
            var gm = GameManager.I;

            bool slowed = Time.time < slowUntil;
            agent.speed = Def.Speed * (slowed ? slowFactor : 1f);

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
                        other.Heal(Def.HealPerPulse);
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
            if (d.magnitude < Def.Radius + 0.9f)
                player.TakeDamage(PlayerDpsPerMetre * Mathf.Max(Def.Radius, 0.3f) * 3f * Time.deltaTime);
        }

        void UpdateFlash()
        {
            bool flash = Time.time < flashUntil;
            if (flash == flashing) return;
            flashing = flash;
            if (flash) block.SetColor("_Color", Color.white);
            else block.Clear();
            foreach (var r in bodyRenderers) r.SetPropertyBlock(block);
        }

        void OnDestroy() => All.Remove(this);
    }
}
