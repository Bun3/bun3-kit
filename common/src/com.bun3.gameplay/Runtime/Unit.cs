#nullable enable
using System;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay
{
    /// <summary>
    /// One acting subject in a world — an effect target plus the abilities granted to it and its
    /// owner identity. The effect target is composed rather than inherited so that the replication
    /// schema, dirty tracking, and snapshots all keep the same boundaries, and so that a partial
    /// snapshot cannot be expressed.
    /// </summary>
    public sealed class Unit
    {
        /// <summary>Creates a unit from an id, attribute registry, archetype-declared attributes, and tag catalog.</summary>
        /// <param name="id">Unit id, also its target id.</param>
        /// <param name="registry">Registry holding the attribute definitions.</param>
        /// <param name="attributeIds">Attribute ids this unit declares.</param>
        /// <param name="tagCatalog">Tag catalog used for owned-tag counting.</param>
        /// <param name="ownerId">Owner identity; 0 means unowned.</param>
        public Unit(
            TargetId id, AttributeRegistry registry, ReadOnlySpan<ushort> attributeIds,
            TagCatalog tagCatalog, ulong ownerId = 0)
        {
            Id = id;
            Effects = new EffectTarget(id, registry, attributeIds, tagCatalog);
            Abilities = new AbilitySet();
            OwnerId = ownerId;
        }

        internal Unit(TargetId id, AttributeLayout layout, TagCatalog tagCatalog, ulong ownerId)
        {
            Id = id;
            Effects = new EffectTarget(id, layout, tagCatalog);
            Abilities = new AbilitySet();
            OwnerId = ownerId;
        }

        /// <summary>Unit id.</summary>
        public TargetId Id { get; }

        /// <summary>Attribute set, owned tags, and active effects of this unit.</summary>
        public EffectTarget Effects { get; }

        /// <summary>Abilities granted to this unit.</summary>
        public AbilitySet Abilities { get; }

        /// <summary>Owner identity; 0 means unowned. The simulation assigns no meaning beyond identity.</summary>
        public ulong OwnerId { get; set; }

        /// <summary>Attribute set of this unit.</summary>
        public AttributeSet Attributes => Effects.Attributes;

        /// <summary>Tags this unit owns, with their counts.</summary>
        public TagCountContainer Tags => Effects.Tags;

        /// <summary>Creates a deep-copy snapshot of this unit's deterministic state.</summary>
        /// <returns>Snapshot of effects, ability grants, and owner identity.</returns>
        public UnitSnapshot CreateSnapshot() =>
            new UnitSnapshot(Id, Effects.CreateSnapshot(), Abilities.CreateSnapshot(), OwnerId);

        /// <summary>Restores this unit from a snapshot.</summary>
        /// <param name="snapshot">Snapshot to restore.</param>
        /// <param name="catalog">Effect catalog used to reclaim and regrant tags; must be the catalog used at snapshot time.</param>
        /// <exception cref="ArgumentException">Thrown when the snapshot belongs to another unit.</exception>
        public void RestoreSnapshot(UnitSnapshot snapshot, EffectCatalog catalog)
        {
            if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.UnitId != Id)
                throw new ArgumentException("Cannot restore a snapshot taken from a different unit.", nameof(snapshot));

            Effects.RestoreSnapshot(snapshot.Effects, catalog);
            Abilities.RestoreSnapshot(snapshot.Abilities);
            OwnerId = snapshot.OwnerId;
        }
    }
}
