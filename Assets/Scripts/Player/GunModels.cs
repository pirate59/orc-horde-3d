using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>The rifle's moving parts.</summary>
    public sealed class GunRig
    {
        public Transform Muzzle, Ejector, Magazine;
        public Vector3 MagazineRest;
    }

    /// <summary>
    /// The player's rifle as a built thing: an orc-hunter's carbine. A steel
    /// receiver on a wooden stock and grip, a shrouded barrel ending in a slotted
    /// brake, a holo sight with a mint reticle, a mint power strip down the side
    /// to match the HUD, and a curved box magazine that comes out to reload.
    /// </summary>
    public static class GunModels
    {
        public static GunRig Rifle(Transform gun)
        {
            var steel = new Color(0.32f, 0.34f, 0.37f);
            var dark = new Color(0.12f, 0.12f, 0.13f);
            var wood = new Color(0.42f, 0.27f, 0.15f);
            var mint = U.Hex(0x5effc4);
            var brass = new Color(0.8f, 0.62f, 0.3f);

            Part(gun, "rifle-body", m =>
            {
                // Receiver and top rail.
                m.Paint(0, steel, 0.5f).Box(new Vector3(0, 0, 0.02f), new Vector3(0.07f, 0.085f, 0.32f), Quaternion.identity);
                m.Paint(0, dark, 0.45f).Box(new Vector3(0, 0.05f, 0.04f), new Vector3(0.034f, 0.016f, 0.28f), Quaternion.identity);
                for (int i = 0; i < 9; i++) m.Box(new Vector3(0, 0.061f, -0.08f + i * 0.03f), new Vector3(0.036f, 0.006f, 0.012f), Quaternion.identity);
                m.Paint(0, dark, 0.4f).Box(new Vector3(0.0365f, 0.012f, 0.06f), new Vector3(0.004f, 0.022f, 0.06f), Quaternion.identity);
                m.Paint(0, steel * 1.2f, 0.55f).Capsule(new Vector3(0.04f, 0.025f, -0.05f), new Vector3(0.06f, 0.025f, -0.05f), 0.008f, 0.01f, 6, 1);

                // The barrel in its vented shroud, and the brake.
                m.Paint(0, dark, 0.5f).Cylinder(new Vector3(0, 0.01f, 0.17f), new Vector3(0, 0.01f, 0.52f), 0.013f, 12);
                m.Paint(0, steel, 0.5f).Cylinder(new Vector3(0, 0.01f, 0.17f), new Vector3(0, 0.01f, 0.38f), 0.027f, 14);
                m.Paint(0, dark, 0.45f);
                for (int i = 0; i < 5; i++) m.Torus(new Vector3(0, 0.01f, 0.2f + i * 0.04f), Vector3.forward, 0.028f, 0.004f, 14, 4);
                m.Paint(0, steel * 0.85f, 0.5f).Box(new Vector3(0, 0.01f, 0.535f), new Vector3(0.036f, 0.028f, 0.05f), Quaternion.identity);
                m.Paint(0, dark).Box(new Vector3(0, 0.01f, 0.535f), new Vector3(0.038f, 0.008f, 0.012f), Quaternion.identity);

                // Wooden handguard, pistol grip and stock with a rubber butt.
                m.Paint(0, wood, 0.25f).Box(new Vector3(0, -0.032f, 0.24f), new Vector3(0.056f, 0.044f, 0.17f), Quaternion.identity);
                m.Box(new Vector3(0, -0.08f, -0.07f), new Vector3(0.034f, 0.1f, 0.045f), Quaternion.Euler(-16, 0, 0));
                m.Box(new Vector3(0, -0.012f, -0.24f), new Vector3(0.042f, 0.07f, 0.2f), Quaternion.Euler(4, 0, 0));
                m.Paint(0, dark, 0.15f).Box(new Vector3(0, -0.02f, -0.345f), new Vector3(0.044f, 0.085f, 0.02f), Quaternion.Euler(4, 0, 0));
                m.Paint(0, steel, 0.5f).Torus(new Vector3(0, -0.06f, -0.02f), Vector3.right, 0.026f, 0.004f, 14, 4);

                // Holo sight on the rail.
                m.Paint(0, dark, 0.4f).Box(new Vector3(0, 0.075f, -0.01f), new Vector3(0.05f, 0.03f, 0.06f), Quaternion.identity);
                foreach (var s in new[] { -1f, 1f })
                    m.Box(new Vector3(s * 0.024f, 0.105f, -0.025f), new Vector3(0.005f, 0.04f, 0.02f), Quaternion.identity);
                m.Box(new Vector3(0, 0.126f, -0.025f), new Vector3(0.052f, 0.006f, 0.02f), Quaternion.identity);

                // The power strip down the left side.
                m.Paint(0, mint, 0.6f, 1.6f).Box(new Vector3(-0.036f, 0.005f, 0.03f), new Vector3(0.003f, 0.01f, 0.22f), Quaternion.identity);
                m.Box(new Vector3(-0.0285f, -0.032f, 0.24f), new Vector3(0.003f, 0.006f, 0.12f), Quaternion.identity);
            });
            // The sight's reticle, floating in the open frame: glowing, with no shadow.
            Part(gun, "rifle-reticle", m =>
            {
                m.Paint(0, mint, 0.9f, 2.5f).Torus(new Vector3(0, 0.104f, -0.0265f), Vector3.forward, 0.008f, 0.0012f, 16, 4);
            });

            var mag = new GameObject("Magazine").transform;
            mag.SetParent(gun, false);
            mag.localPosition = new Vector3(0, -0.04f, 0.08f);
            Part(mag, "rifle-magazine", m =>
            {
                // Curved: three segments stepping forward as they drop.
                m.Paint(0, dark, 0.35f);
                for (int i = 0; i < 3; i++)
                    m.Box(new Vector3(0, -0.03f - i * 0.045f, i * 0.012f), new Vector3(0.03f, 0.05f, 0.05f), Quaternion.Euler(-12 - i * 4, 0, 0));
                m.Paint(0, brass, 0.6f).Capsule(new Vector3(0, 0.002f, -0.015f), new Vector3(0, 0.002f, 0.02f), 0.007f, 0.005f, 6, 1);
                m.Paint(0, mint, 0.6f, 1.2f).Box(new Vector3(-0.0155f, -0.08f, 0.012f), new Vector3(0.002f, 0.08f, 0.006f), Quaternion.Euler(-16, 0, 0));
            });

            foreach (var r in gun.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var rig = new GunRig { Magazine = mag, MagazineRest = mag.localPosition };
            rig.Muzzle = new GameObject("Muzzle").transform;
            rig.Muzzle.SetParent(gun, false);
            rig.Muzzle.localPosition = new Vector3(0, 0.01f, 0.57f);
            rig.Ejector = new GameObject("Ejector").transform;
            rig.Ejector.SetParent(gun, false);
            rig.Ejector.localPosition = new Vector3(0.04f, 0.015f, 0.06f);
            return rig;
        }

        static void Part(Transform parent, string key, System.Action<OrcMesh> draw)
        {
            var m = new OrcMesh();
            draw(m);
            var go = new GameObject(key);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = m.Build(key);
            go.AddComponent<MeshRenderer>().sharedMaterial = Material;
        }

        static Material material;

        /// <summary>The tower material without the biome's overlays: a gun in hand stays clean.</summary>
        static Material Material
        {
            get
            {
                if (material != null) return material;
                material = new Material(TowerModels.Material);
                material.SetFloat("_CoverAmount", 0);
                material.SetFloat("_GrimeAmount", 0);
                return material;
            }
        }
    }
}
