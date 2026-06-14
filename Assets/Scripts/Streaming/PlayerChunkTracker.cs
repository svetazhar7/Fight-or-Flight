using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Finds the positions streaming should center on. Purely local - nothing here
/// is networked or synchronized:
///  - on a client, targets are the NetworkObjects owned by the local player
///    (the plane once seated, the pawn before that);
///  - on the server, every player's objects are tracked too, but only so
///    terrain colliders exist under remote planes - they stream with a small
///    radius and never drive visual quality;
///  - offline / in tests, falls back to an override transform or Camera.main.
/// The NetworkObject scan is cheap (a handful of NOBs in the scene) and runs
/// on a slow interval; positions are read every tick from cached transforms.
/// </summary>
[System.Serializable]
public class PlayerChunkTracker
{
    public struct Target
    {
        public Transform transform;
        public bool isLocal;   // local player: full radius + LOD; remote: collider-only radius
    }

    [Tooltip("Optional explicit streaming target (e.g. for offline test scenes or editor flythroughs). When set, network discovery is skipped.")]
    public Transform overrideTarget;

    [Tooltip("How often (seconds) to re-scan the scene for player objects.")]
    public float rescanInterval = 2f;

    private readonly List<Target> _targets = new();
    private float _rescanTimer;
    private bool _scannedOnce;

    public IReadOnlyList<Target> Targets => _targets;

    public void Refresh(float dt, bool isServer, bool isClient)
    {
        _rescanTimer -= dt;
        if (_scannedOnce && _rescanTimer > 0f)
        {
            Prune();
            return;
        }
        _rescanTimer = Mathf.Max(0.25f, rescanInterval);
        _scannedOnce = true;
        _targets.Clear();

        if (overrideTarget != null)
        {
            _targets.Add(new Target { transform = overrideTarget, isLocal = true });
            return;
        }

        // Scan the few NetworkObjects in the scene. Owned-by-local = full
        // streaming target; other players' objects only matter on the server
        // (collider support) and are ignored on pure clients.
        NetworkObject[] nobs = Object.FindObjectsByType<NetworkObject>();
        foreach (NetworkObject nob in nobs)
        {
            if (nob == null || !nob.Owner.IsValid) continue; // unowned scene objects (world generator etc.)
            bool local = nob.IsOwner;
            if (!local && !isServer) continue;
            _targets.Add(new Target { transform = nob.transform, isLocal = local });
        }

        // Server-only build: there is no "local" player; keep all as remote.
        if (!isClient)
            for (int i = 0; i < _targets.Count; i++)
                _targets[i] = new Target { transform = _targets[i].transform, isLocal = false };

        if (_targets.Count == 0)
        {
            Camera cam = Camera.main;
            if (cam != null)
                _targets.Add(new Target { transform = cam.transform, isLocal = true });
        }
    }

    private void Prune()
    {
        for (int i = _targets.Count - 1; i >= 0; i--)
            if (_targets[i].transform == null)
                _targets.RemoveAt(i);
    }
}
