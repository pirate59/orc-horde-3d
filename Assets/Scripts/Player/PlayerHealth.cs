using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// Standing in the horde's road hurts. Falling only costs time: you come back
    /// at the base, and the round carries on without you.
    /// </summary>
    public sealed class PlayerHealth : MonoBehaviour
    {
        public static PlayerHealth I { get; private set; }

        public const float MaxHealth = 100f;
        const float RegenDelay = 4f, RegenPerSecond = 15f, RespawnSeconds = 5f;

        public float Health { get; private set; } = MaxHealth;
        public bool Alive => Health > 0f;
        public float RespawnIn => Mathf.Max(0, respawnAt - Time.time);
        public float LastHurt { get; private set; } = -10f;

        float respawnAt;

        void Awake() => I = this;

        public void TakeDamage(float amount)
        {
            if (!Alive || GameManager.I.Phase != Phase.Combat) return;
            Health -= amount;
            LastHurt = Time.time;
            if (Health <= 0f)
            {
                Health = 0f;
                respawnAt = Time.time + RespawnSeconds;
                GameManager.I.Announce("You were overrun — respawning at the base", U.Hex(0xff4a4a), RespawnSeconds);
            }
        }

        public void Revive()
        {
            if (!Alive) PlayerController.I.Teleport(MapBuilder.PlayerStart(GameManager.I.Map, GameManager.I.Grid).position);
            Health = MaxHealth;
        }

        void Update()
        {
            if (!Alive)
            {
                if (Time.time >= respawnAt) Revive();
                return;
            }
            if (Time.time - LastHurt > RegenDelay)
                Health = Mathf.Min(MaxHealth, Health + RegenPerSecond * Time.deltaTime);
        }
    }
}
