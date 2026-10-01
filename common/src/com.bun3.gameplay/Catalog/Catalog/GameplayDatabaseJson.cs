#nullable enable
using System;
using System.IO;
using Bun3.Gameplay.Abilities.Catalog;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects.Catalog;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Catalog
{
    /// <summary>Loads the effect, ability and archetype authoring documents and compiles them into a
    /// <see cref="GameplayDatabase"/> in one call.</summary>
    public static class GameplayDatabaseJson
    {
        /// <summary>Reads each UTF-8 JSON stream to its end and compiles the definitions.</summary>
        /// <param name="tags">Tag catalog.</param>
        /// <param name="seams">Seam registry.</param>
        /// <param name="attributes">Attribute registry; also resolves attribute names in the documents.</param>
        /// <param name="effectsJson">Effect document (<c>"effects"</c>).</param>
        /// <param name="abilitiesJson">Ability document (<c>"abilities"</c>).</param>
        /// <param name="archetypesJson">Archetype document (<c>"archetypes"</c>), or null for none.</param>
        /// <returns>The compiled database.</returns>
        /// <exception cref="TagCatalogException">A document's syntax or shape is invalid.</exception>
        /// <exception cref="InvalidOperationException">A definition fails validation; the message names it.</exception>
        public static GameplayDatabase Load(
            TagCatalog tags, SeamRegistry seams, AttributeRegistry attributes, Stream effectsJson, Stream abilitiesJson,
            Stream? archetypesJson = null) =>
            GameplayDatabase.Build(
                tags, seams, attributes,
                EffectDefJson.Load(effectsJson, attributes),
                AbilityDefJson.Load(abilitiesJson, attributes),
                archetypesJson is null ? Array.Empty<ArchetypeDef>() : ArchetypeDefJson.Load(archetypesJson, attributes));
    }
}
