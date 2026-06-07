using FishNet.Object.Prediction;

namespace FightOrFlight.Aircraft
{
    /// <summary>
    /// Raw, mode-independent input captured on the controlling client (pilot).
    /// Sent to the server every tick via FishNet prediction. The simulation
    /// decides what each value means based on the active control mode.
    /// </summary>
    public struct PlaneReplicateData : IReplicateData
    {
        public float Horizontal;  // A/D  (-1..1)
        public float Vertical;    // W/S  (-1..1)
        public bool ActionQ;      // ground: brake | flight: throttle down
        public bool ActionE;      // ground: boost | flight: throttle up

        private uint _tick;

        public PlaneReplicateData(float horizontal, float vertical, bool actionQ, bool actionE)
        {
            Horizontal = horizontal;
            Vertical = vertical;
            ActionQ = actionQ;
            ActionE = actionE;
            _tick = 0;
        }

        public void Dispose() { }
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
    }

    /// <summary>
    /// Authoritative state snapshot the server sends so clients can correct (reconcile).
    /// Anything that is simulation state AND changes over time must live here so it is
    /// replayed deterministically during a correction (rigidbody, throttle, mode, grounded).
    /// </summary>
    public struct PlaneReconcileData : IReconcileData
    {
        public PredictionRigidbody Body;
        public float Throttle;
        public byte Mode;
        public bool Grounded;

        private uint _tick;

        public PlaneReconcileData(PredictionRigidbody body, float throttle, byte mode, bool grounded)
        {
            Body = body;
            Throttle = throttle;
            Mode = mode;
            Grounded = grounded;
            _tick = 0;
        }

        // PredictionRigidbody is pooled by FishNet — no manual disposal needed.
        public void Dispose() { }
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
    }
}
