using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;

namespace FightOrFlight.Aircraft
{
    /// <summary>
    /// Example condition: airframe icing. Reduces lift &amp; control, increases drag,
    /// scaled by an icing amount the server drives from weather. Template for every
    /// other "ongoing effect" mechanic — copy, change the math, add to the damage
    /// system's condition list.
    /// </summary>
    public class IcingCondition : NetworkBehaviour, IPlaneCondition
    {
        [SerializeField, Range(0f, 1f)] private float _liftLossAtFull = 0.45f;
        [SerializeField, Range(0f, 1f)] private float _dragGainAtFull = 0.6f;
        [SerializeField, Range(0f, 1f)] private float _controlLossAtFull = 0.3f;

        private readonly SyncVar<float> _ice = new(); // 0..1

        public float Ice => _ice.Value;
        public bool IsActive => _ice.Value > 0.001f;

        [Server]
        public void SetIce(float amount) => _ice.Value = Mathf.Clamp01(amount);

        public void Contribute(PlaneModifierState state)
        {
            float ice = _ice.Value;
            state.LiftMultiplier *= 1f - _liftLossAtFull * ice;
            state.DragMultiplier *= 1f + _dragGainAtFull * ice;
            state.ControlMultiplier *= 1f - _controlLossAtFull * ice;
        }
    }
}
