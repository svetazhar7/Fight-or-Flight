using System;
using System.Collections.Generic;
using UnityEngine;
using FishNet.Object;

namespace FightOrFlight.Aircraft
{
    /// <summary>
    /// Central hub for the plane's failure state. Two jobs:
    ///  1) Aggregate all active <see cref="IPlaneCondition"/>s into the per-tick
    ///     <see cref="PlaneModifierState"/> (called by PlaneController before physics).
    ///  2) Provide a single event bus for part breakage (scoring, SFX, AI reactions).
    ///
    /// Every future mechanic plugs in here: detachable things are PlaneParts, ongoing
    /// effects are IPlaneConditions. The flight model never changes.
    /// </summary>
    public class PlaneDamageSystem : NetworkBehaviour
    {
        [Tooltip("All damageable parts (wings, propeller, tail...).")]
        [SerializeField] private PlanePart[] _parts;

        [Tooltip("Components implementing IPlaneCondition (icing, overheat, storm...).")]
        [SerializeField] private MonoBehaviour[] _conditionBehaviours;

        private readonly List<IPlaneCondition> _conditions = new();

        public event Action<PlanePart> OnAnyPartBroken;

        private void Awake()
        {
            if (_conditionBehaviours != null)
            {
                for (int i = 0; i < _conditionBehaviours.Length; i++)
                {
                    if (_conditionBehaviours[i] is IPlaneCondition condition)
                        _conditions.Add(condition);
                    else if (_conditionBehaviours[i] != null)
                        Debug.LogWarning($"{_conditionBehaviours[i].name} does not implement IPlaneCondition.", this);
                }
            }

            if (_parts != null)
            {
                for (int i = 0; i < _parts.Length; i++)
                    if (_parts[i] != null)
                        _parts[i].OnPartBroken += HandlePartBroken;
            }
        }

        private void OnDestroy()
        {
            if (_parts == null)
                return;
            for (int i = 0; i < _parts.Length; i++)
                if (_parts[i] != null)
                    _parts[i].OnPartBroken -= HandlePartBroken;
        }

        /// <summary>
        /// Called every tick by PlaneController BEFORE physics. Reads only synced
        /// condition state, so it replays deterministically during reconcile.
        /// </summary>
        public void ComposeModifiers(PlaneModifierState state)
        {
            for (int i = 0; i < _conditions.Count; i++)
            {
                IPlaneCondition condition = _conditions[i];
                if (condition.IsActive)
                    condition.Contribute(state);
            }
        }

        private void HandlePartBroken(PlanePart part) => OnAnyPartBroken?.Invoke(part);
    }
}
