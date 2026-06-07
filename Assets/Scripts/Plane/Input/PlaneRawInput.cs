namespace FightOrFlight.Aircraft
{
    /// <summary>
    /// Snapshot of the pilot's raw input for one tick. Mode-independent: the
    /// controller maps these axes onto throttle/pitch/roll/steer/etc.
    /// </summary>
    public struct PlaneRawInput
    {
        public float Horizontal;  // A/D
        public float Vertical;    // W/S
        public bool ActionQ;      // Q
        public bool ActionE;      // E
    }
}
