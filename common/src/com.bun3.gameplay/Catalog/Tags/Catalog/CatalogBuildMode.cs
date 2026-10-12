#nullable enable

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>Distinguishes whether a game catalog is built as a development cache or a published artifact.</summary>
    public enum CatalogBuildMode
    {
        /// <summary>Local development build using the fixed version 0.0.0-dev.</summary>
        Development,

        /// <summary>Publish build using an explicit release version.</summary>
        Published,
    }
}
