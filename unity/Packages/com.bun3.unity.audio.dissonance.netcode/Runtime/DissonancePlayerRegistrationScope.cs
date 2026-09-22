using System;
using System.Collections.Generic;
using global::Dissonance;
using UnityEngine;

namespace Bun3.Unity.Audio.Dissonance.Netcode
{
    internal sealed class DissonancePlayerRegistrationScope : IDissonancePlayer, IDisposable
    {
        private static readonly Dictionary<(DissonanceComms, string), DissonancePlayerRegistrationScope> Registrations = new();
        private readonly DissonanceComms _comms;
        private readonly Transform _transform;
        public string PlayerId { get; }
        public Vector3 Position => _transform != null ? _transform.position : Vector3.zero;
        public Quaternion Rotation => _transform != null ? _transform.rotation : Quaternion.identity;
        public NetworkPlayerType Type { get; }
        public bool IsTracking { get; private set; }

        internal DissonancePlayerRegistrationScope(DissonanceComms comms, string playerId, Transform transform, NetworkPlayerType type)
        {
            if (comms == null) throw new ArgumentNullException(nameof(comms));
            if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentException("A nonblank player identity is required.", nameof(playerId));
            if (transform == null) throw new ArgumentNullException(nameof(transform));
            if (!CanRegister(comms, playerId))
                throw new InvalidOperationException("The voice identity already has a tracker.");
            _comms = comms; PlayerId = playerId; _transform = transform; Type = type;
            Registrations.Add((comms, playerId), this);
            try { comms.TrackPlayerPosition(this); IsTracking = true; }
            catch { Registrations.Remove((comms, playerId)); throw; }
        }

        internal static bool CanRegister(DissonanceComms comms, string playerId) =>
            comms != null && !string.IsNullOrWhiteSpace(playerId) &&
            !Registrations.ContainsKey((comms, playerId)) && comms.FindPlayer(playerId)?.Tracker == null;

        public void Dispose()
        {
            if (!IsTracking) return;
            IsTracking = false;
            Registrations.Remove((_comms, PlayerId));
            if (_comms == null) return;
            var player = _comms.FindPlayer(PlayerId);
            if (player == null || ReferenceEquals(player.Tracker, this)) _comms.StopTracking(this);
        }
    }
}
