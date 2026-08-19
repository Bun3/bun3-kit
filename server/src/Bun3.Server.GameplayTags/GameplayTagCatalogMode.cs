namespace Bun3.Server.GameplayTags
{
    /// <summary>Execution mode governing how the server locates and validates the GameplayTag catalog artifact.</summary>
    public enum GameplayTagCatalogMode
    {
        /// <summary>Uses the OS-shared development cache or a per-user environment-variable path.</summary>
        LocalDevelopment,

        /// <summary>Uses the path bundled with the deployment and pinned ID, version, and fingerprint.</summary>
        Packaged,
    }
}
