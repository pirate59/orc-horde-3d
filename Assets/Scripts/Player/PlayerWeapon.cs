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
        public const float Damage = 24f, FireInterval = 0.1f, ReloadSeconds = 1.5f;
        const float Range = 150f, Spread = 0.006f;

        public int Ammo { get; private set; } = MagazineSize;
        public bool Reloading => Time.time < reloadDone;
        public float ReloadProgress => Reloading ? 1f - (reloadDone - Time.time) / ReloadSeconds : 1f;
        public float HitMarkerUntil { get; private set; }
        public bool LastHitWasHead { get; private set; }

        float nextShot, reloadDone;
        bool reloadPending;
        Transform gun, muzzle;
        GunRig rig;
        Vector3 gunRest;
        Light flash;
        // Recoil: a push back and a tip up that spring home.
        float recoil, recoilYaw, bob;

        void Awake() => I = this;

        void Start()
        {
            var cam = PlayerController.I.Cam.transform;
            gun = new GameObject("Rifle").transform;
            gun.SetParent(cam, false);
            gunRest = new Vector3(0.21f, -0.205f, 0.38f);
            gun.localPosition = gunRest;
            rig = GunModels.Rifle(gun);
            muzzle = rig.Muzzle;

            flash = muzzle.gameObject.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = U.Hex(0xffd27a);
            flash.range = 6f;
            flash.intensity = 0f;
        }

        void Update()
        {
            flash.intensity = Mathf.MoveTowards(flash.intensity, 0f, 60f * Time.deltaTime);
            recoil = Mathf.MoveTowards(recoil, 0f, Time.deltaTime * 6f);
            recoilYaw = Mathf.MoveTowards(recoilYaw, 0f, Time.deltaTime * 20f);
            Pose();

            var pc = PlayerController.I;
            bool building = BuildController.I.Tool != BuildTool.None || pc.Tactical;
            gun.gameObject.SetActive(!building);
            if (!pc.InputLive || building) return;

            if (Reloading) return;
            if (reloadPending)
            {
                Ammo = MagazineSize;
                reloadPending = false;
            }
            if ((Ammo < MagazineSize && Input.GetKeyDown(KeyCode.R)) || Ammo == 0)
            {
                StartReload();
                return;
            }

            if (Input.GetMouseButton(0) && Time.time >= nextShot) Fire(pc);
        }

        /// <summary>One shot along the aim, as if the trigger were pulled: for the smoke test's armoury.</summary>
        public void DebugFire() => Fire(PlayerController.I);

        public void StartReload()
        {
            reloadDone = Time.time + ReloadSeconds;
            reloadPending = true;
        }

        /// <summary>
        /// Where the gun sits this frame: bobbing with the walk, kicked by recoil,
        /// and through a reload the magazine drops away, a fresh one comes up and
        /// seats, while the gun rolls over to show the well.
        /// </summary>
        void Pose()
        {
            float moving = Mathf.Clamp01(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")).magnitude);
            bob += Time.deltaTime * (moving > 0 ? 9f : 2f);
            var sway = new Vector3(Mathf.Sin(bob) * 0.006f, Mathf.Abs(Mathf.Cos(bob)) * 0.008f, 0) * (moving > 0 ? 1f : 0.3f);

            float roll = 0, dip = 0;
            var magOffset = Vector3.zero;
            if (Reloading)
            {
                float p = ReloadProgress;
                float tilt = p < 0.15f ? p / 0.15f : p > 0.85f ? (1 - p) / 0.15f : 1f;
                roll = 28f * Mathf.SmoothStep(0, 1, tilt);
                dip = 0.05f * tilt;
                // Out (falling away), gone, then up and seated.
                if (p < 0.12f) magOffset = Vector3.zero;
                else if (p < 0.38f) magOffset = new Vector3(0, -0.35f, 0.04f) * Mathf.Pow((p - 0.12f) / 0.26f, 2);
                else if (p < 0.55f) magOffset = new Vector3(0, -0.35f, 0.04f);
                else if (p < 0.78f) magOffset = new Vector3(0, -0.12f, 0.02f) * (1 - Mathf.SmoothStep(0, 1, (p - 0.55f) / 0.23f));
            }
            rig.Magazine.localPosition = rig.MagazineRest + magOffset;

            var target = gunRest + sway + new Vector3(0, -dip, -recoil * 0.08f);
            gun.localPosition = Vector3.Lerp(gun.localPosition, target, 18f * Time.deltaTime);
            gun.localRotation = Quaternion.Euler(-recoil * 6f, recoilYaw, roll);
        }

        void Fire(PlayerController pc)
        {
            nextShot = Time.time + FireInterval / Progression.RifleFireRate;
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
                    e.TakeDamage(Damage * Progression.RifleDamage, head, ray.origin);
                    HitMarkerUntil = Time.time + 0.12f;
                    LastHitWasHead = head;
                    Fx.Burst(hit.point, 0.25f, U.Hex(0x7a1010), 0.15f);
                }
                else Fx.Burst(hit.point, 0.15f, U.Hex(0xd8c8a0), 0.12f);
            }

            Fx.Line(muzzle.position, end, U.Hex(0xfff0b0), 0.025f, 0.05f);
            Fx.Burst(muzzle.position, 0.09f, U.Hex(0xffe08a), 0.04f);
            flash.intensity = 4f;
            recoil = 1f;
            recoilYaw = Random.Range(-1.2f, 1.2f);
            var cam = pc.Cam.transform;
            Fx.Casing(rig.Ejector.position, cam.right * Random.Range(1.6f, 2.4f) + cam.up * Random.Range(1f, 1.8f), U.Hex(0xc8a050), 0.008f, 0.7f);
            pc.Kick(0.35f);
        }
    }
}
