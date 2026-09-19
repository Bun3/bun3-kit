using System;
using System.Collections.Generic;
using global::Dissonance;
using global::Dissonance.Integrations.Unity_NFGO;
using global::Dissonance.Networking;
using Unity.Netcode;

namespace Bun3.Unity.Audio.Dissonance.Netcode
{
    /// <summary>Owns the binding between existing NGO and Dissonance objects and their player trackers.</summary>
    public sealed class DissonanceNfgoSessionScope : IDisposable
    {
        private static readonly Dictionary<NetworkManager, DissonanceNfgoSessionScope> Sessions = new();
        private readonly List<DissonanceNfgoPlayer> _players = new();
        private readonly NetworkManager _manager;
        private readonly NfgoCommsNetwork _transport;
        internal DissonanceComms Comms { get; }
        internal bool IsDisposed { get; private set; }

        /// <summary>Binds existing objects without starting or taking ownership of their networking lifecycle.</summary>
        public DissonanceNfgoSessionScope(NetworkManager manager, DissonanceComms comms, NfgoCommsNetwork transport)
        {
            if (manager == null) throw new ArgumentNullException(nameof(manager));
            if (comms == null) throw new ArgumentNullException(nameof(comms));
            if (transport == null) throw new ArgumentNullException(nameof(transport));
            if (comms.gameObject != transport.gameObject) throw new ArgumentException("The SDK comms and transport must share a GameObject.", nameof(transport));
            if (Sessions.ContainsKey(manager)) throw new InvalidOperationException("The NGO manager already has a Dissonance binding.");
            foreach (var existing in Sessions.Values)
                if (existing.Comms == comms) throw new InvalidOperationException("The SDK comms already belongs to another binding.");
            _manager = manager; Comms = comms; _transport = transport;
            Sessions.Add(manager, this);
        }

        /// <summary>Whether the actual singleton manager and official SDK transport are connected. This does not prove PCM delivery.</summary>
        public bool IsReady => !IsDisposed && _manager != null && Comms != null && _transport != null &&
            ReferenceEquals(NetworkManager.Singleton, _manager) && _manager.IsListening &&
            Comms.isActiveAndEnabled && _transport.isActiveAndEnabled && _transport.IsInitialized &&
            _transport.Status == ConnectionStatus.Connected;

        /// <summary>Looks up a currently tracked owner's SDK identity. This is not a gameplay authority check.</summary>
        public bool TryGetPlayerId(ulong ownerClientId, out string playerId)
        {
            playerId = null;
            if (IsDisposed || Comms == null) return false;
            for (int i = 0; i < _players.Count; i++)
            {
                var player = _players[i];
                if (player != null && player.IsSpawned && player.IsTracking && player.OwnerClientId == ownerClientId)
                { playerId = player.PlayerId; return true; }
            }
            return false;
        }

        internal static bool TryResolve(NetworkManager manager, out DissonanceNfgoSessionScope scope)
        {
            scope = null;
            return manager != null && Sessions.TryGetValue(manager, out scope) && !scope.IsDisposed;
        }

        internal void Attach(DissonanceNfgoPlayer player)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(DissonanceNfgoSessionScope));
            if (!_players.Contains(player)) _players.Add(player);
        }
        internal void Detach(DissonanceNfgoPlayer player) => _players.Remove(player);
        internal bool IsNameAvailable(DissonanceNfgoPlayer requester, string name)
        {
            for (int i = 0; i < _players.Count; i++)
            {
                var player = _players[i];
                if (player != null && player != requester && player.IsSpawned && string.Equals(player.PlayerId, name, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        /// <summary>Immediately detaches owned trackers. The network and SDK objects remain owned by the caller.</summary>
        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            Sessions.Remove(_manager);
            while (_players.Count > 0)
            {
                var player = _players[_players.Count - 1];
                _players.RemoveAt(_players.Count - 1);
                if (player != null) player.ReleaseSession(this);
            }
        }
    }
}
