using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;

namespace FightOrFlight.Aircraft
{
    /// <summary>
    /// Example condition: engine overheat. Cuts available thrust as heat rises.
    /// Drive <see cref="SetHeat"/> from the server (e.g. boost usage raises heat,
    /// time/altitude cools it). Same pattern fits fire, fuel starvation, etc.
    /// </summary>
    public class EngineOverheatCondition : NetworkBehaviour, IPlaneCondition
    {
        [SerializeField, Range(0f, 1f)] private float _maxThrustLossAtFull = 0.7f;

        private readonly SyncVar<float> _heat = new(); // 0..1

        public float Heat => _heat.Value;
        public bool IsActive => _heat.Value > 0.001f;

        [Server]
        public void SetHeat(float value) => _heat.Value = Mathf.Clamp01(value);

        public void Contribute(PlaneModifierState state)
        {
            state.ThrustMultiplier *= 1f - _maxThrustLossAtFull * _heat.Value;
        }
    }
}
