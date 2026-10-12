#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Tags;
using Newtonsoft.Json.Linq;
using static Bun3.Gameplay.Effects.Catalog.JsonFields;

namespace Bun3.Gameplay.Catalog
{
    /// <summary>
    /// Authoring JSON loader for unit archetypes. Strictly validates syntax and shape; ability references
    /// and per-archetype rules are checked by <see cref="ArchetypeCatalogBuilder.Build"/>. Attributes are a
    /// name-to-BigNum-string object (<c>{ "MaxHp": "100" }</c>) resolved through the attribute registry.
    /// </summary>
    public static class ArchetypeDefJson
    {
        /// <summary>Reads the UTF-8 JSON stream from its current position to the end into archetype definitions.</summary>
        /// <param name="utf8Json">Readable UTF-8 JSON stream.</param>
        /// <param name="attributes">Attribute registry resolving attribute names to ids.</param>
        /// <returns>Archetype definitions in the order they appear in the JSON.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="utf8Json"/> or <paramref name="attributes"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">The stream is not readable.</exception>
        /// <exception cref="TagCatalogException">The JSON syntax or archetype shape is invalid.</exception>
        public static List<ArchetypeDef> Load(Stream utf8Json, AttributeRegistry attributes) =>
            LoadDocument(
                utf8Json, (attributes ?? throw new ArgumentNullException(nameof(attributes))).NameToId, "archetypes",
                ReadArchetype);

        private static ArchetypeDef ReadArchetype(JObject archetype, IReadOnlyDictionary<string, ushort> attributeNames)
        {
            RequireAllowedProperties(archetype, "name", "attributes", "abilities");
            var def = new ArchetypeDef { Name = RequireString(archetype, "name") };

            if (archetype.Property("attributes", StringComparison.Ordinal) != null)
            {
                foreach (var property in RequireObject(archetype, "attributes").Properties())
                {
                    if (!attributeNames.TryGetValue(property.Name, out var attributeId))
                    {
                        throw Error($"Unknown attribute name: {property.Name}", property);
                    }

                    def.Attributes.Add(new ArchetypeAttributeDef
                    {
                        AttributeId = attributeId, Base = RequireBigNumToken(property.Value),
                    });
                }
            }

            if (archetype.Property("abilities", StringComparison.Ordinal) != null)
            {
                foreach (var item in RequireArray(archetype, "abilities"))
                {
                    var entry = AsObject(item, "abilities items must be objects.");
                    RequireAllowedProperties(entry, "ability", "level");
                    def.Abilities.Add(new ArchetypeAbilityDef
                    {
                        AbilityName = RequireString(entry, "ability"), Level = ReadOptionalInt(entry, "level", 1),
                    });
                }
            }

            return def;
        }
    }
}
