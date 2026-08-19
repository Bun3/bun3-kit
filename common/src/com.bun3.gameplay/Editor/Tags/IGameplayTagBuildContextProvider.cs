#nullable enable
using System.Collections.Generic;

namespace Bun3.Gameplay.Editor.Tags
{
    /// <summary>Provides the game project's tag catalog authoring and publish inputs to the Unity editor.</summary>
    public interface IGameplayTagBuildContextProvider
    {
        /// <summary>Stable catalog ID of the game product.</summary>
        string CatalogId { get; }

        /// <summary>Absolute paths of external source metadata resolved by the product dependency layer.</summary>
        IReadOnlyList<string> ExternalSourceMetadataPaths { get; }

        /// <summary>Gets the catalog artifact and expected values a Unity publish build pins.</summary>
        /// <returns>Pinned input of the published catalog.</returns>
        GameplayTagPublishedCatalogContext GetPublishedCatalog();
    }
}
