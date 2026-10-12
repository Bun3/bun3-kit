#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Numerics;

namespace Bun3.Gameplay.Effects
{
    /// <summary>
    /// Pipeline that drains the effect application queue and handles the Instant/Duration/Infinite
    /// paths, chains, and Ongoing toggles. One tick runs six phases —
    /// (1) <see cref="DrainApplications"/> (budget/immunity/application conditions/apply; fires
    /// OnApplication/OnStackOverflow chains) → (2) <see cref="AdvanceTime"/> (period firing, lifetime
    /// decrement, expiry; fires OnCompleteNormal chains) → (3) rebuild of slots marked dirty by 1–2 →
    /// (4) Ongoing-condition toggles → (5) rebuild of slots marked dirty by 4 — phases 3–5 run per target
    /// in one pass (<see cref="SettleAttributes"/>), since they touch only that target → (6) event finalization
    /// (no pipeline work — observation buffers are read after the tick and cleared when the next one
    /// starts). Composed of this file (state, queueing, phase order) plus <c>EffectPipeline.Apply.cs</c> (phase 1),
    /// <c>EffectPipeline.Time.cs</c> (phase 2, catch-up, ownership), <c>EffectPipeline.Settle.cs</c> (phases 3–5),
    /// <c>EffectPipeline.Evaluate.cs</c> (conditions and magnitudes), <c>EffectPipeline.Removal.cs</c> (removal paths),
    /// <c>EffectPipeline.Chains.cs</c> (chains, selection, OnEvent routing) and <c>EffectPipeline.Events.cs</c>
    /// (observation buffers).
    /// </summary>
    public sealed partial class EffectPipeline
    {
        private struct PendingApply
        {
            internal int EffectId;
            internal TargetId Source;
            internal TargetId Target;
            internal int Level;
            internal BigNum EventMagnitude;
        }

        private readonly EffectCatalog _catalog;

        private readonly IEffectTargetResolver _resolver;

        private readonly IRng _rng;

        // World rescales this per registered unit; a standalone pipeline keeps its constructor value.
        internal int ApplyBudgetPerTick { get; set; }

        private readonly Queue<PendingApply> _queue = new Queue<PendingApply>();

        private readonly List<EffectInstance> _expiryScratch = new List<EffectInstance>();

        private readonly Stack<EffectInstance> _instancePool = new Stack<EffectInstance>();

        private readonly List<EffectInstance> _removalScratch = new List<EffectInstance>();

        /// <summary>Number of apply requests silently dropped because the target failed to resolve.</summary>
        internal long UnresolvedTargetDropCount { get; private set; }

        /// <summary>Number of apply requests blocked by immunity tags.</summary>
        internal long ImmuneDropCount { get; private set; }

        /// <summary>Number of apply requests blocked by unmet application conditions.</summary>
        internal long ConditionDropCount { get; private set; }

        /// <summary>Number of apply requests silently dropped by a failed ChanceToApply roll.</summary>
        internal long ChanceDropCount { get; private set; }

        /// <summary>Number of apply requests silently dropped by DR immunity (stage-multiplier duration of 0 ticks or less).</summary>
        internal long DrImmuneDropCount { get; private set; }

        /// <summary>Number of selector calls whose result exceeded the fixed selection buffer.</summary>
        internal long SelectorTruncationCount { get; private set; }

        /// <summary>Called when an instance starts granting (creation, or re-enable) and stops granting
        /// (removal, or disable). Set by the owning world; null in pipeline-only tests.</summary>
        internal Action<EffectTarget, EffectInstance, bool>? AbilityGrantHook { get; set; }

        // Deterministic [0,1) roll scale: 2^32, the range of _rng.NextUInt32(), so probability
        // comparison stays integer-only BigNum arithmetic (no floating point; 0/1 boundaries fall out naturally).
        private static readonly BigNum RngRollRange = (BigNum)4_294_967_296L;

        /// <summary>Number of ticks processed so far. The internal setter is for snapshot-restore replay determinism only.</summary>
        public long CurrentTick { get; internal set; }

