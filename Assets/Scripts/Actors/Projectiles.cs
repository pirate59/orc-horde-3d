using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>A flat-flying bullet: stops in the first thing it meets.</summary>
    public sealed class Projectile : MonoBehaviour
    {
        Vector3 velocity;
        float damage, radius, life;
        int mask;

        public static void Fire(Vector3 from, Vector3 dir, TowerDef def, int sightMask)
        {
            var go = Shapes.Prim(PrimitiveType.Sphere, null, from, new Vector3(0.09f, 0.09f, 0.6f), Mats.Unlit(def.Color * 1.4f));
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.rotation = Quaternion.LookRotation(dir);
            var p = go.AddComponent<Projectile>();
            p.velocity = dir * def.ProjectileSpeed;
            p.damage = def.Damage;
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
                if (e != null) e.TakeDamage(damage, from: transform.position);
                Fx.Burst(hit.point, 0.2f, U.Hex(0xffe08a), 0.08f);
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
            Fx.Burst(to + Vector3.up * 0.3f, splash, U.Hex(0xffa040), 0.35f);
            Destroy(gameObject);
        }
    }
}
