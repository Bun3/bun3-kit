#nullable enable
using System;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Seams
{
    /// <summary>Contract for computing a magnitude (damage, heal, etc.).</summary>
    public interface IMagnitudeCalc
    {
        /// <summary>Computes the magnitude for the given context.</summary>
        /// <param name="ctx">Context to compute with.</param>
        /// <returns>Computed magnitude.</returns>
        BigNum Calculate(in MagnitudeContext ctx);
    }

    /// <summary>Contract for performing an effect execution.</summary>
    public interface IExecutionCalc
    {
        /// <summary>Executes the effect for the given context.</summary>
        /// <param name="ctx">Context to execute with.</param>
        void Execute(ref ExecutionContext ctx);
    }

    /// <summary>Contract for selecting targets.</summary>
    public interface ITargetSelector
    {
        /// <summary>Selects targets for the given context.</summary>
        /// <param name="ctx">Context to select with.</param>
        /// <param name="results">Span receiving the selected target ids.</param>
        /// <returns>Number of selected targets.</returns>
        int Select(in SelectorContext ctx, System.Span<TargetId> results);
    }

    /// <summary>Context for magnitude calculation. When the source is unresolved, <see cref="SourceAttr"/> is always 0.</summary>
    public readonly ref struct MagnitudeContext
    {
        private readonly EffectTarget _target;
        private readonly EffectTarget? _source;

        internal MagnitudeContext(
            EffectTarget target, EffectTarget? source, bool hasSource, int level, int stack, long worldTick)
        {
            _target = target;
            _source = source;
            HasSource = hasSource;
            Level = level;
            Stack = stack;
            WorldTick = worldTick;
        }

        /// <summary>Whether the source is resolved.</summary>
        public bool HasSource { get; }

        /// <summary>Effect level.</summary>
        public int Level { get; }

        /// <summary>Effect stack count.</summary>
        public int Stack { get; }

        /// <summary>World tick at calculation time.</summary>
        public long WorldTick { get; }

        /// <summary>Gets the Current of a source attribute. Returns 0 when the source is unresolved.</summary>
        /// <param name="attributeId">Attribute id to query.</param>
        public BigNum SourceAttr(ushort attributeId) =>
            HasSource ? _source!.Attributes.GetCurrent(attributeId) : BigNum.Zero;

        /// <summary>Gets the Current of a target attribute.</summary>
        /// <param name="attributeId">Attribute id to query.</param>
        public BigNum TargetAttr(ushort attributeId) => _target.Attributes.GetCurrent(attributeId);

        /// <summary>Returns whether the target has the tag (self or any descendant in the hierarchy).</summary>
        /// <param name="tag">Tag to query.</param>
        public bool TargetHasTag(GameplayTag tag) => _target.Tags.Has(tag);

        /// <summary>Returns whether the source has the tag (self or any descendant in the hierarchy).
        /// Always false when the source is unresolved.</summary>
        /// <param name="tag">Tag to query.</param>
        public bool SourceHasTag(GameplayTag tag) => HasSource && _source!.Tags.Has(tag);
    }

    /// <summary>Context for effect execution. When the source is unresolved, <see cref="SourceAttr"/> is always 0.</summary>
    public ref struct ExecutionContext
    {
        private readonly EffectPipeline _pipeline;
        private readonly EffectTarget _target;
        private readonly EffectTarget? _source;
        private readonly TargetId _sourceId;
        private readonly TargetId _targetId;
        private readonly ReadOnlySpan<BigNum> _inputs;

        internal ExecutionContext(
            EffectPipeline pipeline, EffectTarget target, EffectTarget? source, bool hasSource,
            TargetId sourceId, TargetId targetId, int level, int stack, long worldTick,
            ReadOnlySpan<BigNum> inputs, IRng rng)
        {
            _pipeline = pipeline;
            _target = target;
            _source = source;
            _sourceId = sourceId;
            _targetId = targetId;
            HasSource = hasSource;
            Level = level;
            Stack = stack;
            WorldTick = worldTick;
            _inputs = inputs;
            Rng = rng;
        }

        /// <summary>Whether the source is resolved.</summary>
        public bool HasSource { get; }

        /// <summary>Effect level.</summary>
        public int Level { get; }

        /// <summary>Effect stack count.</summary>
        public int Stack { get; }

        /// <summary>World tick at calculation time.</summary>
        public long WorldTick { get; }

        /// <summary>Random number generator usable in this execution.</summary>
        public IRng Rng { get; }

        /// <summary>Gets the Current of a source attribute. Returns 0 when the source is unresolved.</summary>
        /// <param name="attributeId">Attribute id to query.</param>
        public BigNum SourceAttr(ushort attributeId) =>
            HasSource ? _source!.Attributes.GetCurrent(attributeId) : BigNum.Zero;

        /// <summary>Gets the Current of a target attribute.</summary>
        /// <param name="attributeId">Attribute id to query.</param>
        public BigNum TargetAttr(ushort attributeId) => _target.Attributes.GetCurrent(attributeId);

        /// <summary>Gets a pre-evaluated input operand value.</summary>
        /// <param name="index">Index into the input list.</param>
        public BigNum Input(int index) => _inputs[index];

        /// <summary>Returns whether the target has the tag (self or any descendant in the hierarchy).</summary>
        /// <param name="tag">Tag to query.</param>
        public bool TargetHasTag(GameplayTag tag) => _target.Tags.Has(tag);

        /// <summary>Returns whether the source has the tag (self or any descendant in the hierarchy).
        /// Always false when the source is unresolved.</summary>
        /// <param name="tag">Tag to query.</param>
        public bool SourceHasTag(GameplayTag tag) => HasSource && _source!.Tags.Has(tag);

        /// <summary>Writes a target attribute's Base directly. Always passes through clamp, propagation, and event rules.</summary>
        /// <param name="attributeId">Attribute id to write.</param>
        /// <param name="value">Value to write.</param>
        public void WriteTarget(ushort attributeId, BigNum value) => _target.Attributes.SetBase(attributeId, value);

        /// <summary>Enqueues another effect with the same source, target, and level. Direct re-entrant application is forbidden.</summary>
        /// <param name="specId">Effect spec id to apply.</param>
        public void ApplyToTarget(int specId) => _pipeline.EnqueueApply(specId, _sourceId, _targetId, Level);
    }

    /// <summary>Context for target selection.</summary>
    public readonly ref struct SelectorContext
    {
        private readonly ReadOnlySpan<BigNum> _params;

        internal SelectorContext(TargetId source, ReadOnlySpan<BigNum> parameters, IRng rng)
        {
            Source = source;
            _params = parameters;
            Rng = rng;
        }

        /// <summary>Source that triggered the selection.</summary>
        public TargetId Source { get; }

        /// <summary>Random number generator usable in this selection.</summary>
        public IRng Rng { get; }

        /// <summary>Number of parameters passed to the selection.</summary>
        public int ParamCount => _params.Length;

        /// <summary>Gets a parameter value passed to the selection.</summary>
        /// <param name="index">Index into the parameter list.</param>
        public BigNum Param(int index) => _params[index];
    }
}
