using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>A flat-flying bullet: stops in the first thing it meets.</summary>
    public sealed class Projectile : MonoBehaviour
    {
        Vector3 velocity;
        float damage, radius, life;
        int mask;
        static int fired;

        public static void Fire(Vector3 from, Vector3 dir, TowerDef def, float damage, int sightMask)
        {
            // The round itself is too quick to see: what shows is the air it tears through, and every
            // fourth one a glowing tracer.
            var go = new GameObject("Bullet");
            go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(dir));
            WeaponFx.AirTrail(go, 0.12f, 0.12f, ++fired % 4 == 0 ? new Color(1f, 0.75f, 0.35f) : default);
            var p = go.AddComponent<Projectile>();
            p.velocity = dir * def.ProjectileSpeed;
            p.damage = damage;
            p.radius = def.ProjectileRadius;
            p.life = def.ProjectileLifetime;
            p.mask = sightMask | Layers.EnemyMask | (1 << Layers.Road) | (1 << Layers.BuildGround);
        }

        void Update()
        {
            life -= Time.deltaTime;
            if (life <= 0f) { Destroy(gameObject); return; }

            var step = velocity * Time.deltaTime;
            if (Physics.SphereCast(transform.position, radius, step.normalized, out var hit, step.magnitude, mask,
                    QueryTriggerInteraction.Ignore))
            {
                var e = hit.collider.GetComponentInParent<Enemy>();
                // A tower's bullet glances off the rock round an orc in a tunnel.
                if (e != null && e.InTunnel) Combat.Sparks(hit.point, hit.normal, U.Hex(0xffd27a), 3);
                else if (e != null)
                {
                    e.TakeDamage(damage, from: transform.position);
                    WeaponFx.Smoke(hit.point, hit.normal * 1.5f + Random.insideUnitSphere, 0.25f, 0.35f, new Color(0.4f, 0.03f, 0.03f, 0.8f));
                    if (Random.value < 0.15f) Combat.Blood(hit.point, 0.5f);
                }
                else
                {
                    Combat.Sparks(hit.point, hit.normal, U.Hex(0xffd27a), 3);
                    Combat.Dust(hit.point, hit.normal, new Color(0.5f, 0.45f, 0.4f, 0.5f));
                }
                Destroy(gameObject);
                return;
            }
            transform.position += step;
        }
    }

    /// <summary>A lobbed shell: flies a fixed arc to its aim point and bursts there, whatever is in between.</summary>
    public sealed class Shell : MonoBehaviour
    {
        Vector3 from, to;
        float duration, t, splash, damage, apex;
        Color color;

        public static void Lob(Vector3 from, Vector3 to, float speed, float splash, float damage, Color color)
        {
            var go = Shapes.Prim(PrimitiveType.Sphere, null, from, Vector3.one * 0.3f, Mats.Lit(U.Hex(0x22262b)));
            // A trail of smoke and shimmer behind it, so its arc can be followed.
            WeaponFx.SmokeTrail(go, 0.3f, 0.7f);
            var shimmer = new GameObject("shimmer");
            shimmer.transform.SetParent(go.transform, false);
            WeaponFx.AirTrail(shimmer, 0.35f, 0.25f);
            var s = go.AddComponent<Shell>();
            s.from = from;
            s.to = to;
            float flat = Vector3.Distance(new Vector3(from.x, 0, from.z), new Vector3(to.x, 0, to.z));
            s.duration = Mathf.Max(0.25f, flat / speed);
            s.apex = 1.5f + flat * 0.25f;
            s.splash = splash;
            s.damage = damage;
            s.color = color;
        }

        void Update()
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            transform.position = Vector3.Lerp(from, to, k) + Vector3.up * (4f * apex * k * (1f - k));
            if (k < 1f) return;
            Damage.Splash(to, splash, damage);
            Sfx.At("explosion", to, 0.8f);
            Combat.Flash(to + Vector3.up * 0.8f, U.Hex(0xffa040), splash * 5f, 5f, 0.3f);
            Combat.Scorch(to, splash * 0.55f);
            Combat.Debris(to, 6f, U.Hex(0x6b5a48), 6);
            Combat.Shake(to, 0.3f, 16f);
            // A fireball rolling up into smoke, sparks flung out, and a ring along the ground showing the splash's reach.
            WeaponFx.Fireball(to + Vector3.up * 0.4f, splash * 0.45f);
            Fx.Ring(to + Vector3.up * 0.1f, splash, U.Hex(0xff8a30), 0.35f);
            Destroy(gameObject);
        }
    }
}
