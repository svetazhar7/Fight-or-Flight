using System;
using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;

namespace FightOrFlight.Aircraft
{
    /// <summary>
    /// Base for any damageable, detachable part of the plane (wings, propeller, tail,
    /// cargo door...). Health and attachment are server-authoritative SyncVars; the
    /// OnChange hooks fire on every client so visuals/aero react identically everywhere.
    ///
    /// Subclass and override <see cref="OnHealthChanged"/> / <see cref="OnAttachedChanged"/>
    /// to wire the part to its gameplay effect.
    /// </summary>
    public abstract class PlanePart : NetworkBehaviour, IDamageable
    {
        [SerializeField] protected string _partId = "part";
        [SerializeField] protected float _maxHealth = 100f;

        [Tooltip("Optional networked debris spawned where the part was when it detaches.")]
        [SerializeField] protected GameObject _debrisPrefab;

        private readonly SyncVar<float> _health = new();
        private readonly SyncVar<bool> _attached = new();

        public string PartId => _partId;
        public bool IsAttached => _attached.Value;
        public float Health01 => _maxHealth <= 0f ? 0f : Mathf.Clamp01(_health.Value / _maxHealth);

        /// <summary>Server-side: raised when this part is destroyed/detached.</summary>
        public event Action<PlanePart> OnPartBroken;

        protected virtual void Awake()
        {
            _health.OnChange += HandleHealthChanged;
            _attached.OnChange += HandleAttachedChanged;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _attached.Value = true;
            _health.Value = _maxHealth;
        }

        [Server]
        public void ApplyDamage(in DamageInfo info)
        {
            if (!_attached.Value)
                return;

            _health.Value = Mathf.Max(0f, _health.Value - info.Amount);
            if (_health.Value <= 0f)
                Detach(info.Direction);
        }

        [Server]
        public void Detach(Vector3 impulseDir)
        {
            if (!_attached.Value)
                return;

            _attached.Value = false;
            OnPartDetachedServer(impulseDir);
            OnPartBroken?.Invoke(this);
        }

        /// <summary>Server-only side effects of detaching (spawn debris by default).</summary>
        protected virtual void OnPartDetachedServer(Vector3 impulseDir)
        {
            if (_debrisPrefab == null)
                return;

            GameObject go = Instantiate(_debrisPrefab, transform.position, transform.rotation);
            Spawn(go);
            if (go.TryGetComponent(out Rigidbody rb))
                rb.AddForce(impulseDir.normalized * 3f, ForceMode.VelocityChange);
        }

        private void HandleHealthChanged(float prev, float next, bool asServer) => OnHealthChanged(prev, next, asServer);
        private void HandleAttachedChanged(bool prev, bool next, bool asServer) => OnAttachedChanged(prev, next, asServer);

        /// <summary>Runs on server AND clients whenever health changes.</summary>
        protected virtual void OnHealthChanged(float prev, float next, bool asServer) { }

        /// <summary>Runs on server AND clients whenever attachment changes.</summary>
        protected virtual void OnAttachedChanged(bool prev, bool next, bool asServer) { }
    }
}
