using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>A flat-flying bullet: stops in the first thing it meets.</summary>
    public sealed class Projectile : MonoBehaviour
    {
        Vector3 velocity;
        float damage, radius, life;
        int mask;

        public static void Fire(Vector3 from, Vector3 dir, TowerDef def, float damage, int sightMask)
        {
            var go = Shapes.Prim(PrimitiveType.Sphere, null, from, new Vector3(0.09f, 0.09f, 0.6f), Mats.Unlit(def.Color * 2.6f));
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.rotation = Quaternion.LookRotation(dir);
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
                    Fx.Burst(hit.point, 0.18f, U.Hex(0x6a0c0c), 0.1f);
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
            // A short, hot fireball, a ring along the ground showing the splash's reach, and smoke.
            Fx.Burst(to + Vector3.up * 0.4f, splash * 0.45f, U.Hex(0xffc060), 0.22f);
            Fx.Ring(to + Vector3.up * 0.1f, splash, U.Hex(0xff8a30), 0.35f);
            for (int i = 0; i < 4; i++)
                Fx.Puff(to + Random.insideUnitSphere * splash * 0.3f + Vector3.up * 0.3f, Vector3.up * Random.Range(1f, 2f) + Random.insideUnitSphere * 0.5f,
                    Random.Range(0.5f, 0.9f), new Color(0.3f, 0.28f, 0.26f, 0.6f), Random.Range(0.6f, 1f));
            Destroy(gameObject);
        }
    }
}
