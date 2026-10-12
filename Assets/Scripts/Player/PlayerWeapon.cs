using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// The player's rifle. New to the FPS: Orc Horde has no avatar, so these
    /// numbers are tuned here, sized so two body shots drop a Fodder and one to
    /// the head does.
    ///
    /// Hold the right mouse button to aim down the sight: the rifle comes up to
    /// the eye with the holo reticle on the centre of the screen, the view
    /// narrows, shots group tighter and kick less, and the player moves slowly.
    /// </summary>
    public sealed class PlayerWeapon : MonoBehaviour
    {
        public static PlayerWeapon I { get; private set; }

        public const int MagazineSize = 30;
        public const float Damage = 24f, FireInterval = 0.1f, ReloadSeconds = 1.5f;
        const float Range = 150f;
        /// <summary>Random spread of a shot, from the hip and down the sight.</summary>
        const float HipSpread = 0.008f, AimSpread = 0.0015f;
        /// <summary>Camera kick per shot, in degrees.</summary>
        const float HipKick = 0.35f, AimKick = 0.12f;
        public const float HipFov = 75f, AimFov = 48f;
        /// <summary>Seconds to bring the sight up (and as long to drop it).</summary>
        const float AimTime = 0.18f;
        /// <summary>
        /// Where the gun sits when aimed: the sight's reticle (at y 0.104 on the
        /// model) on the camera's axis, at arm's length so the sight frames the target rather than hiding it.
        /// </summary>
        static readonly Vector3 AimPosition = new Vector3(0f, -0.104f, 0.36f);

        public int Ammo { get; private set; } = MagazineSize;
        public bool Reloading => Time.time < reloadDone;
        public float ReloadProgress => Reloading ? 1f - (reloadDone - Time.time) / ReloadSeconds : 1f;
        public float HitMarkerUntil { get; private set; }
        public bool LastHitWasHead { get; private set; }
        /// <summary>0 from the hip, 1 fully down the sight; eased between.</summary>
        public float Aim { get; private set; }
        public bool Aiming => aimHeld;

        float nextShot, reloadDone;
        bool reloadPending;
        Transform gun, muzzle;
        GunRig rig;
        Vector3 gunRest;
        Light flash;
        // Recoil: a push back and a tip up that spring home.
        float recoil, recoilYaw, bob;
        bool aimHeld;

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

            // Aim with the right button: not while building, reloading or dead.
            bool wasAiming = aimHeld;
            aimHeld = !building && pc.InputLive && !Reloading && (Input.GetMouseButton(1) || debugAim);
            if (aimHeld && !wasAiming) Sfx.UI("ads_up");
            Aim = Mathf.MoveTowards(Aim, aimHeld ? 1f : 0f, Time.deltaTime / AimTime);
            if (!pc.Tactical) pc.Cam.fieldOfView = Mathf.Lerp(HipFov, AimFov, Ease(Aim));

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

        static float Ease(float t) => t * t * (3f - 2f * t);

        /// <summary>Raises (or lowers) the sight as if the right button were held: for the smoke test.</summary>
        public void DebugAim(bool on) => debugAim = on;
        bool debugAim;

        public void StartReload()
        {
            reloadDone = Time.time + ReloadSeconds;
            reloadPending = true;
            Sfx.UI("rifle_reload");
        }

        /// <summary>
        /// Where the gun sits this frame: bobbing with the walk, kicked by recoil,
        /// and through a reload the magazine drops away, a fresh one comes up and
        /// seats, while the gun rolls over to show the well.
        /// </summary>
        void Pose()
        {
            float aim = Ease(Aim);
            float moving = Mathf.Clamp01(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")).magnitude);
            bob += Time.deltaTime * (moving > 0 ? 9f : 2f) * (1f - 0.5f * aim);
            // Down the sight the gun barely bobs, so the reticle stays on target.
            var sway = new Vector3(Mathf.Sin(bob) * 0.006f, Mathf.Abs(Mathf.Cos(bob)) * 0.008f, 0) * (moving > 0 ? 1f : 0.3f) * (1f - 0.85f * aim);

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

            float kick = recoil * Mathf.Lerp(1f, 0.35f, aim);
            var target = Vector3.Lerp(gunRest, AimPosition, aim) + sway + new Vector3(0, -dip, -kick * 0.08f);
            gun.localPosition = Vector3.Lerp(gun.localPosition, target, (18f + 30f * aim) * Time.deltaTime);
            gun.localRotation = Quaternion.Euler(-kick * Mathf.Lerp(6f, 2.5f, aim), recoilYaw * (1f - 0.7f * aim), roll);
        }

        void Fire(PlayerController pc)
        {
            nextShot = Time.time + FireInterval / Progression.RifleFireRate;
            Ammo--;
            var ray = pc.AimRay;
            float aim = Ease(Aim);
            ray.direction = (ray.direction + Random.insideUnitSphere * Mathf.Lerp(HipSpread, AimSpread, aim)).normalized;

            var end = ray.origin + ray.direction * Range;
            if (Physics.Raycast(ray, out var hit, Range, Layers.ShotMask, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                var e = hit.collider.GetComponentInParent<Enemy>();
                if (e != null)
                {
                    bool head = hit.collider.GetComponent<EnemyHead>() != null;
                    e.TakeDamage(Damage * Progression.RifleDamage, head, ray.origin);
                    // A killing headshot holds the world still for a beat.
                    if (head && !e.Alive) Combat.HitStop(0.05f);
                    if (Random.value < 0.3f) Combat.Blood(hit.point, 0.6f);
                    HitMarkerUntil = Time.time + 0.12f;
                    LastHitWasHead = head;
                    Sfx.UI(head ? "headshot" : "hit");
                    Fx.Burst(hit.point, 0.25f, U.Hex(0x7a1010), 0.15f);
                }
                else
                {
                    Combat.Sparks(hit.point, hit.normal, U.Hex(0xffe0a0), 4);
                    Combat.Dust(hit.point, hit.normal, new Color(0.55f, 0.5f, 0.45f, 0.55f));
                    Combat.Debris(hit.point, 2.5f, U.Hex(0x6b5a48), 2);
                }
            }

            Sfx.UI("rifle_shot");
            // A tracer hot enough to glow.
            Fx.Line(muzzle.position, end, U.Hex(0xfff0b0) * 2.2f, 0.025f, 0.05f);
            Fx.Burst(muzzle.position, 0.09f, U.Hex(0xffe08a), 0.04f);
            flash.intensity = 4f;
            recoil = 1f;
            recoilYaw = Random.Range(-1.2f, 1.2f);
            var cam = pc.Cam.transform;
            Fx.Casing(rig.Ejector.position, cam.right * Random.Range(1.6f, 2.4f) + cam.up * Random.Range(1f, 1.8f), U.Hex(0xc8a050), 0.008f, 0.7f);
            pc.Kick(Mathf.Lerp(HipKick, AimKick, aim));
        }
    }
}
