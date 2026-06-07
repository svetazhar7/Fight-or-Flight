namespace FightOrFlight.Aircraft
{
    /// <summary>
    /// Active control context of the plane. Decides ONLY how raw input is
    /// interpreted and whether ground-wheel forces are applied — aerodynamics
    /// always run, so takeoff/landing are emergent rather than scripted.
    /// </summary>
    public enum PlaneControlMode : byte
    {
        Ground = 0,
        Flight = 1
    }
}
