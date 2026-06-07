using UnityEngine;

namespace FightOrFlight.Aircraft
{
    /// <summary>
    /// Tunable engine data. Lives in an asset so designers can balance without
    /// recompiling and so different aircraft can reuse the same engine code.
    /// Create via: Assets ▸ Create ▸ Fight or Flight ▸ Plane ▸ Engine Config.
    /// </summary>
    [CreateAssetMenu(fileName = "PlaneEngineConfig",
        menuName = "Fight or Flight/Plane/Engine Config")]
    public class PlaneEngineConfig : ScriptableObject
    {
        [Header("Thrust")]
        [Tooltip("Newtons at full throttle.")]
        public float MaxThrust = 8000f;

        [Tooltip("How fast throttle (0..1) moves toward the input, per second.")]
        public float ThrottleChangeRate = 0.6f;

        [Header("Boost (ground, hold E)")]
        public bool AllowBoost = true;
        public float BoostMultiplier = 1.5f;

        [Header("RPM — drives propeller + audio only, not physics")]
        [Range(0f, 1f)] public float IdleRpm01 = 0.15f;

        [Tooltip("How fast normalized RPM chases the throttle.")]
        public float RpmSpool = 1.5f;
    }
}
