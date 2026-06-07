namespace FightOrFlight.Aircraft
{
    /// <summary>
    /// Semantic control commands for a single tick, produced by interpreting the
    /// raw input for the current <see cref="PlaneControlMode"/>. Subsystems consume
    /// this — they never read raw input or the keyboard directly.
    /// </summary>
    public struct PlaneControlState
    {
        /// <summary>-1..1, how much to change engine throttle this tick.</summary>
        public float ThrottleDelta;

        /// <summary>-1..1 nose up/down.</summary>
        public float Pitch;

        /// <summary>-1..1 roll.</summary>
        public float Roll;

        /// <summary>-1..1 yaw.</summary>
        public float Yaw;

        /// <summary>-1..1 nose-wheel steering (ground only).</summary>
        public float Steer;

        /// <summary>0..1 wheel brake (ground only).</summary>
        public float Brake;

        /// <summary>True while thrust boost/afterburner is held (ground only).</summary>
        public bool Boost;
    }
}
