using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// Orc Horde's two hands: the loadout's left ability on Q, its right on E,
    /// Mortar and Bombing Run until the skill tree opens others. Hold to aim at
    /// the crosshair, release to call it in. The Rallying Cry needs no aim:
    /// it goes the moment the key does.
    /// </summary>
    public sealed class PlayerAbilities : MonoBehaviour
    {
        public static PlayerAbilities I { get; private set; }

        const float AimRange = 120f;
        const float MortarDelay = 0.9f;
        const float StrafeSeconds = 0.8f;
        const int StrafeBursts = 9;

        public AbilityDef Left => Progression.Loadout[0];
        public AbilityDef Right => Progression.Loadout[1];

        /// <summary>Why the crosshair's spot won't take the ability being aimed, for the HUD; null when it will.</summary>
        public string AimProblem { get; private set; }

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
            AimProblem = null;
            if (!Usable) { aiming = null; return; }

            Handle(KeyCode.Q, Left);
            Handle(KeyCode.E, Right);
        }

        void Handle(KeyCode key, AbilityDef ability)
        {
            if (ability == null || CooldownLeft(ability) > 0f) return;
            if (ability == Abilities.RallyingCry)
            {
                if (Input.GetKeyDown(key)) Fire(ability, transform.position, transform.forward);
                return;
            }
            if (Input.GetKeyDown(key)) aiming = ability;
            if (aiming != ability) return;

            bool onTarget = TryAim(out var point, out var forward);
            if (onTarget) AimProblem = Problem(ability, point);
            if (Input.GetKey(key))
            {
                if (onTarget) DrawMarker(ability, point, forward);
                return;
            }

            // Released: call it in, or let it go if the crosshair was on the sky or the spot won't take it.
            aiming = null;
            if (!onTarget) return;
            if (AimProblem != null) { Sfx.UI("ui_invalid"); return; }
            Fire(ability, point, forward);
        }

        /// <summary>Mines and slicks go on the road, where the horde walks; a sentry on any open ground, not on rock.</summary>
        static string Problem(AbilityDef ability, Vector3 point)
        {
            if ((ability == Abilities.ProximityMines || ability == Abilities.OilSlick) && !AbilityFields.OnRoad(point))
                return "Aim at the road";
            if ((ability == Abilities.SentryDrop || ability == Abilities.DecoyBeacon) && !AbilityFields.OpenGround(point))
                return "Aim at open ground";
            return null;
        }

        /// <summary>Calls an ability in at a point, if it is off cooldown. Returns whether it fired.</summary>
        public bool Fire(AbilityDef ability, Vector3 point, Vector3 forward, bool force = false)
        {
            if (!force && (!Usable || CooldownLeft(ability) > 0f)) return false;
            readyAt[ability] = Time.time + Progression.AbilityCooldown(ability);
            switch (ability.Id)
            {
                case "mortar": StartCoroutine(Mortar(point)); break;
                case "bombing_run": StartCoroutine(BombingRun(point, forward)); break;
                case "emergency_repairs": AbilityFields.Repair(point, ability); break;
                case "oil_slick": AbilityFields.Spill(point, ability); break;
                case "rallying_cry": AbilityFields.StartRally(ability); break;
                case "sentry_drop": AbilityFields.DropSentries(point, forward, ability); break;
                case "proximity_mines": AbilityFields.LayMines(point, ability); break;
                case "artillery_barrage": AbilityFields.CallBarrage(point, ability); break;
                case "decoy_beacon": AbilityFields.PlantBeacon(point, ability); break;
            }
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
            var c = AimProblem == null ? ability.Color : new Color(1f, 0.3f, 0.3f);
            marker.startColor = marker.endColor = c;
            var lift = point + Vector3.up * 0.08f;
            // A sentry shows what it will cover; everything else its own reach.
            float radius = ability == Abilities.SentryDrop
                ? Progression.Tower(Progression.AbilityTrait(ability, "sentryCannon") > 0 ? Towers.Cannon : Towers.MachineGun).Range
                : Progression.AbilityRadius(ability);
            if (radius > 0)
            {
                Fx.SetGroundCircle(marker, point, radius);
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
            Sfx.At("mortar_whistle", point);
            for (float t = 0; t < MortarDelay; t += Time.deltaTime)
            {
                shell.transform.position = Vector3.Lerp(point + Vector3.up * 30f, point, t / MortarDelay);
                yield return null;
            }
            Destroy(shell);
            Damage.Splash(point, Progression.AbilityRadius(Abilities.Mortar), Progression.AbilityDamage(Abilities.Mortar));
            Combat.Flash(point + Vector3.up, U.Hex(0xffa040), Progression.AbilityRadius(Abilities.Mortar) * 5f, 7f, 0.4f);
            Combat.Scorch(point, Progression.AbilityRadius(Abilities.Mortar) * 0.75f);
            Combat.Debris(point, 8f, U.Hex(0x6b5a48), 10);
            Combat.Shake(point, 0.6f, 26f);
            Sfx.At("explosion", point, 1.2f, 0.85f);
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
                if (i % 2 == 0)
                {
                    Sfx.At("explosion", p, 0.9f);
                    Combat.Flash(p + Vector3.up, U.Hex(0xffa040), Abilities.BombingRun.Width * 3f, 4f, 0.3f);
                }
                Combat.Scorch(p, Abilities.BombingRun.Width * 0.35f);
                Combat.Shake(p, 0.18f, 22f);
                Fx.Burst(p + Vector3.up * 0.5f, Abilities.BombingRun.Width / 2, U.Hex(0xffa040), 0.4f);
                yield return new WaitForSeconds(StrafeSeconds / StrafeBursts);
            }
        }
    }
}
