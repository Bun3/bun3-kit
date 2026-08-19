#nullable enable
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;

namespace Bun3.Gameplay.Effects
{
    /// <summary>How an effect persists.</summary>
    public enum EffectDurationType : byte
    {
        /// <summary>Applies immediately and vanishes right away.</summary>
        Instant = 0,
        /// <summary>Lasts a fixed number of ticks.</summary>
        Duration = 1,
        /// <summary>Lasts indefinitely until explicitly removed.</summary>
        Infinite = 2,
    }

    /// <summary>Behavior when a stackable effect is reapplied.</summary>
    public enum StackReapply : byte
    {
        /// <summary>Only refreshes the duration.</summary>
        Refresh = 0,
        /// <summary>Adds one stack.</summary>
        AddStack = 1,
        /// <summary>Keeps the stack and adds the new duration to the remaining duration, truncated at a
        /// cap (new duration × <see cref="StackPolicy.ExtendCapMultiplier"/>).</summary>
        ExtendCapped = 2,
    }

    /// <summary>Behavior when a stack expires.</summary>
    public enum StackExpiration : byte
    {
        /// <summary>Removes all stacks at once.</summary>
        ClearAll = 0,
        /// <summary>Removes a single stack and refreshes the duration.</summary>
        RemoveOneAndRefresh = 1,
    }

    /// <summary>Behavior when a reapplication exceeds the maximum stack count.</summary>
    public enum StackOverflow : byte
    {
        /// <summary>Rejects the reapplication.</summary>
        Deny = 0,
        /// <summary>Applies a designated other effect instead.</summary>
        ApplyEffect = 1,
    }

    /// <summary>When a chained effect fires.</summary>
    public enum ChainTrigger : byte
    {
        /// <summary>When the source effect is applied.</summary>
        OnApplication = 0,
        /// <summary>When the source effect ends normally.</summary>
        OnCompleteNormal = 1,
        /// <summary>When the source effect ends prematurely (abnormally).</summary>
        OnCompletePrematurely = 2,
        /// <summary>When the source effect hits stack overflow.</summary>
        OnStackOverflow = 3,
    }

    /// <summary>Rule deciding the level of a chain-triggered effect.</summary>
    public enum ChainLevelRule : byte
    {
        /// <summary>Inherits the source effect's level.</summary>
        Inherit = 0,
        /// <summary>Uses a fixed level.</summary>
        Fixed = 1,
    }

    /// <summary>How a level table handles levels beyond MaxLevel.</summary>
    public enum LevelTail : byte
    {
        /// <summary>Keeps the last value at MaxLevel.</summary>
        Clamp = 0,
        /// <summary>Extrapolates linearly from the last value with a per-level increment.</summary>
        Extrapolate = 1,
    }

    /// <summary>One key of a sparse level curve (level, value).</summary>
    public sealed class LevelKey
    {
        /// <summary>Level.</summary>
        public int Level { get; set; }

        /// <summary>Value at that level.</summary>
        public BigNum Value { get; set; }
    }

    /// <summary>
    /// Magnitude definition. Level-scaling notations are mutually exclusive — exactly one of five:
    /// ① <see cref="Base"/> (+<see cref="PerLevel"/>) linear, ② <see cref="PerLevelValues"/> explicit array,
    /// ③ <see cref="Formula"/> deterministic formula, ④ <see cref="CurveKeys"/> sparse keys + linear
    /// interpolation, or a <see cref="CalcTag"/> registered in SeamRegistry.
    /// </summary>
    public sealed class MagnitudeDef
    {
        /// <summary>Level-independent base magnitude (notation ①). Exclusive with the other notations.</summary>
        public Operand? Base { get; set; }

        /// <summary>Additional magnitude per level (notation ①). Only usable when <see cref="Base"/> is set.</summary>
        public Operand? PerLevel { get; set; }

        /// <summary>Explicit per-level values (notation ②). The length must equal <see cref="EffectSpec.MaxLevel"/>.</summary>
        public List<BigNum>? PerLevelValues { get; set; }

        /// <summary>Deterministic formula (notation ③). Follows <see cref="Numerics.BigNumFormula"/> syntax
        /// and is evaluated with the level bound to variable x.</summary>
        public string? Formula { get; set; }

        /// <summary>Sparse level keys (notation ④). Ascending levels, no duplicates, first key must be
        /// level 1. Linear interpolation between keys; past the last key the <see cref="Tail"/> policy applies.</summary>
        public List<LevelKey>? CurveKeys { get; set; }

        /// <summary>Magnitude-calc tag registered in SeamRegistry. Exclusive with the other notations.</summary>
        public string? CalcTag { get; set; }

