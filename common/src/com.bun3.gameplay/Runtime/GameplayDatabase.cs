#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay
{
    /// <summary>
    /// Everything compiled once at startup that a <see cref="World"/> runs on: the tag catalog, seam
    /// registry and attribute registry, and the effect, ability and archetype catalogs built against them.
    /// Immutable — one instance can back any number of worlds.
    /// </summary>
    public sealed class GameplayDatabase
    {
        /// <summary>Bundles already-compiled pieces. The catalogs must have been built against these tags,
        /// seams and attributes.</summary>
        /// <param name="tags">Tag catalog.</param>
        /// <param name="seams">Seam registry.</param>
        /// <param name="attributes">Attribute registry.</param>
        /// <param name="effects">Effect catalog.</param>
        /// <param name="abilities">Ability catalog built against <paramref name="effects"/>.</param>
        /// <param name="archetypes">Archetype catalog built against <paramref name="abilities"/>.</param>
        public GameplayDatabase(
            TagCatalog tags, SeamRegistry seams, AttributeRegistry attributes, EffectCatalog effects,
            AbilityCatalog abilities, ArchetypeCatalog archetypes)
        {
            Tags = tags ?? throw new ArgumentNullException(nameof(tags));
            Seams = seams ?? throw new ArgumentNullException(nameof(seams));
            Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
            Effects = effects ?? throw new ArgumentNullException(nameof(effects));
            Abilities = abilities ?? throw new ArgumentNullException(nameof(abilities));
            Archetypes = archetypes ?? throw new ArgumentNullException(nameof(archetypes));
        }

        /// <summary>Tag catalog.</summary>
        public TagCatalog Tags { get; }

        /// <summary>Seam registry the catalogs resolved their calcs and selectors from.</summary>
        public SeamRegistry Seams { get; }

        /// <summary>Attribute registry.</summary>
        public AttributeRegistry Attributes { get; }

        /// <summary>Compiled effects.</summary>
        public EffectCatalog Effects { get; }

        /// <summary>Compiled abilities.</summary>
        public AbilityCatalog Abilities { get; }

        /// <summary>Compiled unit archetypes.</summary>
        public ArchetypeCatalog Archetypes { get; }

        /// <summary>Compiles effect, ability and archetype definitions, in that dependency order.</summary>
        /// <param name="tags">Tag catalog.</param>
        /// <param name="seams">Seam registry.</param>
        /// <param name="attributes">Attribute registry.</param>
        /// <param name="effects">Effect definitions.</param>
        /// <param name="abilities">Ability definitions.</param>
        /// <param name="archetypes">Archetype definitions.</param>
        /// <returns>The compiled database.</returns>
        /// <exception cref="InvalidOperationException">A definition fails validation; the message names it.</exception>
        public static GameplayDatabase Build(
            TagCatalog tags, SeamRegistry seams, AttributeRegistry attributes, IEnumerable<EffectDef> effects,
            IEnumerable<AbilityDef> abilities, IEnumerable<ArchetypeDef> archetypes)
        {
            if (effects is null) throw new ArgumentNullException(nameof(effects));
            if (abilities is null) throw new ArgumentNullException(nameof(abilities));
            if (archetypes is null) throw new ArgumentNullException(nameof(archetypes));

            var effectBuilder = new EffectCatalogBuilder();
            foreach (var def in effects) effectBuilder.Add(def);
            var effectCatalog = effectBuilder.Build(tags, seams, attributes);

            var abilityBuilder = new AbilityCatalogBuilder();
            foreach (var def in abilities) abilityBuilder.Add(def);
            var abilityCatalog = abilityBuilder.Build(tags, seams, attributes, effectCatalog);

            var archetypeBuilder = new ArchetypeCatalogBuilder();
            foreach (var def in archetypes) archetypeBuilder.Add(def);
            var archetypeCatalog = archetypeBuilder.Build(attributes, abilityCatalog);

            return new GameplayDatabase(tags, seams, attributes, effectCatalog, abilityCatalog, archetypeCatalog);
        }
    }
}
