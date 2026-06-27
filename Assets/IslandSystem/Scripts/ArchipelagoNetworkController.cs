using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace IslandSystem
{
    /// <summary>
    /// Makes the archipelago multiplayer-correct with FishNet. The terrain is NEVER sent over the network —
    /// only a world <b>seed</b> (and level). The SERVER picks the seed once and syncs it; every peer (the
    /// server and each client, including late joiners) regenerates the SAME archipelago locally from that
    /// seed. Cheap to network and guaranteed consistent (the generator is fully deterministic per seed).
    ///
    /// Put this on the ArchipelagoGenerator GameObject together with a FishNet <see cref="NetworkObject"/>
    /// (as a scene network object in the gameplay scene, e.g. 01_Game).
    /// </summary>
    [RequireComponent(typeof(ArchipelagoGenerator))]
    public class ArchipelagoNetworkController : NetworkBehaviour
    {
        [Tooltip("0 = the server picks a random world seed each session. Non-zero = always use this fixed seed.")]
        public int fixedSeed = 0;

        private readonly SyncVar<int> _seed = new();
        private readonly SyncVar<int> _level = new();

        private ArchipelagoGenerator _generator;
        private bool _built;

        private void Awake() => _generator = GetComponent<ArchipelagoGenerator>();

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            // Safety net: if the synced seed arrives after we start (late spawn), build then.
            _seed.OnChange += OnSeedChanged;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            // The server owns the world: choose the seed + level once and sync them to everyone.
            _level.Value = _generator.level;
            _seed.Value = fixedSeed != 0 ? fixedSeed : Random.Range(int.MinValue, int.MaxValue);
            Build(); // the server needs its own copy too (physics, spawn points, etc.)
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // FishNet delivers a SyncVar's initial value before OnStartClient, so the seed is ready here.
            Build();
        }

        private void OnSeedChanged(int prev, int next, bool asServer) => Build();

        private void Build()
        {
            if (_built) return;            // host runs both OnStartServer + OnStartClient — generate once
            _built = true;
            _generator.GenerateAtRuntime(_seed.Value, _level.Value);
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _seed.OnChange -= OnSeedChanged;
        }
    }
}