        /// <summary>How the level table (②③④) handles levels beyond MaxLevel.</summary>
        public LevelTail Tail { get; set; }

        /// <summary>Per-level increment when <see cref="Tail"/> is Extrapolate. 0 means it is derived
        /// automatically from the difference of the array's last two values.</summary>
        public BigNum ExtrapolateIncrement { get; set; }
    }

    /// <summary>Attribute modifier definition.</summary>
    public sealed class ModifierDef
    {
        /// <summary>Id of the attribute to modify.</summary>
        public ushort AttributeId { get; set; }

        /// <summary>Modifier operation kind.</summary>
        public AttributeModifierOp Op { get; set; }

        /// <summary>Magnitude definition of the modification.</summary>
        public MagnitudeDef Magnitude { get; set; } = new MagnitudeDef();

        /// <summary>Whether the magnitude scales with the stack count. Defaults to true.</summary>
        public bool ScaleWithStack { get; set; } = true;
    }

    /// <summary>Effect execution (side effect) definition.</summary>
    public sealed class ExecutionDef
    {
        /// <summary>Execution tag registered in SeamRegistry.</summary>
        public string CalcTag { get; set; } = string.Empty;

        /// <summary>Input operands passed to the execution.</summary>
        public List<Operand> Inputs { get; set; } = new List<Operand>();
    }

    /// <summary>Condition definition — compares a left and a right operand with a comparison operator.</summary>
    public sealed class ConditionDef
    {
        /// <summary>Left operand.</summary>
        public Operand Left { get; set; }

        /// <summary>Comparison operator.</summary>
        public ComparisonOp Op { get; set; }

        /// <summary>Right operand.</summary>
        public Operand Right { get; set; }
    }

    /// <summary>Chain edge definition — a trigger leading from a source effect to another effect.</summary>
    public sealed class ChainEdgeDef
    {
        /// <summary>Trigger timing.</summary>
        public ChainTrigger Trigger { get; set; }

        /// <summary>Name of the effect to trigger.</summary>
        public string EffectName { get; set; } = string.Empty;

        /// <summary>Target-selector tag registered in SeamRegistry, or null (use the source target as is).</summary>
        public string? SelectorTag { get; set; }

        /// <summary>Parameters passed to target selection.</summary>
        public List<BigNum> SelectorParams { get; set; } = new List<BigNum>();

        /// <summary>Trigger conditions; all must hold to fire.</summary>
        public List<ConditionDef> Conditions { get; set; } = new List<ConditionDef>();

        /// <summary>Rule deciding the target effect's level.</summary>
        public ChainLevelRule LevelRule { get; set; }

        /// <summary>Level to use when <see cref="LevelRule"/> is Fixed.</summary>
        public int FixedLevel { get; set; }
    }

    /// <summary>Stack policy.</summary>
    public sealed class StackPolicy
    {
        /// <summary>Maximum stack count; 0 means the effect does not stack.</summary>
        public int MaxStack { get; set; }

        /// <summary>Behavior when a stackable effect is reapplied.</summary>
        public StackReapply OnReapply { get; set; }

        /// <summary>Stacks added per reapplication. Defaults to 1.</summary>
        public int AddStackCount { get; set; } = 1;

        /// <summary>Whether reapplication refreshes the duration. Defaults to true.</summary>
        public bool RefreshDurationOnReapply { get; set; } = true;

        /// <summary>Whether reapplication resets the periodic execution timer.</summary>
        public bool ResetPeriodOnReapply { get; set; }

        /// <summary>Behavior when a stack expires.</summary>
        public StackExpiration OnExpiration { get; set; }

        /// <summary>Behavior when the maximum stack count is exceeded.</summary>
        public StackOverflow OnOverflow { get; set; }

        /// <summary>Name of the effect to apply instead when <see cref="OnOverflow"/> is ApplyEffect.</summary>
        public string? OverflowEffectName { get; set; }

        /// <summary>Whether an overflow application clears all existing stacks.</summary>
        public bool ClearStacksOnOverflow { get; set; }

        /// <summary>If true, merging (reapplication) syncs the instance Level to the Stack value.
        /// Combine with a level table to express non-linear per-stack magnitudes. Rejected in Build
        /// when MaxStack is 0.</summary>
        public bool LevelFromStack { get; set; }

        /// <summary>Multiplier that caps the duration for <see cref="StackReapply.ExtendCapped"/>.
        /// Cap = DurationTicks × this value. Defaults to 1.3. Values below 1 are rejected in Build.</summary>
        public BigNum ExtendCapMultiplier { get; set; } = BigNum.FromParts(13, -1);
    }

