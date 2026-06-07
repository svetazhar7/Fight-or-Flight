using UnityEngine;

namespace FightOrFlight.Aircraft
{
    public enum DamageType
    {
        Generic,
        Collision,
        Fire,
        Explosion
    }

    public struct DamageInfo
    {
        public float Amount;
        public Vector3 Point;
        public Vector3 Direction;
        public DamageType Type;

        public DamageInfo(float amount, Vector3 point, Vector3 direction, DamageType type = DamageType.Generic)
        {
            Amount = amount;
            Point = point;
            Direction = direction;
            Type = type;
        }
    }

    /// <summary>Anything that can take damage (parts, and later the cargo, crew, etc.).</summary>
    public interface IDamageable
    {
        void ApplyDamage(in DamageInfo info);
    }
}
