using System;
using System.Text;
using global::Dissonance;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Bun3.Unity.Audio.Dissonance.Netcode
{
    /// <summary>Replicates an owner's SDK voice identity and owns an immutable registration across avatar lifetimes.</summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class DissonanceNfgoPlayer : NetworkBehaviour, IDissonancePlayer
    {
        private readonly NetworkVariable<DissonancePlayerIdentity> _identity = new();
        private DissonanceNfgoSessionScope _session;
        private DissonanceComms _comms;
        private DissonanceComms _explicitComms;
        private DissonancePlayerRegistrationScope _registration;
        private string _playerId = string.Empty;
        private bool _subscribed;
        private double _nextPublication;

        /// <summary>Gets the cached replicated SDK identity, which is separate from OwnerClientId.</summary>
        public string PlayerId => _playerId;
        /// <summary>Gets the avatar's current world position.</summary>
        public Vector3 Position => transform.position;
        /// <summary>Gets the avatar's current world rotation.</summary>
        public Quaternion Rotation => transform.rotation;
        /// <summary>Gets whether the current identity belongs to the local SDK user.</summary>
        public NetworkPlayerType Type => _registration != null ? _registration.Type : NetworkPlayerType.Unknown;
        /// <summary>Whether this avatar currently owns an SDK position registration.</summary>
        public bool IsTracking => _comms != null && _registration != null && _registration.IsTracking;

        /// <summary>Explicitly selects existing comms instead of resolving a registered manager scope.</summary>
        public void Bind(DissonanceComms comms)
        {
            if (comms == null) throw new ArgumentNullException(nameof(comms));
            if (_explicitComms == comms && _comms == comms) return;
            ClearBinding();
            _explicitComms = comms;
            if (IsSpawned) BindComms(comms);
        }

        /// <summary>Starts a fresh network lifetime without reusing a previous owner's replicated identity.</summary>
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer) _identity.Value = default;
            _identity.OnValueChanged += OnIdentityChanged;
            _subscribed = true;
            CacheIdentity();
            _nextPublication = 0;
            ResolveBinding();
        }

        private void Update()
        {
            if (!IsSpawned) return;
            if (_comms == null)
            {
                if (!ReferenceEquals(_comms, null)) ClearBinding();
                ResolveBinding();
            }
            if (_comms == null) return;
            PublishLocalIdentity();
            RefreshTracking();
        }

        private void OnEnable()
        {
            if (!IsSpawned) return;
            _nextPublication = 0;
            ResolveBinding();
            RefreshTracking();
        }
        private void OnDisable() => ClearRegistration();

        private void ResolveBinding()
        {
            if (_comms != null) return;
            if (_explicitComms != null)
            {
                BindComms(_explicitComms);
                return;
            }
            if (!DissonanceNfgoSessionScope.TryResolve(NetworkManager, out var session) || session.Comms == null) return;
            _session = session;
            session.Attach(this);
            BindComms(session.Comms);
        }

        private void BindComms(DissonanceComms comms)
        {
            _comms = comms;
            _comms.LocalPlayerNameChanged += OnLocalNameChanged;
            PublishLocalIdentity();
            RefreshTracking();
        }

        private void OnLocalNameChanged(string name)
        {
            ClearRegistration();
            _nextPublication = 0;
            PublishLocalIdentity();
            RefreshTracking();
        }

        private void PublishLocalIdentity()
        {
            if (!IsSpawned || !isActiveAndEnabled || !IsOwner || _comms == null) return;
            if (Time.unscaledTimeAsDouble < _nextPublication) return;
            string name = _comms.LocalPlayerName;
            if (!IsValidIdentity(name) || string.Equals(name, _playerId, StringComparison.Ordinal)) return;
            _nextPublication = Time.unscaledTimeAsDouble + 1;
            SetIdentityRpc(name);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SetIdentityRpc(string playerName, RpcParams args = default)
        {
            if (!IsServer || !IsSpawned || !isActiveAndEnabled) return;
            if (args.Receive.SenderClientId != OwnerClientId || !IsValidIdentity(playerName)) return;
            if (_session != null && !_session.IsNameAvailable(this, playerName)) return;
            _identity.Value = new DissonancePlayerIdentity(OwnerClientId, playerName);
        }

        internal static bool IsValidIdentity(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > FixedString128Bytes.UTF8MaxLengthInBytes) return false;
            if (string.IsNullOrWhiteSpace(name)) return false;
            for (int i = 0; i < name.Length; i++)
            {
                if (char.IsControl(name[i])) return false;
                if (!char.IsSurrogate(name[i])) continue;
                if (!char.IsHighSurrogate(name[i]) || i + 1 >= name.Length) return false;
                i++;
                if (!char.IsLowSurrogate(name[i])) return false;
            }
            return Encoding.UTF8.GetByteCount(name) <= FixedString128Bytes.UTF8MaxLengthInBytes;
        }

        private void OnIdentityChanged(DissonancePlayerIdentity previous, DissonancePlayerIdentity current)
        {
            ClearRegistration();
            CacheIdentity();
            RefreshTracking();
        }

        private void CacheIdentity()
        {
            var identity = _identity.Value;
            _playerId = identity.IsForOwner(OwnerClientId) ? identity.Name.ToString() : string.Empty;
        }

        private void RefreshTracking()
        {
            if (_registration != null || !IsSpawned || !isActiveAndEnabled || _comms == null) return;
            if (!_identity.Value.IsForOwner(OwnerClientId) || !IsValidIdentity(_playerId)) return;
            if (!DissonancePlayerRegistrationScope.CanRegister(_comms, _playerId)) return;
            var type = string.Equals(_playerId, _comms.LocalPlayerName, StringComparison.Ordinal) ? NetworkPlayerType.Local : NetworkPlayerType.Remote;
            _registration = new DissonancePlayerRegistrationScope(_comms, _playerId, transform, type);
        }

        /// <summary>Republishes the local SDK identity after ownership changes.</summary>
        public override void OnGainedOwnership()
        {
            base.OnGainedOwnership();
            ClearRegistration();
            _nextPublication = 0;
        }
        /// <summary>Releases the prior owner's registration immediately.</summary>
        public override void OnLostOwnership()
        {
            ClearRegistration();
            base.OnLostOwnership();
        }

        /// <summary>Reconciles the atomic owner/name record on every peer, regardless of callback and replication order.</summary>
        protected override void OnOwnershipChanged(ulong previous, ulong current)
        {
            ClearRegistration();
            CacheIdentity();
            _nextPublication = 0;
            RefreshTracking();
            base.OnOwnershipChanged(previous, current);
        }

        private void ClearRegistration()
        {
            var registration = _registration;
            _registration = null;
            registration?.Dispose();
        }
        private void ClearBinding()
        {
            ClearRegistration();
            if (_comms != null) _comms.LocalPlayerNameChanged -= OnLocalNameChanged;
            _comms = null;
            _session?.Detach(this);
            _session = null;
        }
        internal void ReleaseSession(DissonanceNfgoSessionScope scope)
        {
            if (ReferenceEquals(_session, scope)) ClearBinding();
        }

        /// <summary>Releases callbacks and position tracking while allowing the NetworkObject to be pooled.</summary>
        public override void OnNetworkDespawn()
        {
            ClearBinding();
            if (_subscribed) _identity.OnValueChanged -= OnIdentityChanged;
            _subscribed = false;
            _playerId = string.Empty;
            base.OnNetworkDespawn();
        }
        /// <summary>Releases tracking and preserves NGO's own destruction cleanup.</summary>
        public override void OnDestroy()
        {
            try
            {
                ClearBinding();
                if (_subscribed) _identity.OnValueChanged -= OnIdentityChanged;
                _subscribed = false;
            }
            finally { base.OnDestroy(); }
        }
    }
}