        /// <summary>Next instance id to issue. For snapshot-restore replay determinism, save the value at
        /// snapshot time and restore it as-is (only safe when the pending queue is empty — queued entries
        /// would also need their issued ids reproduced, which is out of scope).</summary>
        internal ulong NextInstanceId { get; set; } = 1;

        /// <summary>Number of apply requests left in the queue this tick because the budget was exceeded.</summary>
        public int PendingApplyCount => _queue.Count;

        // A restored snapshot never has pending work, so whatever is queued or observed belongs to the abandoned timeline.
        internal void DiscardPendingWork()
        {
            _queue.Clear();
            ClearObservationBuffers();
        }

        /// <summary>Creates a pipeline from an effect catalog, target resolver, RNG, and per-tick apply budget.</summary>
        /// <param name="catalog">Effect catalog for effect lookup.</param>
        /// <param name="resolver">Resolver mapping TargetId to EffectTarget.</param>
        /// <param name="rng">RNG passed to execution calcs and target selection.</param>
        /// <param name="applyBudgetPerTick">Maximum apply requests drained per tick.</param>
        public EffectPipeline(EffectCatalog catalog, IEffectTargetResolver resolver, IRng rng, int applyBudgetPerTick = 64)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            if (applyBudgetPerTick <= 0)
                throw new ArgumentOutOfRangeException(nameof(applyBudgetPerTick), "Apply budget per tick must be at least 1.");
            ApplyBudgetPerTick = applyBudgetPerTick;
        }

        /// <summary>Enqueues an effect apply request. Actual application happens on the next <see cref="Tick"/>.</summary>
        /// <param name="effectId">Effect id to apply.</param>
        /// <param name="source">Caster target identifier.</param>
        /// <param name="target">Receiving target identifier.</param>
        /// <param name="level">Effect level.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown at the call when the effect id is outside the
        /// catalog.</exception>
        public void EnqueueApply(int effectId, TargetId source, TargetId target, int level = 1)
        {
            if ((uint)effectId >= (uint)_catalog.Count)
                throw new ArgumentOutOfRangeException(nameof(effectId), effectId, "Effect id is outside the catalog.");
            EnqueueApply(effectId, source, target, level, BigNum.Zero);
        }

        internal void EnqueueApply(int effectId, TargetId source, TargetId target, int level, BigNum eventMagnitude)
        {
            _queue.Enqueue(new PendingApply
            {
                EffectId = effectId, Source = source, Target = target, Level = level, EventMagnitude = eventMagnitude,
            });
        }

        /// <summary>Evaluates a compiled condition list outside the drain loop, with no event context.</summary>
        internal bool CheckConditions(
            CompiledCondition[] conditions, EffectTarget target, EffectTarget? source, bool hasSource) =>
            EvaluateConditions(conditions, target, source, hasSource, BigNum.Zero);

        /// <summary>Evaluates a standalone compiled magnitude outside the drain loop, with no event context.</summary>
        internal BigNum ComputeMagnitude(
            CompiledMagnitude magnitude, EffectTarget target, EffectTarget? source, bool hasSource, int level) =>
            EvaluateMagnitude(magnitude, target, source, hasSource, level, stack: 1, BigNum.Zero);

        // Applies one effect outside the drain loop (World calls it while processing commands, before the
        // phases run). Must not be called from inside the phases — the drain loop owns the queue while it
        // runs, and re-entering it would break the per-tick budget.
        internal void ApplyImmediate(int effectId, TargetId source, TargetId target, int level) =>
            ProcessPendingApply(new PendingApply
            {
                EffectId = effectId, Source = source, Target = target, Level = level, EventMagnitude = BigNum.Zero,
            });

        /// <summary>Processes one tick in the six-phase order documented on the class. Observation buffers
        /// (lifecycle events, attribute changes, gameplay events) are cleared first, so after the call they
        /// hold exactly this tick's output.</summary>
        public void Tick()
        {
            ClearObservationBuffers();
            RunPhases();
        }

        internal void RunPhases()
        {
            DrainApplications();
            AdvanceTime();
            _lastAdvancedTick = CurrentTick;
            SettleAttributes();
            CurrentTick++;
        }
    }
}
