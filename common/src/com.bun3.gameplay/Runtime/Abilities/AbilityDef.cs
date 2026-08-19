#nullable enable
using System.Collections.Generic;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;

namespace Bun3.Gameplay.Abilities
{
    /// <summary>Resource an activation consumes, deducted immediately when every gate passes.</summary>
    public sealed class CostDef
    {
        /// <summary>Id of the attribute to consume.</summary>
        public ushort AttributeId { get; set; }

        /// <summary>Amount consumed. Evaluated against the activating unit at activation time.</summary>
        public MagnitudeDef Magnitude { get; set; } = new MagnitudeDef();
    }

    /// <summary>One effect an activation applies, with the rule choosing its targets and level.</summary>
    public sealed class AbilityEffectDef
    {
        /// <summary>Name of the effect to apply.</summary>
        public string EffectName { get; set; } = string.Empty;

        /// <summary>Target-selector tag registered in SeamRegistry, or null (use the activating unit).</summary>
        public string? SelectorTag { get; set; }

        /// <summary>Parameters passed to target selection.</summary>
        public List<BigNum> SelectorParams { get; set; } = new List<BigNum>();

        /// <summary>Rule deciding the applied effect's level.</summary>
        public ChainLevelRule LevelRule { get; set; }

        /// <summary>Level to use when <see cref="LevelRule"/> is Fixed.</summary>
        public int FixedLevel { get; set; }
    }

    /// <summary>
    /// Authoring spec for one ability. A property bag filled by a loader, then validated and
    /// compiled by <see cref="AbilityCatalogBuilder"/>. Abilities carry no logic — activation
    /// applies effects, and anything beyond that belongs in an effect execution seam.
    /// </summary>
    public sealed class AbilityDef
    {
        /// <summary>Ability name; must be unique within the catalog.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Maximum level. 0 means undeclared, which forbids level-table cost notations.</summary>
        public int MaxLevel { get; set; }

        /// <summary>Tags classifying the ability itself.</summary>
        public List<string> AssetTags { get; set; } = new List<string>();

        /// <summary>Tags the owner must have for activation to pass the gate.</summary>
        public List<string> RequiredTags { get; set; } = new List<string>();

        /// <summary>Tags that block activation while the owner has them (self or any descendant).</summary>
        public List<string> BlockedTags { get; set; } = new List<string>();

        /// <summary>Activation conditions; all must hold to activate.</summary>
        public List<ConditionDef> ActivationConditions { get; set; } = new List<ConditionDef>();

        /// <summary>Resources consumed on a successful activation.</summary>
        public List<CostDef> Costs { get; set; } = new List<CostDef>();

        /// <summary>Name of the effect applied to the activating unit on success, or null for no cooldown.
        /// Its GrantedTags paired with <see cref="BlockedTags"/> are what make the cooldown a gate.</summary>
        public string? CooldownEffect { get; set; }

        /// <summary>Effects applied on a successful activation.</summary>
        public List<AbilityEffectDef> Effects { get; set; } = new List<AbilityEffectDef>();
    }
}
