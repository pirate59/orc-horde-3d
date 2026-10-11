using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// Orc Horde's two hands: Mortar on Q, Bombing Run on E. Hold to aim at the
    /// crosshair, release to call it in.
    /// </summary>
    public sealed class PlayerAbilities : MonoBehaviour
    {
        public static PlayerAbilities I { get; private set; }

        const float AimRange = 120f;
        const float MortarDelay = 0.9f;
        const float StrafeSeconds = 0.8f;
        const int StrafeBursts = 9;

        public AbilityDef Left { get; } = Abilities.Mortar;
        public AbilityDef Right { get; } = Abilities.BombingRun;

        readonly Dictionary<AbilityDef, float> readyAt = new Dictionary<AbilityDef, float>();
        LineRenderer marker;
        AbilityDef aiming;

        void Awake() => I = this;

        void Start()
        {
            var go = new GameObject("Ability marker");
            marker = Fx.NewLine(go.transform, U.Hex(0xff6a3a), 0.1f);
            marker.enabled = false;
        }

        public float CooldownLeft(AbilityDef a) => readyAt.TryGetValue(a, out var t) ? Mathf.Max(0, t - Time.time) : 0f;

        public bool Usable => GameManager.I.Phase == Phase.Combat && PlayerController.I.InputLive;

        void Update()
        {
            marker.enabled = false;
            if (!Usable) { aiming = null; return; }

            Handle(KeyCode.Q, Left);
            Handle(KeyCode.E, Right);
        }

        void Handle(KeyCode key, AbilityDef ability)
        {
            if (CooldownLeft(ability) > 0f) return;
            if (Input.GetKeyDown(key)) aiming = ability;
            if (aiming != ability) return;

            bool onTarget = TryAim(out var point, out var forward);
            if (Input.GetKey(key))
            {
                if (onTarget) DrawMarker(ability, point, forward);
                return;
            }

            // Released: call it in, or let it go if the crosshair was on the sky.
            aiming = null;
            if (onTarget) Fire(ability, point, forward);
        }

        /// <summary>Calls an ability in at a point, if it is off cooldown. Returns whether it fired.</summary>
        public bool Fire(AbilityDef ability, Vector3 point, Vector3 forward)
        {
            if (!Usable || CooldownLeft(ability) > 0f) return false;
            readyAt[ability] = Time.time + Progression.AbilityCooldown(ability);
            if (ability == Abilities.Mortar) StartCoroutine(Mortar(point));
            else StartCoroutine(BombingRun(point, forward));
            return true;
        }

        bool TryAim(out Vector3 point, out Vector3 forward)
        {
            var ray = PlayerController.I.AimRay;
            forward = ray.direction;
            forward.y = 0;
            forward.Normalize();
            point = default;
            if (!Physics.Raycast(ray, out var hit, AimRange, Layers.GroundMask | Layers.WallMask, QueryTriggerInteraction.Ignore))
                return false;
            point = hit.point;
            return true;
        }

        void DrawMarker(AbilityDef ability, Vector3 point, Vector3 forward)
        {
            marker.enabled = true;
            var lift = point + Vector3.up * 0.08f;
            if (ability.Radius > 0)
            {
                Fx.SetCircle(marker, lift, Progression.AbilityRadius(ability));
                return;
            }
            var right = Vector3.Cross(Vector3.up, forward) * ability.Width / 2;
            var far = forward * ability.Length;
            marker.loop = true;
            marker.positionCount = 4;
            marker.SetPositions(new[] { lift - right, lift + right, lift + far + right, lift + far - right });
        }

        IEnumerator Mortar(Vector3 point)
        {
            var shell = Shapes.Prim(PrimitiveType.Sphere, null, point + Vector3.up * 30f, Vector3.one * 0.5f, Mats.Lit(U.Hex(0x22262b)));
            Fx.Ring(point + Vector3.up * 0.1f, Progression.AbilityRadius(Abilities.Mortar), U.Hex(0xff6a3a), MortarDelay);
            for (float t = 0; t < MortarDelay; t += Time.deltaTime)
            {
                shell.transform.position = Vector3.Lerp(point + Vector3.up * 30f, point, t / MortarDelay);
                yield return null;
            }
            Destroy(shell);
            Damage.Splash(point, Progression.AbilityRadius(Abilities.Mortar), Progression.AbilityDamage(Abilities.Mortar));
            Fx.Burst(point + Vector3.up * 0.5f, Progression.AbilityRadius(Abilities.Mortar), U.Hex(0xffa040), 0.5f);
            Fx.Burst(point + Vector3.up * 1.5f, Progression.AbilityRadius(Abilities.Mortar) * 0.6f, U.Hex(0xfff0a0), 0.25f);
        }

        IEnumerator BombingRun(Vector3 start, Vector3 forward)
        {
            // Each orc is hit once, however many bursts it stands in.
            var struck = new HashSet<Enemy>();
            float step = Abilities.BombingRun.Length / (StrafeBursts - 1);
            for (int i = 0; i < StrafeBursts; i++)
            {
                var p = start + forward * step * i;
                Damage.Splash(p, Abilities.BombingRun.Width / 2, Progression.AbilityDamage(Abilities.BombingRun), struck);
                Fx.Burst(p + Vector3.up * 0.5f, Abilities.BombingRun.Width / 2, U.Hex(0xffa040), 0.4f);
                yield return new WaitForSeconds(StrafeSeconds / StrafeBursts);
            }
        }
    }
}
