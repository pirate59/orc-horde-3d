using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// Builds an orc's skinned body and moves its bones every frame, with no
    /// animation clips: a walk cycle paced by how fast the orc actually travels,
    /// a weapon swing while it attacks, a crouch while it winds up, a flinch when
    /// hit, and a fall when it dies.
    /// </summary>
    public sealed class OrcAnimator : MonoBehaviour
    {
        static Material material;

        /// <summary>True while hitting a wall or the player: swing the weapon.</summary>
        public bool Attacking { get; set; }
        /// <summary>True while standing to gather a war band or wind up a charge.</summary>
        public bool Holding { get; set; }
        /// <summary>When set (0 or more), animates as if moving at this speed: for the gallery and puppets.</summary>
        public float OverrideSpeed { get; set; } = -1f;
        public Transform Head => bones[(int)OrcBone.Head];

        EnemyDef def;
        OrcDesigns.Design design;
        Transform model;
        Transform[] bones;
        Vector3[] rest;
        float phase, speed, flinch, attackT, attackWeight, holdWeight, dieT = -1f, fallSign, stride, lean;
        Vector3 lastPos;
        bool twoHanded, shield;

        public static OrcAnimator Create(EnemyDef def, Transform enemy)
        {
            var (mesh, layout, design) = OrcDesigns.Get(def);
            var model = new GameObject("Model").transform;
            model.SetParent(enemy, false);
            float scale = def.Height / OrcRig.ModelHeight;
            model.localScale = Vector3.one * scale;

            var bones = OrcRig.CreateBones(model, layout);
            var smr = model.gameObject.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = bones[(int)OrcBone.Hips];
            smr.sharedMaterial = Material;
            // Room for a swinging maul and a fall, so the body is never culled while on screen.
            var b = mesh.bounds;
            b.Expand(1.2f);
            b.center -= bones[(int)OrcBone.Hips].localPosition;
            smr.localBounds = b;

            // The head is its own hitbox, riding the head bone so headshots track the animation.
            var head = new GameObject("Head hitbox");
            head.transform.SetParent(bones[(int)OrcBone.Head], false);
            var sphere = head.AddComponent<SphereCollider>();
            sphere.radius = 0.14f * design.Build.HeadSize;
            sphere.center = new Vector3(0, 0.05f, 0.02f) * design.Build.HeadSize;
            head.AddComponent<EnemyHead>();

            var a = enemy.gameObject.AddComponent<OrcAnimator>();
            a.def = def;
            a.design = design;
            a.model = model;
            a.bones = bones;
            a.rest = new Vector3[bones.Length];
            for (int i = 0; i < bones.Length; i++) a.rest[i] = bones[i].localPosition;
            a.lastPos = enemy.position;
            a.phase = Random.value * Mathf.PI * 2;
            // One step covers a good part of the leg's length.
            a.stride = 0.5f * def.Height * design.Build.LegLength;
            var w = design.Weapon;
            a.twoHanded = w == OrcDesigns.Weapon.Hammer || w == OrcDesigns.Weapon.Maul || w == OrcDesigns.Weapon.RuneHammer || w == OrcDesigns.Weapon.WarAxe;
            a.shield = w == OrcDesigns.Weapon.MaceAndShield;
            a.lean = 4f + 14f * design.Build.Hunch;
            return a;
        }

        static Material Material
        {
            get
            {
                if (material != null) return material;
                material = new Material(Shader.Find("OrcHorde/Orc"));
                material.SetTexture("_Detail", Mats.Tex("skin"));
                material.SetTexture("_DetailNormal", Mats.Tex("skin_n"));
                material.SetTextureScale("_Detail", new Vector2(2, 2));
                material.enableInstancing = false;
                return material;
            }
        }

        public string DebugState =>
            $"speed={speed:F2} attack={attackWeight:F2}/{attackT:F2} hold={holdWeight:F2} die={dieT:F2} upperArmR={bones[(int)OrcBone.UpperArmR].localEulerAngles:F0} yaw={transform.eulerAngles.y:F0}";

        public void Flinch(float amount) => flinch = Mathf.Min(1f, flinch + amount);

        /// <summary>Starts the fall. The body lies a moment, then sinks; the owner destroys it.</summary>
        public void Die()
        {
            dieT = 0f;
            fallSign = Random.value < 0.5f ? -1f : 1f;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (dieT >= 0f) { Fall(dt); return; }

            var delta = transform.position - lastPos;
            lastPos = transform.position;
            delta.y = 0;
            float measured = OverrideSpeed >= 0 ? OverrideSpeed : delta.magnitude / dt;
            speed = Mathf.Lerp(speed, measured, 1f - Mathf.Exp(-dt * 10f));
            phase += speed / Mathf.Max(stride, 0.1f) * Mathf.PI * dt;

            attackWeight = Mathf.MoveTowards(attackWeight, Attacking ? 1f : 0f, dt * 5f);
            holdWeight = Mathf.MoveTowards(holdWeight, Holding ? 1f : 0f, dt * 4f);
            if (attackWeight > 0f) attackT += dt / 0.85f;
            flinch = Mathf.MoveTowards(flinch, 0f, dt * 5f);

            Pose(Mathf.Clamp01(speed / Mathf.Max(def.Speed, 0.5f)) * (1f - holdWeight));
        }

        void Set(OrcBone b, float x, float y = 0, float z = 0) => bones[(int)b].localRotation = Quaternion.Euler(x, y, z);

        /// <summary>
        /// Positive X pitches an upward bone forward and swings a hanging one back;
        /// negative swings a hanging limb forward.
        /// </summary>
        void Pose(float gait)
        {
            float s = Mathf.Sin(phase), c = Mathf.Cos(phase);
            float t = Time.time;
            float bulk = design.Build.Bulk;

            // Legs: thighs swing opposite, knees fold through the swing, feet stay flat.
            float swing = 30f * gait;
            float kneeL = 6f + 50f * gait * Mathf.Max(0, c), kneeR = 6f + 50f * gait * Mathf.Max(0, -c);
            float crouch = holdWeight * 25f;
            Set(OrcBone.ThighL, -swing * s - crouch, 0, -2);
            Set(OrcBone.ThighR, swing * s - crouch, 0, 2);
            Set(OrcBone.ShinL, kneeL + crouch * 1.6f);
            Set(OrcBone.ShinR, kneeR + crouch * 1.6f);
            Set(OrcBone.FootL, swing * s * 0.4f - kneeL * 0.5f - crouch * 0.6f);
            Set(OrcBone.FootR, -swing * s * 0.4f - kneeR * 0.5f - crouch * 0.6f);

            // Hips bob and roll with the step; the spine twists against them.
            var hips = bones[(int)OrcBone.Hips];
            hips.localPosition = rest[(int)OrcBone.Hips] + new Vector3(0, 0.025f * gait * Mathf.Cos(2 * phase) - 0.1f * holdWeight, 0);
            hips.localRotation = Quaternion.Euler(0, 7f * gait * s, 4f * gait * s);
            float breathe = Mathf.Sin(t * 2.1f) * 1.5f * (1 - gait);
            Set(OrcBone.Spine, lean * 0.4f + 6f * gait - flinch * 10f + holdWeight * 12f, -9f * gait * s);
            Set(OrcBone.Chest, lean * 0.6f + 6f * gait + breathe - flinch * 8f + holdWeight * 10f);
            Set(OrcBone.Neck, -lean * 0.5f - 4f * gait - holdWeight * 10f);
            Set(OrcBone.Head, -lean * 0.4f - 3f * gait + flinch * -12f + Mathf.Sin(t * 0.7f) * 4f * (1 - gait),
                Mathf.Sin(t * 0.37f + phase) * 10f * (1 - gait));
            Set(OrcBone.Jaw, 6f + attackWeight * 14f * Mathf.Abs(Mathf.Sin(attackT * Mathf.PI * 2)) + flinch * 18f);

            // Arms swing against the legs, held out from a broad body.
            float armOut = 8f + 9f * (bulk - 1f);
            float armSwing = 24f * gait;
            Set(OrcBone.ClavicleL, 0, 0, 0);
            Set(OrcBone.ClavicleR, 0, 0, 0);
            Set(OrcBone.UpperArmL, armSwing * s + holdWeight * 30f, 0, -armOut);
            Set(OrcBone.ForearmL, -18f - 14f * gait - holdWeight * 20f);
            Set(OrcBone.UpperArmR, -armSwing * s * 0.6f + holdWeight * 30f, 0, armOut);
            Set(OrcBone.ForearmR, -35f - 10f * gait - holdWeight * 20f);
            Set(OrcBone.HandL, 0);
            Set(OrcBone.HandR, 0);

            if (shield)
            {
                // The shield arm stays up in front.
                Set(OrcBone.UpperArmL, -40f, 20f, -armOut - 6f);
                Set(OrcBone.ForearmL, -55f);
            }

            if (attackWeight > 0f)
            {
                // Overhead and down: a long wind-up, a fast strike, a short recovery.
                float k = attackT % 1f;
                float raise = k < 0.55f ? Mathf.SmoothStep(0, 1, k / 0.55f) : k < 0.72f ? 1f - (k - 0.55f) / 0.17f * 1.25f : Mathf.Lerp(-0.25f, 0f, (k - 0.72f) / 0.28f);
                float arm = Mathf.Lerp(0f, -150f, raise) * attackWeight;
                Blend(OrcBone.UpperArmR, Quaternion.Euler(arm, -10f * raise, armOut), attackWeight);
                Blend(OrcBone.ForearmR, Quaternion.Euler(-30f - 30f * raise, 0, 0), attackWeight);
                Blend(OrcBone.Spine, Quaternion.Euler(lean * 0.4f - 10f * raise + 15f * (1 - raise), -15f * raise, 0), attackWeight);
                if (twoHanded)
                {
                    Blend(OrcBone.UpperArmL, Quaternion.Euler(arm * 0.95f, 20f * raise, -armOut), attackWeight);
                    Blend(OrcBone.ForearmL, Quaternion.Euler(-40f - 20f * raise, 0, 0), attackWeight);
                }
            }
        }

        void Blend(OrcBone b, Quaternion target, float weight) =>
            bones[(int)b].localRotation = Quaternion.Slerp(bones[(int)b].localRotation, target, weight);

        /// <summary>Topples over backwards or forwards about the feet, limbs going slack, then sinks away.</summary>
        void Fall(float dt)
        {
            dieT += dt;
            float k = Mathf.Clamp01(dieT / 0.55f);
            float ease = k * k;
            model.localRotation = Quaternion.Euler(fallSign * 84f * ease, 0, 6f * ease);
            Set(OrcBone.Spine, fallSign * 10f * ease);
            Set(OrcBone.Head, fallSign * 25f * ease, 15f * ease);
            Set(OrcBone.Jaw, 20f * ease);
            Set(OrcBone.UpperArmL, -fallSign * 40f * ease, 0, -55f * ease);
            Set(OrcBone.UpperArmR, -fallSign * 30f * ease, 0, 60f * ease);
            Set(OrcBone.ForearmL, -20f);
            Set(OrcBone.ForearmR, -30f);
            Set(OrcBone.ThighL, -20f * ease);
            Set(OrcBone.ThighR, 10f * ease);
            Set(OrcBone.ShinL, 35f * ease);
            Set(OrcBone.ShinR, 20f * ease);
            if (dieT > 2.2f) model.localPosition += Vector3.down * dt * 0.45f * model.localScale.y;
        }
    }
}
