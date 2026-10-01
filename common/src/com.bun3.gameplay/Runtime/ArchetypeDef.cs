#nullable enable
using System.Collections.Generic;
using Bun3.Gameplay.Numerics;

namespace Bun3.Gameplay
{
    /// <summary>Initial Base value an archetype gives one attribute it declares.</summary>
    public sealed class ArchetypeAttributeDef
    {
        /// <summary>Registered attribute id.</summary>
        public ushort AttributeId { get; set; }

        /// <summary>Initial Base value, clamped as usual.</summary>
        public BigNum Base { get; set; }
    }

    /// <summary>Ability an archetype grants when a unit is created from it.</summary>
    public sealed class ArchetypeAbilityDef
    {
        /// <summary>Ability name.</summary>
        public string AbilityName { get; set; } = string.Empty;

        /// <summary>Granted level; at least 1, and at most the ability's MaxLevel when that is declared.</summary>
        public int Level { get; set; } = 1;
    }

    /// <summary>
    /// Authoring definition of a unit template: the attributes a unit declares with their initial Base
    /// values, and the abilities it starts with (granted from the archetype origin). Compiled by
    /// <see cref="ArchetypeCatalogBuilder"/>.
    /// </summary>
    public sealed class ArchetypeDef
    {
        /// <summary>Archetype name; must be unique within the catalog.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Declared attributes with their initial Base values.</summary>
        public List<ArchetypeAttributeDef> Attributes { get; set; } = new List<ArchetypeAttributeDef>();

        /// <summary>Abilities granted at creation.</summary>
        public List<ArchetypeAbilityDef> Abilities { get; set; } = new List<ArchetypeAbilityDef>();
    }
}
