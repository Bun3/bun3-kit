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
        /// <param name="results">Span receiving the selected target ids. At most
        /// <c>results.Length</c> entries may be written.</param>
        /// <returns>Total number of targets selected. A value greater than <c>results.Length</c>
        /// reports truncation — the caller uses only the written entries and counts the event.</returns>
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
        /// <param name="effectId">Effect id to apply.</param>
        public void ApplyToTarget(int effectId) => _pipeline.EnqueueApply(effectId, _sourceId, _targetId, Level);
    }

    /// <summary>Context for target selection. Candidates are the resolver's target ids in ascending
    /// order — that order is the determinism basis, so selectors must not reorder their scan.</summary>
    public readonly ref struct SelectorContext
    {
        private readonly ReadOnlySpan<BigNum> _params;
        private readonly IEffectTargetResolver _resolver;

        // evt is default outside an event path, which leaves every Event* member at its default.
        internal SelectorContext(
            TargetId source, TargetId origin, ReadOnlySpan<BigNum> parameters, IRng rng,
            IEffectTargetResolver resolver, in GameplayEvent evt)
        {
            Source = source;
            Origin = origin;
            _params = parameters;
            Rng = rng;
            _resolver = resolver;
            EventSource = evt.Source;
            EventTarget = evt.Target;
            EventMagnitude = evt.Magnitude;
            EventNominalMagnitude = evt.NominalMagnitude;
        }

        /// <summary>Source that triggered the selection.</summary>
        public TargetId Source { get; }

        /// <summary>Target the selection originates from — the target the firing effect is applied to.</summary>
        public TargetId Origin { get; }

        /// <summary>Source of the gameplay event that triggered this selection. Default outside an event path.</summary>
        public TargetId EventSource { get; }

        /// <summary>Target of the gameplay event that triggered this selection. Default outside an event path.</summary>
        public TargetId EventTarget { get; }

        /// <summary>Clamped magnitude of the gameplay event that triggered this selection. Zero outside an event path.</summary>
        public BigNum EventMagnitude { get; }

        /// <summary>Pre-clamp magnitude of the gameplay event that triggered this selection. Zero outside an event path.</summary>
        public BigNum EventNominalMagnitude { get; }

        /// <summary>Random number generator usable in this selection.</summary>
        public IRng Rng { get; }

        /// <summary>Number of parameters passed to the selection.</summary>
        public int ParamCount => _params.Length;

        /// <summary>Gets a parameter value passed to the selection.</summary>
        /// <param name="index">Index into the parameter list.</param>
        public BigNum Param(int index) => _params[index];

        /// <summary>Number of candidate targets.</summary>
        public int CandidateCount => _resolver.TargetIds.Count;

        /// <summary>Gets a candidate target id. Candidates are in ascending id order.</summary>
        /// <param name="index">Index into the candidate list.</param>
        public TargetId CandidateAt(int index) => _resolver.TargetIds[index];

        /// <summary>Owned tags of the candidate at <paramref name="index"/> — an O(1) read for candidate scans.</summary>
        /// <param name="index">Index into the candidate list.</param>
        /// <returns>Owned-tag container of that candidate.</returns>
        public TagCountContainer CandidateTagsAt(int index) => _resolver.TargetAt(index).Tags;

        /// <summary>Reads the Current value of an attribute of the candidate at <paramref name="index"/> —
        /// an O(1) read for candidate scans.</summary>
        /// <param name="index">Index into the candidate list.</param>
        /// <param name="attributeId">Attribute id to read.</param>
        /// <param name="value">Current value; <see cref="BigNum.Zero"/> when the candidate does not declare it.</param>
        /// <returns>True when the candidate declares that attribute.</returns>
        public bool TryGetCandidateAttributeAt(int index, ushort attributeId, out BigNum value)
        {
            var attributes = _resolver.TargetAt(index).Attributes;
            if (attributes.Has(attributeId))
            {
                value = attributes.GetCurrent(attributeId);
                return true;
            }

            value = BigNum.Zero;
            return false;
        }

        /// <summary>Reads a candidate's owned tags.</summary>
        /// <param name="id">Candidate target id.</param>
        /// <param name="tags">Owned-tag container of that candidate.</param>
        /// <returns>True when the candidate resolves.</returns>
        public bool TryGetTags(TargetId id, out TagCountContainer? tags)
        {
            if (_resolver.TryResolve(id, out var target) && target is not null)
            {
                tags = target.Tags;
                return true;
            }

            tags = null;
            return false;
        }

        /// <summary>Reads a candidate attribute's Current value.</summary>
        /// <param name="id">Candidate target id.</param>
        /// <param name="attributeId">Attribute id to read.</param>
        /// <param name="value">Current value; <see cref="BigNum.Zero"/> when unavailable.</param>
        /// <returns>True when the candidate resolves and declares that attribute.</returns>
        public bool TryGetAttribute(TargetId id, ushort attributeId, out BigNum value)
        {
            if (_resolver.TryResolve(id, out var target) && target is not null
                && target.Attributes.Has(attributeId))
            {
                value = target.Attributes.GetCurrent(attributeId);
                return true;
            }

            value = BigNum.Zero;
            return false;
        }
    }
}
