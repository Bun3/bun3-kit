#nullable enable
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;

namespace Bun3.Gameplay.Effects
{
    /// <summary>
    /// Immutable in-memory deep copy of one target's deterministic state, created by
    /// <see cref="EffectTarget.CreateSnapshot"/>. Stores only attribute Base values (declared order)
    /// and active effect instances (Id ascending, each with the modifier rows it attached) —
    /// Current is not stored (restore reattaches modifiers and runs a full recompute; that
    /// determinism guarantees bit-identical results). Owned tags are not stored either — tags are
    /// only granted via active instances' GrantedTags, so restoring instances regrants them.
    /// Pending application queue, pipeline tick counter, and next issued id are outside this
    /// snapshot's responsibility (managed separately by the caller). Opaque token — all members are
    /// internal, for <see cref="EffectTarget.CreateSnapshot"/>/<see cref="EffectTarget.RestoreSnapshot"/>
    /// only; callers just hold and pass instances without inspecting them.
    /// </summary>
    public sealed class EffectTargetSnapshot
    {
        internal EffectTargetSnapshot(
            TargetId targetId, BigNum[] attributeBases, InstanceRow[] instances, DrHistoryRow[] drHistory)
        {
            TargetId = targetId;
            AttributeBases = attributeBases;
            Instances = instances;
            DrHistory = drHistory;
        }

        /// <summary>Target id this snapshot belongs to. Used to reject restoring onto another target.</summary>
        internal TargetId TargetId { get; }

        /// <summary>Attribute Base values — 1:1 with <see cref="AttributeSet.DeclaredAttributeIdAt"/> declaration order.</summary>
        internal BigNum[] AttributeBases { get; }

        /// <summary>Active effect instance states — Id ascending.</summary>
        internal InstanceRow[] Instances { get; }

        /// <summary>Per-category application history for DR (diminishing returns). This history feeds
        /// duration calculation, so it is included in both snapshot and restore for deterministic replay.</summary>
        internal DrHistoryRow[] DrHistory { get; }

        /// <summary>Fields of one snapshotted instance, plus the modifier rows it had attached.</summary>
        internal sealed class InstanceRow
        {
            internal InstanceRow(
                ulong id, int specId, TargetId source, int level, int stack,
                int remainingTicks, int periodCountdown, bool enabled, long createdTick,
                ModifierRow[] modifiers)
            {
                Id = id;
                SpecId = specId;
                Source = source;
                Level = level;
                Stack = stack;
                RemainingTicks = remainingTicks;
                PeriodCountdown = periodCountdown;
                Enabled = enabled;
                CreatedTick = createdTick;
                Modifiers = modifiers;
            }

            internal ulong Id { get; }
            internal int SpecId { get; }
            internal TargetId Source { get; }
            internal int Level { get; }
            internal int Stack { get; }
            internal int RemainingTicks { get; }
            internal int PeriodCountdown { get; }
            internal bool Enabled { get; }
            internal long CreatedTick { get; }
            internal ModifierRow[] Modifiers { get; }
        }

        /// <summary>One modifier row an instance had attached to an attribute slot. Magnitude is stored
        /// as evaluated at application time (not re-evaluated on restore).</summary>
        internal readonly struct ModifierRow
        {
            internal ModifierRow(
                ushort attributeId, int rowIndex, AttributeModifierOp op, BigNum magnitude, bool scaleWithStack)
            {
                AttributeId = attributeId;
                RowIndex = rowIndex;
                Op = op;
                Magnitude = magnitude;
                ScaleWithStack = scaleWithStack;
            }

            internal ushort AttributeId { get; }
            internal int RowIndex { get; }
            internal AttributeModifierOp Op { get; }
            internal BigNum Magnitude { get; }
            internal bool ScaleWithStack { get; }
        }

        /// <summary>Snapshotted application-history row for one DR category tag.</summary>
        internal readonly struct DrHistoryRow
        {
            internal DrHistoryRow(ushort categoryTagIndex, int appliedCount, long lastAppliedTick)
            {
                CategoryTagIndex = categoryTagIndex;
                AppliedCount = appliedCount;
                LastAppliedTick = lastAppliedTick;
            }

            internal ushort CategoryTagIndex { get; }
            internal int AppliedCount { get; }
            internal long LastAppliedTick { get; }
        }
    }
}
