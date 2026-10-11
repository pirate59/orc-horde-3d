using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>The bones every orc shares. Indices into the skinned mesh's bone array.</summary>
    public enum OrcBone
    {
        Hips, Spine, Chest, Neck, Head, Jaw,
        ClavicleL, UpperArmL, ForearmL, HandL,
        ClavicleR, UpperArmR, ForearmR, HandR,
        ThighL, ShinL, FootL,
        ThighR, ShinR, FootR,
        Count,
    }

    /// <summary>How an orc is built: one set per type, scaling a shared skeleton.</summary>
    public struct OrcBuild
    {
        /// <summary>Width of shoulders, chest and limbs, against a lean orc's 1.</summary>
        public float Bulk;
        /// <summary>How far the head and shoulders hang forward, 0 to 1.</summary>
        public float Hunch;
        public float ArmLength, LegLength, HeadSize, Belly;

        public static OrcBuild Standard => new OrcBuild { Bulk = 1, Hunch = 0.4f, ArmLength = 1, LegLength = 1, HeadSize = 1, Belly = 0 };
    }

    /// <summary>
    /// The orc skeleton: where each bone sits at rest, in model space, for a
    /// given build. Every rig is authored 1.8 units tall at the crown and scaled
    /// to its enemy's height as a whole. Bind rotations are all identity, so a
    /// bone's bind pose is just its position.
    /// </summary>
    public static class OrcRig
    {
        public const float ModelHeight = 1.8f;

        static readonly OrcBone[] Parents =
        {
            OrcBone.Count, OrcBone.Hips, OrcBone.Spine, OrcBone.Chest, OrcBone.Neck, OrcBone.Head,
            OrcBone.Chest, OrcBone.ClavicleL, OrcBone.UpperArmL, OrcBone.ForearmL,
            OrcBone.Chest, OrcBone.ClavicleR, OrcBone.UpperArmR, OrcBone.ForearmR,
            OrcBone.Hips, OrcBone.ThighL, OrcBone.ShinL,
            OrcBone.Hips, OrcBone.ThighR, OrcBone.ShinR,
        };

        public static OrcBone Parent(OrcBone b) => Parents[(int)b];

        /// <summary>Rest positions of every bone for this build, in model space.</summary>
        public static Vector3[] Layout(OrcBuild b)
        {
            var p = new Vector3[(int)OrcBone.Count];
            float leg = b.LegLength, arm = b.ArmLength, h = b.Hunch, w = b.Bulk;

            float foot = 0.07f, knee = foot + 0.4f * leg, hip = foot + 0.8f * leg;
            p[(int)OrcBone.Hips] = new Vector3(0, hip + 0.03f, 0);
            p[(int)OrcBone.Spine] = new Vector3(0, hip + 0.18f, 0.01f + 0.02f * h);
            p[(int)OrcBone.Chest] = new Vector3(0, hip + 0.38f - 0.03f * h, 0.03f + 0.07f * h);
            p[(int)OrcBone.Neck] = new Vector3(0, hip + 0.6f - 0.07f * h, 0.06f + 0.14f * h);
            p[(int)OrcBone.Head] = new Vector3(0, hip + 0.69f - 0.1f * h, 0.09f + 0.2f * h);
            p[(int)OrcBone.Jaw] = p[(int)OrcBone.Head] + new Vector3(0, -0.05f, 0.06f) * b.HeadSize;

            var chest = p[(int)OrcBone.Chest];
            foreach (var (clav, upper, fore, hand, side) in new[]
            {
                (OrcBone.ClavicleL, OrcBone.UpperArmL, OrcBone.ForearmL, OrcBone.HandL, -1f),
                (OrcBone.ClavicleR, OrcBone.UpperArmR, OrcBone.ForearmR, OrcBone.HandR, 1f),
            })
            {
                p[(int)clav] = chest + new Vector3(side * 0.07f * w, 0.17f, 0);
                p[(int)upper] = chest + new Vector3(side * 0.24f * w, 0.15f, -0.01f);
                p[(int)fore] = p[(int)upper] + new Vector3(side * 0.04f * w, -0.3f * arm, 0.02f);
                p[(int)hand] = p[(int)fore] + new Vector3(side * 0.01f, -0.28f * arm, 0.05f);
            }

            foreach (var (thigh, shin, ft, side) in new[]
            {
                (OrcBone.ThighL, OrcBone.ShinL, OrcBone.FootL, -1f),
                (OrcBone.ThighR, OrcBone.ShinR, OrcBone.FootR, 1f),
            })
            {
                p[(int)thigh] = new Vector3(side * 0.1f * w, hip, 0);
                p[(int)shin] = new Vector3(side * 0.11f * w, knee, 0.025f);
                p[(int)ft] = new Vector3(side * 0.12f * w, foot, -0.01f);
            }
            return p;
        }

        /// <summary>Bind poses: each bone's inverse transform at rest, relative to the model root.</summary>
        public static Matrix4x4[] BindPoses(Vector3[] layout)
        {
            var poses = new Matrix4x4[layout.Length];
            for (int i = 0; i < layout.Length; i++) poses[i] = Matrix4x4.Translate(-layout[i]);
            return poses;
        }

        /// <summary>Makes the bone transforms under <paramref name="model"/>, at rest.</summary>
        public static Transform[] CreateBones(Transform model, Vector3[] layout)
        {
            var bones = new Transform[layout.Length];
            for (int i = 0; i < layout.Length; i++)
            {
                bones[i] = new GameObject(((OrcBone)i).ToString()).transform;
            }
            for (int i = 0; i < layout.Length; i++)
            {
                var parent = Parents[i];
                bones[i].SetParent(parent == OrcBone.Count ? model : bones[(int)parent], false);
                bones[i].localPosition = parent == OrcBone.Count ? layout[i] : layout[i] - layout[(int)parent];
                bones[i].localRotation = Quaternion.identity;
            }
            return bones;
        }
    }
}
