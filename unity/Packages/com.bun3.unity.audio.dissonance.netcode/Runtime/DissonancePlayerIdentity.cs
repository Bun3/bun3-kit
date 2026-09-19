using System;
using Unity.Collections;
using Unity.Netcode;

namespace Bun3.Unity.Audio.Dissonance.Netcode
{
    internal struct DissonancePlayerIdentity : INetworkSerializable, IEquatable<DissonancePlayerIdentity>
    {
        private ulong _ownerClientId;
        private FixedString128Bytes _name;
        internal FixedString128Bytes Name => _name;

        internal DissonancePlayerIdentity(ulong ownerClientId, string name)
        {
            if (!DissonanceNfgoPlayer.IsValidIdentity(name)) throw new ArgumentException("The voice identity is invalid.", nameof(name));
            _ownerClientId = ownerClientId;
            _name = new FixedString128Bytes(name);
        }

        internal bool IsForOwner(ulong ownerClientId) => !_name.IsEmpty && _ownerClientId == ownerClientId;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref _ownerClientId);
            serializer.SerializeValue(ref _name);
        }

        public bool Equals(DissonancePlayerIdentity other) => _ownerClientId == other._ownerClientId && _name.Equals(other._name);
    }
}
