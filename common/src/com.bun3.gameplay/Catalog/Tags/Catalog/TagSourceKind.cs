#nullable enable

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>
    /// Distinguishes where and in what form a tag source document is provided.
    /// </summary>
    public enum TagSourceKind
    {
        /// <summary>JSON source owned and edited by the game project.</summary>
        GameJson,

        /// <summary>Read-only JSON source provided by a package.</summary>
        PackageJson,

        /// <summary>Read-only source provided by native code.</summary>
        Native,
    }
}
