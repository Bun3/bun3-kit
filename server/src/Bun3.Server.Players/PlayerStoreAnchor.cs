namespace Bun3.Server.Players
{
    internal readonly struct PlayerStoreAnchor
    {
        private PlayerStoreAnchor(bool exists, long? version) { Exists = exists; Version = version; }
        internal bool Exists { get; }
        internal long? Version { get; }
        internal static PlayerStoreAnchor Missing => default;
        internal static PlayerStoreAnchor Versioned(long version) => new PlayerStoreAnchor(true, version);
        internal static PlayerStoreAnchor Unversioned => new PlayerStoreAnchor(true, null);
    }
}
