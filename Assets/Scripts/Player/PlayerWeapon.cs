using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// The player's rifle. New to the FPS: Orc Horde has no avatar, so these
    /// numbers are tuned here, sized so two body shots drop a Fodder and one to
    /// the head does.
    /// </summary>
    public sealed class PlayerWeapon : MonoBehaviour
    {
        public static PlayerWeapon I { get; private set; }

        public const int MagazineSize = 30;
        const float Damage = 24f, FireInterval = 0.1f, ReloadSeconds = 1.5f, Range = 150f, Spread = 0.006f;

        public int Ammo { get; private set; } = MagazineSize;
        public bool Reloading => Time.time < reloadDone;
        public float ReloadProgress => Reloading ? 1f - (reloadDone - Time.time) / ReloadSeconds : 1f;
        public float HitMarkerUntil { get; private set; }
        public bool LastHitWasHead { get; private set; }

        float nextShot, reloadDone;
        bool reloadPending;
        Transform gun, muzzle;
        Vector3 gunRest;
        Light flash;

        void Awake() => I = this;

        void Start()
        {
            var cam = PlayerController.I.Cam.transform;
            gun = new GameObject("Rifle").transform;
            gun.SetParent(cam, false);
            gunRest = new Vector3(0.25f, -0.22f, 0.45f);
            gun.localPosition = gunRest;
            var dark = Mats.Lit(U.Hex(0x2a2e33));
            var trim = Mats.Lit(U.Hex(0x5effc4), 1.5f);
            Shapes.Prim(PrimitiveType.Cube, gun, Vector3.zero, new Vector3(0.08f, 0.1f, 0.5f), dark);
            Shapes.Prim(PrimitiveType.Cube, gun, new Vector3(0, -0.1f, -0.08f), new Vector3(0.06f, 0.14f, 0.08f), dark);
            Shapes.Prim(PrimitiveType.Cube, gun, new Vector3(0, 0.065f, 0.05f), new Vector3(0.03f, 0.03f, 0.3f), trim);
            var barrel = Shapes.Prim(PrimitiveType.Cylinder, gun, new Vector3(0, 0.01f, 0.35f), new Vector3(0.035f, 0.12f, 0.035f), dark);
            barrel.transform.localRotation = Quaternion.Euler(90, 0, 0);
            foreach (var r in gun.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(gun, false);
            muzzle.localPosition = new Vector3(0, 0.01f, 0.48f);

            flash = muzzle.gameObject.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = U.Hex(0xffd27a);
            flash.range = 6f;
            flash.intensity = 0f;
        }

        void Update()
        {
            flash.intensity = Mathf.MoveTowards(flash.intensity, 0f, 60f * Time.deltaTime);
            gun.localPosition = Vector3.Lerp(gun.localPosition, gunRest, 12f * Time.deltaTime);

            var pc = PlayerController.I;
            bool building = BuildController.I.Tool != BuildTool.None;
            gun.gameObject.SetActive(!building);
            if (!pc.InputLive || building) return;

            if (Reloading)
            {
                gun.localPosition = gunRest + new Vector3(0, -0.12f, 0);
                return;
            }
            if (reloadPending)
            {
                Ammo = MagazineSize;
                reloadPending = false;
            }
            if ((Ammo < MagazineSize && Input.GetKeyDown(KeyCode.R)) || Ammo == 0)
            {
                reloadDone = Time.time + ReloadSeconds;
                reloadPending = true;
                return;
            }

            if (Input.GetMouseButton(0) && Time.time >= nextShot) Fire(pc);
        }

        void Fire(PlayerController pc)
        {
            nextShot = Time.time + FireInterval;
            Ammo--;
            var ray = pc.AimRay;
            ray.direction = (ray.direction + Random.insideUnitSphere * Spread).normalized;

            var end = ray.origin + ray.direction * Range;
            if (Physics.Raycast(ray, out var hit, Range, Layers.ShotMask, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                var e = hit.collider.GetComponentInParent<Enemy>();
                if (e != null)
                {
                    bool head = hit.collider.GetComponent<EnemyHead>() != null;
                    e.TakeDamage(Damage, head, ray.origin);
                    HitMarkerUntil = Time.time + 0.12f;
                    LastHitWasHead = head;
                    Fx.Burst(hit.point, 0.25f, U.Hex(0x7a1010), 0.15f);
                }
                else Fx.Burst(hit.point, 0.15f, U.Hex(0xd8c8a0), 0.12f);
            }

            Fx.Line(muzzle.position, end, U.Hex(0xfff0b0), 0.025f, 0.05f);
            flash.intensity = 4f;
            gun.localPosition = gunRest + new Vector3(0, 0.01f, -0.06f);
            pc.Kick(0.35f);
        }
    }
}
