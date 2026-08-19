#nullable enable
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Effects
{
    /// <summary>Immutable effect catalog, fully validated by <see cref="EffectCatalogBuilder"/>.Build.</summary>
    public sealed class EffectCatalog
    {
        private readonly Dictionary<string, int> _nameToId;
        private readonly CompiledEffectSpec[] _specs;
        private readonly string[] _buildWarnings;

        internal EffectCatalog(
            Dictionary<string, int> nameToId, CompiledEffectSpec[] specs, List<string> buildWarnings)
        {
            _nameToId = nameToId;
            _specs = specs;
            _buildWarnings = buildWarnings.ToArray();
        }

        /// <summary>Number of effects registered in the catalog.</summary>
        public int Count => _specs.Length;

        /// <summary>Looks up an effect id by name, throwing when absent.</summary>
        /// <param name="name">Effect name to look up.</param>
        /// <returns>The effect id found.</returns>
        /// <exception cref="KeyNotFoundException">The name is not registered.</exception>
        public int GetRequiredId(string name)
        {
            if (TryGetId(name, out var id))
            {
                return id;
            }

            throw new KeyNotFoundException($"Unregistered effect name: {name}");
        }

        /// <summary>Tries to look up an effect id by name.</summary>
        /// <param name="name">Effect name to look up.</param>
        /// <param name="id">The effect id found.</param>
        /// <returns>True if the name is registered.</returns>
        public bool TryGetId(string name, out int id) => _nameToId.TryGetValue(name, out id);

        /// <summary>Gets the compiled spec by id.</summary>
        internal CompiledEffectSpec GetSpec(int id) => _specs[id];

        /// <summary>Chain-cycle warnings found during Build. They accumulate here instead of throwing.</summary>
        public IReadOnlyList<string> BuildWarnings => _buildWarnings;
    }

    /// <summary>Compiled attribute modifier.</summary>
    internal sealed class CompiledModifier
    {
        internal CompiledModifier(
            ushort attributeId, AttributeModifierOp op,
            Operand? @base, Operand? perLevel, IMagnitudeCalc? calc,
            BigNum[]? byLevel, LevelTail tail, BigNum increment, bool scaleWithStack)
        {
            AttributeId = attributeId;
            Op = op;
            Base = @base;
            PerLevel = perLevel;
            Calc = calc;
            ByLevel = byLevel;
            Tail = tail;
            Increment = increment;
            ScaleWithStack = scaleWithStack;
        }

        /// <summary>Id of the attribute to modify.</summary>
        internal ushort AttributeId { get; }

        /// <summary>Modifier operation kind.</summary>
        internal AttributeModifierOp Op { get; }

        /// <summary>Level-independent base magnitude; null when <see cref="Calc"/> or <see cref="ByLevel"/> is set.</summary>
        internal Operand? Base { get; }

        /// <summary>Additional magnitude per level.</summary>
        internal Operand? PerLevel { get; }

        /// <summary>Resolved magnitude-calc contract; null for constant/attribute/level-table magnitudes.</summary>
        internal IMagnitudeCalc? Calc { get; }

        /// <summary>Level table (notations ②③④ compiled to a dense array); null when absent (notation ① or Calc).</summary>
        internal BigNum[]? ByLevel { get; }

        /// <summary>How levels beyond the length of <see cref="ByLevel"/> (MaxLevel) are handled.</summary>
        internal LevelTail Tail { get; }

        /// <summary>Per-level increment when Tail is Extrapolate.</summary>
        internal BigNum Increment { get; }

        /// <summary>Whether the magnitude scales with the stack count.</summary>
        internal bool ScaleWithStack { get; }
    }

    /// <summary>Standalone magnitude resolved with the same notations as CompiledModifier (①–④,
    /// CalcTag) but without an owning attribute. Used for magnitude definitions not tied to a
    /// specific AttributeId, such as ChanceToApply.</summary>
    internal sealed class CompiledMagnitude
    {
        internal CompiledMagnitude(
            Operand? @base, Operand? perLevel, IMagnitudeCalc? calc,
            BigNum[]? byLevel, LevelTail tail, BigNum increment)
        {
            Base = @base;
            PerLevel = perLevel;
            Calc = calc;
            ByLevel = byLevel;
            Tail = tail;
            Increment = increment;
        }

        /// <summary>Level-independent base magnitude; null when <see cref="Calc"/> or <see cref="ByLevel"/> is set.</summary>
        internal Operand? Base { get; }

        /// <summary>Additional magnitude per level.</summary>
        internal Operand? PerLevel { get; }

        /// <summary>Resolved magnitude-calc contract; null for constant/attribute/level-table magnitudes.</summary>
        internal IMagnitudeCalc? Calc { get; }

        /// <summary>Level table (notations ②③④ compiled to a dense array); null when absent (notation ① or Calc).</summary>
        internal BigNum[]? ByLevel { get; }

        /// <summary>How levels beyond the length of <see cref="ByLevel"/> (MaxLevel) are handled.</summary>
        internal LevelTail Tail { get; }

        /// <summary>Per-level increment when Tail is Extrapolate.</summary>
        internal BigNum Increment { get; }
    }

    /// <summary>Compiled effect execution.</summary>
    internal sealed class CompiledExecution
    {
        internal CompiledExecution(IExecutionCalc calc, Operand[] inputs)
        {
            Calc = calc;
            Inputs = inputs;
        }

        /// <summary>Resolved execution contract.</summary>
        internal IExecutionCalc Calc { get; }

        /// <summary>Input operands passed to the execution.</summary>
        internal Operand[] Inputs { get; }
    }

    /// <summary>Compiled condition.</summary>
    internal readonly struct CompiledCondition
    {
        internal CompiledCondition(Operand left, ComparisonOp op, Operand right)
        {
            Left = left;
            Op = op;
            Right = right;
        }

        /// <summary>Left operand.</summary>
        internal Operand Left { get; }

        /// <summary>Comparison operator.</summary>
        internal ComparisonOp Op { get; }

        /// <summary>Right operand.</summary>
        internal Operand Right { get; }
    }

    /// <summary>Compiled chain edge.</summary>
    internal sealed class CompiledChain
    {
        internal CompiledChain(
            ChainTrigger trigger, int effectId, ITargetSelector? selector, BigNum[] selectorParams,
            CompiledCondition[] conditions, ChainLevelRule levelRule, int fixedLevel)
        {
            Trigger = trigger;
            EffectId = effectId;
            Selector = selector;
            SelectorParams = selectorParams;
            Conditions = conditions;
            LevelRule = levelRule;
            FixedLevel = fixedLevel;
        }

        /// <summary>Trigger timing.</summary>
        internal ChainTrigger Trigger { get; }

        /// <summary>Id of the effect to trigger.</summary>
        internal int EffectId { get; }

        /// <summary>Resolved target-selector contract, or null (use the source target as is).</summary>
        internal ITargetSelector? Selector { get; }

        /// <summary>Parameters passed to target selection.</summary>
        internal BigNum[] SelectorParams { get; }

        /// <summary>Trigger conditions.</summary>
        internal CompiledCondition[] Conditions { get; }

        /// <summary>Rule deciding the target effect's level.</summary>
        internal ChainLevelRule LevelRule { get; }

        /// <summary>Level to use when LevelRule is Fixed.</summary>
        internal int FixedLevel { get; }
    }

    /// <summary>Effect spec fully validated and resolved by Build.</summary>
    internal sealed class CompiledEffectSpec
    {
        internal CompiledEffectSpec(
            string name,
            EffectDurationType durationType,
            int durationTicks,
            int periodTicks,
            StackPolicy stack,
            CompiledModifier[] modifiers,
            CompiledExecution[] executions,
            CompiledCondition[] applicationConditions,
            CompiledCondition[] ongoingConditions,
            GameplayTag[] grantedTags,
            GameplayTag[] assetTags,
            GameplayTag[] immunityTags,
            CompiledChain[] chains,
            int overflowEffectId,
            GameplayTag[] removeOnApplyTags,
            CompiledMagnitude? chanceToApply,
            BigNum[]? durationPerLevel,
            CompiledMagnitude? durationScale,
            GameplayTag drCategory,
            int drWindowTicks,
            BigNum[] drStageMultipliers)
        {
            Name = name;
            DurationType = durationType;
            DurationTicks = durationTicks;
            PeriodTicks = periodTicks;
            Stack = stack;
            Modifiers = modifiers;
            Executions = executions;
            ApplicationConditions = applicationConditions;
            OngoingConditions = ongoingConditions;
            GrantedTags = grantedTags;
            AssetTags = assetTags;
            ImmunityTags = immunityTags;
            Chains = chains;
            OverflowEffectId = overflowEffectId;
            RemoveOnApplyTags = removeOnApplyTags;
            ChanceToApply = chanceToApply;
            DurationPerLevel = durationPerLevel;
            DurationScale = durationScale;
            DrCategory = drCategory;
            DrWindowTicks = drWindowTicks;
            DrStageMultipliers = drStageMultipliers;
        }

        /// <summary>Effect name.</summary>
        internal string Name { get; }

        /// <summary>How the effect persists.</summary>
        internal EffectDurationType DurationType { get; }

        /// <summary>Duration in ticks.</summary>
        internal int DurationTicks { get; }

        /// <summary>Periodic execution interval (ticks).</summary>
        internal int PeriodTicks { get; }

        /// <summary>Stack policy.</summary>
        internal StackPolicy Stack { get; }

        /// <summary>Compiled attribute modifiers.</summary>
        internal CompiledModifier[] Modifiers { get; }

        /// <summary>Compiled effect executions.</summary>
        internal CompiledExecution[] Executions { get; }

        /// <summary>Compiled application conditions.</summary>
        internal CompiledCondition[] ApplicationConditions { get; }

        /// <summary>Compiled ongoing conditions.</summary>
        internal CompiledCondition[] OngoingConditions { get; }

        /// <summary>Resolved granted tags.</summary>
        internal GameplayTag[] GrantedTags { get; }

        /// <summary>Resolved asset tags.</summary>
        internal GameplayTag[] AssetTags { get; }

        /// <summary>Resolved immunity tags.</summary>
        internal GameplayTag[] ImmunityTags { get; }

        /// <summary>Compiled chain edges.</summary>
        internal CompiledChain[] Chains { get; }

        /// <summary>Id of the effect applied instead on stack overflow; -1 when none.</summary>
        internal int OverflowEffectId { get; }

        /// <summary>Resolved RemoveOnApply tags. When this effect applies, matching active effects on
        /// the target are removed immediately.</summary>
        internal GameplayTag[] RemoveOnApplyTags { get; }

        /// <summary>Compiled application-chance magnitude; null means it always applies.</summary>
        internal CompiledMagnitude? ChanceToApply { get; }

        /// <summary>Per-level duration ticks (notation ② only); null when absent
        /// (single <see cref="EffectSpec.DurationTicks"/> value is used).</summary>
        internal BigNum[]? DurationPerLevel { get; }

        /// <summary>Duration multiplier evaluated once on application; null when absent (same as multiplier 1).</summary>
        internal CompiledMagnitude? DurationScale { get; }

        /// <summary>DR category tag; <see cref="GameplayTag.None"/> when DR is unused.</summary>
        internal GameplayTag DrCategory { get; }

        /// <summary>Window (ticks) after which the DR application count resets.</summary>
        internal int DrWindowTicks { get; }

        /// <summary>Per-stage DR duration multipliers; empty when <see cref="DrCategory"/> is unused.</summary>
        internal BigNum[] DrStageMultipliers { get; }
    }
}
