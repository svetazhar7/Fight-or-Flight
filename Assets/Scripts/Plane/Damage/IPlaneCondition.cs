namespace FightOrFlight.Aircraft
{
    /// <summary>
    /// A state effect that modifies flight characteristics without being a detachable
    /// part: icing, overheat, fire, storm, loss of control, cargo shift, ...
    ///
    /// Implement on a NetworkBehaviour, sync the intensity with a SyncVar, and add the
    /// component to PlaneDamageSystem's condition list. The flight model/engine never
    /// need to know it exists — that is the whole point of the modifier aggregation.
    /// </summary>
    public interface IPlaneCondition
    {
        /// <summary>Skip contribution entirely when false (perf + clarity).</summary>
        bool IsActive { get; }

        /// <summary>Multiply/add this condition's effect into the per-tick aggregate.</summary>
        void Contribute(PlaneModifierState state);
    }
}
