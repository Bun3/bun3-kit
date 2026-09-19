using System;
using global::Dissonance;

namespace Bun3.Unity.Audio.Dissonance
{
    /// <summary>Owns one receiving room membership and an optional transmitting channel, without choosing routing policy.</summary>
    /// <remarks>Use from one main-thread owner. Do not reenter scope methods from SDK room or channel notifications.</remarks>
    public sealed class DissonanceRoomScope : IDisposable
    {
        private readonly Rooms _rooms;
        private readonly RoomChannels _channels;
        private RoomMembership? _membership;
        private RoomChannel? _channel;
        private bool _disposed;

        /// <summary>Creates an empty scope for an existing SDK component. No room is joined or channel opened.</summary>
        public DissonanceRoomScope(DissonanceComms comms)
        {
            if (comms == null) throw new ArgumentNullException(nameof(comms));
            _rooms = comms.Rooms;
            _channels = comms.RoomChannels;
        }

        /// <summary>Gets the exact selected room name, or null when no room is owned.</summary>
        public string CurrentRoom { get; private set; }

        /// <summary>Whether this scope's transmitting channel is open; this does not prove speech or packet delivery.</summary>
        public bool IsTransmitting => _channel.HasValue && _channel.Value.IsOpen;

        /// <summary>
        /// Selects a receiving room and explicitly enables or disables its transmitting channel.
        /// Repeated selection reuses owned resources; positional changes affect only this scope's channel.
        /// Names must contain a non-whitespace character and are not trimmed or normalized.
        /// </summary>
        public void SetRoom(string roomName, bool transmit, bool positional = false)
        {
            EnsureAlive();
            if (string.IsNullOrWhiteSpace(roomName))
                throw new ArgumentException("A nonblank room name is required.", nameof(roomName));

            if (!string.Equals(CurrentRoom, roomName, StringComparison.Ordinal))
            {
                ReleaseOwned();
                _membership = _rooms.Join(roomName);
                CurrentRoom = roomName;
            }

            if (transmit)
            {
                if (!_channel.HasValue || !_channel.Value.IsOpen)
                    _channel = _channels.Open(roomName, positional);
                else
                {
                    var channel = _channel.Value;
                    channel.Positional = positional;
                }
            }
            else
            {
                var channel = _channel;
                _channel = null;
                if (channel.HasValue) channel.Value.Dispose();
            }
        }

        /// <summary>Closes only the owned transmitting channel and releases one owned receiving membership.</summary>
        public void Clear()
        {
            EnsureAlive();
            ReleaseOwned();
        }

        /// <summary>Releases the owned resources once. Other memberships and duplicate channels remain untouched.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ReleaseOwned();
        }

        private void ReleaseOwned()
        {
            var channel = _channel;
            var membership = _membership;
            _channel = null;
            _membership = null;
            CurrentRoom = null;
            try
            {
                if (channel.HasValue) channel.Value.Dispose();
            }
            finally
            {
                if (membership.HasValue) _rooms.Leave(membership.Value);
            }
        }

        private void EnsureAlive()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DissonanceRoomScope));
        }
    }
}