    /// <summary>
    /// Authoring spec for one effect. A property bag filled by a loader, then validated and
    /// compiled by <see cref="Effects.EffectCatalogBuilder"/>.
    /// </summary>
    public sealed class EffectSpec
    {
        /// <summary>Effect name; must be unique within the catalog.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Maximum level, required when a level-table notation (②③④) is used. 0 means undeclared.</summary>
        public int MaxLevel { get; set; }

        /// <summary>How the effect persists.</summary>
        public EffectDurationType DurationType { get; set; }

        /// <summary>Duration in ticks. Must be 0 for Instant/Infinite.</summary>
        public int DurationTicks { get; set; }

        /// <summary>Periodic execution interval (ticks); 0 means no periodic execution.</summary>
        public int PeriodTicks { get; set; }

        /// <summary>Stack policy.</summary>
        public StackPolicy Stack { get; set; } = new StackPolicy();

        /// <summary>Attribute modifiers.</summary>
        public List<ModifierDef> Modifiers { get; set; } = new List<ModifierDef>();

        /// <summary>Effect executions (side effects).</summary>
        public List<ExecutionDef> Executions { get; set; } = new List<ExecutionDef>();

        /// <summary>Application conditions; all must hold to apply.</summary>
        public List<ConditionDef> ApplicationConditions { get; set; } = new List<ConditionDef>();

        /// <summary>Ongoing conditions; if any breaks, the effect is not removed but toggled disabled
        /// (enabled=false), turning off its modifiers and granted tags. It re-enables once the
        /// conditions hold again.</summary>
        public List<ConditionDef> OngoingConditions { get; set; } = new List<ConditionDef>();

        /// <summary>Tags granted to the target while the effect is applied.</summary>
        public List<string> GrantedTags { get; set; } = new List<string>();

        /// <summary>Asset tags classifying the effect itself.</summary>
        public List<string> AssetTags { get; set; } = new List<string>();

        /// <summary>While this effect is active, blocks application of other effects whose AssetTags
        /// are descendants-or-self of these tags.</summary>
        public List<string> ImmunityTags { get; set; } = new List<string>();

        /// <summary>Chain edges leading to other effects.</summary>
        public List<ChainEdgeDef> Chains { get; set; } = new List<ChainEdgeDef>();

        /// <summary>When this effect applies successfully, immediately removes (right before
        /// application) the target's active effects whose AssetTags are descendants-or-self of these
        /// tags (e.g. a higher tier replacing a lower one). The merge target (same spec) is excluded.</summary>
        public List<string> RemoveOnApplyTags { get; set; } = new List<string>();

        /// <summary>Magnitude definition of the application chance. Null means it always applies. The
        /// evaluated value is interpreted in [0,1] (≤0 fails, ≥1 passes) and rolled deterministically
        /// with the World-seeded Rng.</summary>
        public MagnitudeDef? ChanceToApply { get; set; }

        /// <summary>Per-level duration ticks (notation ② only). The length must equal
        /// <see cref="MaxLevel"/>, and it is mutually exclusive with <see cref="DurationTicks"/>
        /// (if one is set the other must be 0). Only usable when <see cref="DurationType"/> is Duration.</summary>
        public List<BigNum>? DurationPerLevel { get; set; }

        /// <summary>Duration multiplier evaluated once on application (new instance creation). Final
        /// duration = (<see cref="DurationPerLevel"/>?[level-1] ?? <see cref="DurationTicks"/>) × this
        /// value, truncated to integer ticks, then clamped to a minimum of 1 tick (when DR does not
        /// zero it). Only usable when <see cref="DurationType"/> is Duration.</summary>
        public MagnitudeDef? DurationScale { get; set; }

        /// <summary>Category tag path identifying the diminishing-returns (DR) series. Null means this
        /// effect does not use DR. Only usable when <see cref="DurationType"/> is Duration.</summary>
        public string? DrCategory { get; set; }

        /// <summary>Window (ticks) after which the DR application count resets. Must be at least 1 when
        /// <see cref="DrCategory"/> is set — the count resets to 0 once this many ticks pass after the
        /// target's last application in this category.</summary>
        public int DrWindowTicks { get; set; }

        /// <summary>Per-stage duration multipliers for DR (e.g. ["0.5","0.25","0"]). The first
        /// application always uses multiplier 1; the nth (n≥1) application uses index min(n-1,
        /// length-1). A final value of 0 means immunity. Must be non-empty with all values ≥0 when
        /// <see cref="DrCategory"/> is set.</summary>
        public List<BigNum> DrStageMultipliers { get; set; } = new List<BigNum>();
    }
}
