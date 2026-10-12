using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// Life in the air around the player: snow drifting down on snow maps, and
    /// at night fireflies over grass and dust motes catching the moon elsewhere.
    /// One particle system that follows the camera, so the whole map seems full
    /// of it at the cost of a small box.
    /// </summary>
    public sealed class Motes : MonoBehaviour
    {
        ParticleSystem system;

        public static void Create(Transform parent, string biome, Mood mood)
        {
            bool snow = biome == "snow";
            if (!snow && mood != Mood.Night) return;
            var go = new GameObject("Motes");
            go.transform.SetParent(parent, false);
            var m = go.AddComponent<Motes>();
            var ps = m.system = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = snow ? 1400 : 220;
            main.startLifetime = snow ? 7f : 6f;
            main.startSpeed = 0f;
            main.startSize = snow ? new ParticleSystem.MinMaxCurve(0.04f, 0.09f) : new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.startColor = snow ? new Color(0.95f, 0.97f, 1f, 0.85f)
                : biome == "grassland" ? new Color(0.85f, 1f, 0.45f, 1f) : new Color(0.75f, 0.85f, 1f, 0.6f);
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.rateOverTime = snow ? (mood == Mood.Night ? 140f : 190f) : 36f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = snow ? new Vector3(44, 1, 44) : new Vector3(40, 6, 40);
            shape.position = snow ? new Vector3(0, 12, 0) : new Vector3(0, 2.5f, 0);

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            if (snow)
            {
                velocity.x = new ParticleSystem.MinMaxCurve(-0.4f, 0.6f);
                velocity.y = new ParticleSystem.MinMaxCurve(-2.2f, -1.4f);
                velocity.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            }
            else
            {
                velocity.x = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
                velocity.y = new ParticleSystem.MinMaxCurve(-0.15f, 0.2f);
                velocity.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
                // Fireflies wander and pulse.
                var noise = ps.noise;
                noise.enabled = true;
                noise.strength = 0.6f;
                noise.frequency = 0.4f;
                var fade = ps.colorOverLifetime;
                fade.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.2f), new GradientAlphaKey(0.2f, 0.45f),
                            new GradientAlphaKey(1, 0.7f), new GradientAlphaKey(0, 1) });
                fade.color = g;
            }

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            // Fireflies glow: bright enough to catch the bloom.
            r.sharedMaterial = snow ? Mats.Unlit(Color.white) : Mats.Unlit(new Color(2.2f, 2.2f, 2.2f, 1f));
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null) transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y - 1.5f, cam.transform.position.z);
        }
    }
}
