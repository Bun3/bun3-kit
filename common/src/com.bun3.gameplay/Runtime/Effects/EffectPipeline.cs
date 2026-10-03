#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;

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
    /// starts). Composed of this file plus <c>EffectPipeline.Events.cs</c> (gameplay event emission and
    /// OnEvent chain routing).
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
        private ulong _nextInstanceId = 1;

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
        internal ulong NextInstanceId
        {
            get => _nextInstanceId;
            set => _nextInstanceId = value;
        }

        /// <summary>Number of apply requests left in the queue this tick because the budget was exceeded.</summary>
        public int PendingApplyCount => _queue.Count;

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
        public void EnqueueApply(int effectId, TargetId source, TargetId target, int level = 1) =>
            EnqueueApply(effectId, source, target, level, BigNum.Zero);

        internal void EnqueueApply(int effectId, TargetId source, TargetId target, int level, BigNum eventMagnitude)
        {
            _queue.Enqueue(new PendingApply
            {
                EffectId = effectId, Source = source, Target = target, Level = level, EventMagnitude = eventMagnitude,
            });
        }

        /// <summary>Random source shared with this pipeline.</summary>
        internal IRng Rng => _rng;

        /// <summary>Evaluates a compiled condition list outside the drain loop, with no event context.</summary>
        internal bool CheckConditions(
            CompiledCondition[] conditions, EffectTarget target, EffectTarget? source, bool hasSource) =>
            EvaluateConditions(conditions, target, source, hasSource, BigNum.Zero);

        /// <summary>Evaluates a standalone compiled magnitude outside the drain loop, with no event context.</summary>
        internal BigNum ComputeMagnitude(
            CompiledMagnitude magnitude, EffectTarget target, EffectTarget? source, bool hasSource, int level) =>
            EvaluateMagnitudeCore(
                magnitude.Base, magnitude.PerLevel, magnitude.Calc, magnitude.ByLevel, magnitude.Tail, magnitude.Increment,
                target, source, hasSource, level, stack: 1, BigNum.Zero);

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

        internal void ClearObservationBuffers()
        {
            _gameplayEventCount = 0;
            var count = _resolver.TargetIds.Count;
            for (var i = 0; i < count; i++)
            {
                var target = _resolver.TargetAt(i);
                target.ClearEffectEvents();
                target.Attributes.ClearChanges();
            }
        }

        internal void RunPhases()
        {
            DrainApplications();
            AdvanceTime();
            SettleAttributes();
            // Phase 6, event finalization: no pipeline work. Observation buffers are read after this call
            // and cleared at the start of the next tick.

            CurrentTick++;
        }

        /// <summary>
        /// Removes every active instance on the target whose effect asset tags have any query tag as
        /// self-or-ancestor (in ascending Id order). Modifier detachment, GrantedTags reclamation, and
        /// removal from the active list happen immediately in this call, but the resulting attribute
        /// Current values are not recomputed until the next tick's rebuild phase. Removal takes the same
        /// cleanup path as expiry but fires the <see cref="EffectLifecycleKind.RemovedPrematurely"/> event
        /// and <see cref="ChainTrigger.OnCompletePrematurely"/> chains (never OnCompleteNormal).
        /// </summary>
        /// <param name="target">Target identifier.</param>
        /// <param name="query">Dispel query tag container.</param>
        /// <returns>Number of instances removed; 0 if the target does not resolve.</returns>
        public int RemoveByTags(TargetId target, TagContainer query)
        {
            if (query is null) throw new ArgumentNullException(nameof(query));

            Span<GameplayTag> queryTags = stackalloc GameplayTag[query.ExactKindCount];
            query.CopyExactTags(queryTags);
            return RemoveByTagSpan(target, queryTags);
        }

        internal int RemoveByTagSpan(TargetId target, ReadOnlySpan<GameplayTag> queryTags)
        {
            if (!_resolver.TryResolve(target, out var effectTarget) || effectTarget is null) return 0;

            var tagCatalog = effectTarget.Tags.Catalog;
            var active = effectTarget.ActiveEffects;
            _removalScratch.Clear();
            for (var i = 0; i < active.Count; i++)
            {
                var instance = active[i];
                if (MatchesDispelQuery(_catalog.GetEffect(instance.EffectId).AssetTags, queryTags, tagCatalog))
                    _removalScratch.Add(instance);
            }

            var removed = _removalScratch.Count;
            for (var i = 0; i < removed; i++)
            {
                RemoveInstancePrematurely(effectTarget, _removalScratch[i]);
            }

            _removalScratch.Clear();
            return removed;
        }

        /// <summary>Removes one active instance on the target by id. Modifiers detach immediately in this
        /// call, but attribute Current values recompute on the next tick's rebuild phase. Removal path,
        /// events, and chains match <see cref="RemoveByTags"/> (<see cref="ChainTrigger.OnCompletePrematurely"/>).</summary>
        /// <param name="target">Target identifier.</param>
        /// <param name="instanceId">Instance id to remove.</param>
        /// <returns><see langword="true"/> if the instance was found and removed; <see langword="false"/>
        /// if the target does not resolve or the id does not exist.</returns>
        public bool RemoveById(TargetId target, ulong instanceId)
        {
            if (!_resolver.TryResolve(target, out var effectTarget) || effectTarget is null) return false;

            var active = effectTarget.ActiveEffects;
            for (var i = 0; i < active.Count; i++)
            {
                if (active[i].Id != instanceId) continue;
                RemoveInstancePrematurely(effectTarget, active[i]);
                return true;
            }

            return false;
        }

        // Matches when any explicit query tag is self-or-ancestor of any effect asset tag.
        private static bool MatchesDispelQuery(GameplayTag[] assetTags, ReadOnlySpan<GameplayTag> queryTags, TagCatalog tagCatalog)
        {
            for (var q = 0; q < queryTags.Length; q++)
            {
                for (var a = 0; a < assetTags.Length; a++)
                {
                    if (tagCatalog.IsAncestorOrSelf(queryTags[q], assetTags[a])) return true;
                }
            }

            return false;
        }

        private void RemoveInstancePrematurely(EffectTarget target, EffectInstance instance) =>
            RemoveInstanceCompletely(
                target, instance, _catalog.GetEffect(instance.EffectId),
                EffectLifecycleKind.RemovedPrematurely, ChainTrigger.OnCompletePrematurely);

        // Phase 1: drain the apply queue up to the per-tick budget. Requests newly enqueued this tick by
        // chains (OnApplication/OnStackOverflow) are processed in the same while loop as long as budget
        // remains — the budget is the only cap.
        private void DrainApplications()
        {
            var budget = ApplyBudgetPerTick;
            while (budget > 0 && _queue.Count > 0)
            {
                budget--;
                var pending = _queue.Dequeue();
                ProcessPendingApply(in pending);
            }
        }

        private void ProcessPendingApply(in PendingApply pending)
        {
            if (!_resolver.TryResolve(pending.Target, out var target) || target is null)
            {
                UnresolvedTargetDropCount++;
                return;
            }

            var effect = _catalog.GetEffect(pending.EffectId);

            if (IsImmune(target, effect))
            {
                ImmuneDropCount++;
                return;
            }

            var hasSource = _resolver.TryResolve(pending.Source, out var source) && source is not null;

            if (!RollChance(effect, target, source, hasSource, pending.Level, pending.EventMagnitude))
            {
                ChanceDropCount++;
                return;
            }

            if (!EvaluateConditions(effect.ApplicationConditions, target, source, hasSource, pending.EventMagnitude))
            {
                ConditionDropCount++;
                return;
            }

            // DR immunity must be decided before the RemoveOnApply side effect — otherwise an immune
            // application would observably dispel the target's existing effects first (every other drop
            // path — Immune/Chance/Condition — also short-circuits before side effects). DR applies only
            // to the new-instance path, so when an active instance of the same effect exists (merge target)
            // we skip it and keep the existing merge behavior. ApplyDrHistory mutates history, so it is
            // called exactly once — the ticks computed here are passed through to CreateInstance.
            var precomputedDurationTicks = -1;
            if (effect.DurationType == EffectDurationType.Duration && effect.DrCategory.IsValid
                && FindActiveByEffectId(target, pending.EffectId) == null)
            {
                var value = ComputeScaledDuration(effect, target, source, hasSource, pending.Level, stack: 1, pending.EventMagnitude);
                value *= ApplyDrHistory(target, effect);
                var ticks = ToTicksFloor(value);
                if (ticks <= 0)
                {
                    DrImmuneDropCount++;
                    return;
                }

                precomputedDurationTicks = ticks;
            }

            RemoveOnApply(target, effect, pending.EffectId);

            if (effect.DurationType == EffectDurationType.Instant)
            {
                ApplyInstant(effect, target, source, hasSource, in pending, pending.EventMagnitude);
            }
            else
            {
                ApplyDurationOrInfinite(effect, target, in pending, precomputedDurationTicks);
            }
        }

        // ChanceToApply roll — null always passes. Scales the evaluated chance to [0,1] integer space
        // (×2^32) and compares against NextUInt32() as BigNum integers — no floating point; the
        // chance<=0 / chance>=1 boundaries fall out of the same comparison.
        private bool RollChance(
            CompiledEffect effect, EffectTarget target, EffectTarget? source, bool hasSource, int level,
            BigNum eventMagnitude)
        {
            var chance = effect.ChanceToApply;
            if (chance is null) return true;

            var probability = EvaluateMagnitudeCore(
                chance.Base, chance.PerLevel, chance.Calc, chance.ByLevel, chance.Tail, chance.Increment,
                target, source, hasSource, level, stack: 1, eventMagnitude);
            var roll = (BigNum)(long)_rng.NextUInt32();
            return roll < probability * RngRollRange;
        }

        // RemoveOnApplyTags — just before applying, prematurely removes every active instance whose effect
        // AssetTags hierarchically match these tags. The same effect (about to merge) is excluded.
        private void RemoveOnApply(EffectTarget target, CompiledEffect effect, int incomingEffectId)
        {
            if (effect.RemoveOnApplyTags.Length == 0) return;
            var active = target.ActiveEffects;
            if (active.Count == 0) return;

            var tagCatalog = target.Tags.Catalog;
            _removalScratch.Clear();
            for (var i = 0; i < active.Count; i++)
            {
                var instance = active[i];
                if (instance.EffectId == incomingEffectId) continue;   // merge takes precedence — exclude
                var otherAssetTags = _catalog.GetEffect(instance.EffectId).AssetTags;
                if (MatchesDispelQuery(otherAssetTags, effect.RemoveOnApplyTags, tagCatalog))
                    _removalScratch.Add(instance);
            }

            for (var i = 0; i < _removalScratch.Count; i++)
            {
                RemoveInstancePrematurely(target, _removalScratch[i]);
            }

            _removalScratch.Clear();
        }

        // Checks whether any immunity tag of the target's active instances is self-or-ancestor of any
        // asset tag on the incoming effect.
        private bool IsImmune(EffectTarget target, CompiledEffect incomingEffect)
        {
            if (incomingEffect.AssetTags.Length == 0) return false;
            var active = target.ActiveEffects;
            if (active.Count == 0) return false;

            var tagCatalog = target.Tags.Catalog;
            for (var i = 0; i < active.Count; i++)
            {
                var instance = active[i];
                if (!instance.Enabled) continue;
                var immunityTags = _catalog.GetEffect(instance.EffectId).ImmunityTags;
                for (var j = 0; j < immunityTags.Length; j++)
                {
                    for (var k = 0; k < incomingEffect.AssetTags.Length; k++)
                    {
                        if (tagCatalog.IsAncestorOrSelf(immunityTags[j], incomingEffect.AssetTags[k]))
                            return true;
                    }
                }
            }

            return false;
        }

        private static bool EvaluateConditions(
            CompiledCondition[] conditions, EffectTarget target, EffectTarget? source, bool hasSource,
            BigNum eventMagnitude)
        {
            for (var i = 0; i < conditions.Length; i++)
            {
                if (!EvaluateCondition(conditions[i], target, source, hasSource, eventMagnitude))
                    return false;
            }

            return true;
        }

        private static bool EvaluateCondition(
            CompiledCondition condition, EffectTarget target, EffectTarget? source, bool hasSource,
            BigNum eventMagnitude)
        {
            var left = EvaluateOperand(condition.Left, target, source, hasSource, eventMagnitude);
            var right = EvaluateOperand(condition.Right, target, source, hasSource, eventMagnitude);
            var cmp = left.CompareTo(right);
            switch (condition.Op)
            {
                case ComparisonOp.Equal: return cmp == 0;
                case ComparisonOp.NotEqual: return cmp != 0;
                case ComparisonOp.Less: return cmp < 0;
                case ComparisonOp.LessOrEqual: return cmp <= 0;
                case ComparisonOp.Greater: return cmp > 0;
                default: return cmp >= 0;   // GreaterOrEqual
            }
        }

        // Constant → value; Attribute → target Current × factor; SourceAttribute → source Current × factor
        // (0 when source unresolved); EventMagnitude → the triggering event's magnitude × factor (0 when
        // there is no event context, e.g. outside the one-hop effect an OnEvent chain applies directly).
        private static BigNum EvaluateOperand(
            Operand operand, EffectTarget target, EffectTarget? source, bool hasSource, BigNum eventMagnitude)
        {
            switch (operand.Kind)
            {
                case OperandKind.Constant:
                    return operand.Value;
                case OperandKind.Attribute:
                    return target.Attributes.GetCurrent(operand.AttributeId) * operand.Value;
                case OperandKind.EventMagnitude:
                    return eventMagnitude * operand.Value;
                default:   // SourceAttribute
                    return hasSource ? source!.Attributes.GetCurrent(operand.AttributeId) * operand.Value : BigNum.Zero;
            }
        }

        private BigNum EvaluateMagnitude(
            CompiledModifier modifier, EffectTarget target, EffectTarget? source, bool hasSource, int level, int stack,
            BigNum eventMagnitude) =>
            EvaluateMagnitudeCore(
                modifier.Base, modifier.PerLevel, modifier.Calc, modifier.ByLevel, modifier.Tail, modifier.Increment,
                target, source, hasSource, level, stack, eventMagnitude);

        private BigNum EvaluateMagnitudeCore(
            Operand? @base, Operand? perLevel, IMagnitudeCalc? calc, BigNum[]? byLevel, LevelTail tail, BigNum increment,
            EffectTarget target, EffectTarget? source, bool hasSource, int level, int stack, BigNum eventMagnitude)
        {
            if (calc != null)
            {
                var ctx = new MagnitudeContext(target, source, hasSource, level, stack, CurrentTick);
                return calc.Calculate(in ctx);
            }

            if (byLevel != null)
            {
                return EvaluateByLevel(byLevel, tail, increment, level);
            }

            var value = EvaluateOperand(@base!.Value, target, source, hasSource, eventMagnitude);
            if (perLevel.HasValue)
            {
                value += EvaluateOperand(perLevel.Value, target, source, hasSource, eventMagnitude) * (level - 1);
            }

            return value;
        }

        // Level-table lookup — levels <= 0 clamp to 1, levels within MaxLevel read directly, and the
        // overflow follows the Tail policy (Clamp = hold last value, Extrapolate = last value + increment × levels past the end).
        private static BigNum EvaluateByLevel(BigNum[] byLevel, LevelTail tail, BigNum increment, int level)
        {
            var clampedLevel = level <= 0 ? 1 : level;
            var maxLevel = byLevel.Length;
            if (clampedLevel <= maxLevel)
            {
                return byLevel[clampedLevel - 1];
            }

            var last = byLevel[maxLevel - 1];
            return tail == LevelTail.Clamp ? last : last + increment * (clampedLevel - maxLevel);
        }

        // DurationPerLevel (if any) × DurationScale (if any; multiplier 1 otherwise) — a continuous value
        // before truncation/clamping. Shared by CreateInstance (new instances) and MergeReapply's
        // Refresh/ExtendCapped (new-duration recomputation).
        private BigNum ComputeScaledDuration(
            CompiledEffect effect, EffectTarget target, EffectTarget? source, bool hasSource, int level, int stack,
            BigNum eventMagnitude)
        {
            var baseDuration = effect.DurationPerLevel != null
                ? EvaluateByLevel(effect.DurationPerLevel, LevelTail.Clamp, BigNum.Zero, level)
                : (BigNum)effect.DurationTicks;

            if (effect.DurationScale == null) return baseDuration;

            var scale = effect.DurationScale;
            var scaleValue = EvaluateMagnitudeCore(
                scale.Base, scale.PerLevel, scale.Calc, scale.ByLevel, scale.Tail, scale.Increment,
                target, source, hasSource, level, stack, eventMagnitude);
            return baseDuration * scaleValue;
        }

        // "New duration" used by merges (Refresh/ExtendCapped) — DR applies only to the new-instance
        // path, so it is not composed here. Clamps to a minimum of 1 tick after truncation.
        private int ComputeMergedDurationTicks(
            CompiledEffect effect, EffectTarget target, EffectInstance instance, BigNum eventMagnitude)
        {
            var hasSource = _resolver.TryResolve(instance.Source, out var source) && source is not null;
            var value = ComputeScaledDuration(effect, target, source, hasSource, instance.Level, instance.Stack, eventMagnitude);
            var ticks = ToTicksFloor(value);
            return ticks <= 0 ? 1 : ticks;
        }

        // Reads and updates DR history and returns the duration multiplier for this application. If the
        // window (DrWindowTicks) has elapsed, the count resets before staging — the first application
        // (count 0) gets multiplier 1, the nth (n>=1) gets DrStageMultipliers[min(n-1, length-1)].
        // Regardless of whether the application succeeds or fizzles, the count is always incremented and
        // lastAppliedTick refreshed (an immune application also extends the window).
        private BigNum ApplyDrHistory(EffectTarget target, CompiledEffect effect)
        {
            var index = target.FindOrCreateDrHistory(effect.DrCategory.Index);
            ref var entry = ref target.DrHistoryAt(index);

            if (entry.LastAppliedTick + effect.DrWindowTicks < CurrentTick)
            {
                entry.AppliedCount = 0;
            }

            var stage = entry.AppliedCount;
            var multiplier = stage == 0
                ? BigNum.One
                : effect.DrStageMultipliers[Math.Min(stage - 1, effect.DrStageMultipliers.Length - 1)];

            entry.AppliedCount++;
            entry.LastAppliedTick = CurrentTick;
            return multiplier;
        }

        private void ApplyInstant(
            CompiledEffect effect, EffectTarget target, EffectTarget? source, bool hasSource, in PendingApply pending,
            BigNum eventMagnitude)
        {
            ExecuteOnBase(effect, target, source, hasSource, pending.Source, pending.Level, stack: 1, eventMagnitude);
            FireChain(effect.Chains, ChainTrigger.OnApplication, pending.Source, pending.Level, pending.Target, target);
        }

        // The instant/periodic payload, applied straight to Base: modifiers, then executions, then one gameplay
        // event per RaiseEvents entry carrying the actual Base delta (and the modifiers-only nominal amount).
        private void ExecuteOnBase(
            CompiledEffect effect, EffectTarget target, EffectTarget? source, bool hasSource, TargetId sourceId,
            int level, int stack, BigNum eventMagnitude)
        {
            var raises = effect.RaiseEvents;
            Span<BigNum> before = stackalloc BigNum[raises.Length];
            Span<BigNum> nominal = stackalloc BigNum[raises.Length];
            for (var r = 0; r < raises.Length; r++)
            {
                before[r] = target.Attributes.GetBase(raises[r].AttributeId);
                nominal[r] = BigNum.Zero;
            }

            var modifiers = effect.Modifiers;
            for (var i = 0; i < modifiers.Length; i++)
            {
                var modifier = modifiers[i];
                var magnitude = EvaluateMagnitude(modifier, target, source, hasSource, level, stack, eventMagnitude);
                for (var r = 0; r < raises.Length; r++)
                {
                    if (raises[r].AttributeId == modifier.AttributeId) nominal[r] += magnitude;
                }

                ApplyModifierToBase(target, modifier, magnitude);
            }

            var executions = effect.Executions;
            for (var i = 0; i < executions.Length; i++)
            {
                RunExecution(executions[i], target, source, hasSource, sourceId, target.Id, level, stack, eventMagnitude);
            }

            for (var r = 0; r < raises.Length; r++)
            {
                var delta = target.Attributes.GetBase(raises[r].AttributeId) - before[r];
                var evt = new GameplayEvent(raises[r].Tag, sourceId, target.Id, delta, nominal[r]);
                RaiseGameplayEvent(in evt);
            }
        }

        // Instant has no instance, so its modifiers are permanent changes applied straight to Base.
        // Multiply/Override carry aggregation semantics (Duration/Infinite only) and do not exist for
        // Instant — express percent changes as Add + a self-referencing operand (e.g. Operand.Attribute(Hp, -0.3)).
        private static void ApplyModifierToBase(EffectTarget target, CompiledModifier modifier, BigNum magnitude)
        {
            switch (modifier.Op)
            {
                case AttributeModifierOp.Add:
                    target.Attributes.AddBase(modifier.AttributeId, magnitude);
                    break;
                default:
                    // Unreachable: EffectCatalogBuilder forces Op to Add for Instant/periodic effects.
                    throw new InvalidOperationException($"Instant modifiers only allow Add: {modifier.Op}");
            }
        }

        private void RunExecution(
            CompiledExecution execution, EffectTarget target, EffectTarget? source, bool hasSource,
            TargetId sourceId, TargetId targetId, int level, int stack, BigNum eventMagnitude)
        {
            // ponytail: input counts are authoring-effect scale, so stackalloc suffices — switch to a heap array if large inputs become a real need.
            Span<BigNum> inputs = stackalloc BigNum[execution.Inputs.Length];
            for (var i = 0; i < execution.Inputs.Length; i++)
            {
                inputs[i] = EvaluateOperand(execution.Inputs[i], target, source, hasSource, eventMagnitude);
            }

            var ctx = new ExecutionContext(
                this, target, source, hasSource, sourceId, targetId,
                level, stack, CurrentTick, inputs, _rng);
            execution.Calc.Execute(ref ctx);
        }

        // Chain-firing helper. For each edge matching trigger: evaluate the edge conditions against the
        // origin target's current state plus the firing instance's Source → without a Selector use the
        // origin target as-is, otherwise pick targets via SelectorContext (inheriting the firing
        // instance's Source) → EnqueueApply for each target (source inherited; level is the firing level
        // when LevelRule is Inherit, or the edge's FixedLevel when Fixed).
        private void FireChain(
            CompiledChainEdge[] chains, ChainTrigger trigger, TargetId source, int level,
            TargetId originTargetId, EffectTarget originTarget)
        {
            for (var i = 0; i < chains.Length; i++)
            {
                var chain = chains[i];
                if (chain.Trigger != trigger) continue;

                var hasSource = _resolver.TryResolve(source, out var sourceTarget) && sourceTarget is not null;
                // Lifecycle chains (OnApplication/OnCompleteNormal/OnCompletePrematurely/OnStackOverflow)
                // have no triggering event; Build also rejects EventMagnitude in a non-OnEvent edge's
                // Conditions, so this value is provably unreachable here.
                if (!EvaluateConditions(chain.Conditions, originTarget, sourceTarget, hasSource, BigNum.Zero)) continue;

                var chainLevel = chain.LevelRule == ChainLevelRule.Inherit ? level : chain.FixedLevel;
                ApplyToSelection(
                    chain.Selector, chain.SelectorParams, chain.EffectId, source, originTargetId, chainLevel, default);
            }
        }

        // The dispatch step shared by chain edges, event chains and ability effects: without a selector the
        // effect goes to the origin, otherwise to every selected target. evt is default outside an event
        // path; its magnitude rides along into the applied effect (one hop only).
        internal void ApplyToSelection(
            ITargetSelector? selector, ReadOnlySpan<BigNum> selectorParams, int effectId, TargetId source,
            TargetId origin, int level, in GameplayEvent evt)
        {
            if (selector is null)
            {
                EnqueueApply(effectId, source, origin, level, evt.Magnitude);
                return;
            }

            // ponytail: target counts are authoring-effect scale, so a 32-slot stack buffer suffices — on overflow only the first 32 are applied.
            Span<TargetId> targets = stackalloc TargetId[32];
            var ctx = new SelectorContext(source, origin, selectorParams, _rng, _resolver, in evt);
            var count = SelectTargets(selector, in ctx, targets);
            for (var k = 0; k < count; k++)
            {
                EnqueueApply(effectId, source, targets[k], level, evt.Magnitude);
            }
        }

        // Counts truncation when a selector's reported total exceeds the buffer and returns the clamped
        // count actually written into results.
        private int SelectTargets(ITargetSelector selector, in SelectorContext ctx, Span<TargetId> results)
        {
            var selected = selector.Select(in ctx, results);
            if (selected <= results.Length) return selected;

            SelectorTruncationCount++;
            return results.Length;
        }

        // Duration/Infinite: if the target has an active instance with the same EffectId, merge via the
        // stack policy; otherwise create a new instance, attach modifiers, and grant tags.
        // precomputedDurationTicks is the duration (with DR composed) that ProcessPendingApply computed
        // before RemoveOnApply (-1 when DR is unused or a merge target exists) and CreateInstance uses it
        // as-is — prevents a duplicate ApplyDrHistory call.
        private void ApplyDurationOrInfinite(
            CompiledEffect effect, EffectTarget target, in PendingApply pending, int precomputedDurationTicks)
        {
            var existing = FindActiveByEffectId(target, pending.EffectId);
            if (existing != null)
            {
                MergeReapply(effect, target, existing, pending.EventMagnitude);
                return;
            }

            CreateInstance(effect, target, in pending, precomputedDurationTicks, pending.EventMagnitude);
        }

        private static EffectInstance? FindActiveByEffectId(EffectTarget target, int effectId)
        {
            var active = target.ActiveEffects;
            for (var i = 0; i < active.Count; i++)
            {
                if (active[i].EffectId == effectId) return active[i];
            }

            return null;
        }

        // Reapply merge: AddStack raises the stack and routes the overflow to HandleStackOverflow; both
        // Refresh and AddStack reset duration/period timers per policy. StackChanged is raised only when
        // the stack value actually changes. A merge is not a new application, so no OnApplication chain fires here.
        private void MergeReapply(CompiledEffect effect, EffectTarget target, EffectInstance instance, BigNum eventMagnitude)
        {
            var stackPolicy = effect.Stack;
            if (stackPolicy.OnReapply == StackReapply.AddStack)
            {
                // Build guarantees MaxStack > 0 for the AddStack policy.
                var wouldBe = instance.Stack + stackPolicy.AddStackCount;
                if (wouldBe > stackPolicy.MaxStack)
                {
                    HandleStackOverflow(effect, target, instance, eventMagnitude);
                }
                else if (wouldBe != instance.Stack)
                {
                    instance.Stack = wouldBe;
                    target.RaiseEffectEvent(new EffectLifecycleEvent(
                        EffectLifecycleKind.StackChanged, instance.Id, instance.EffectId, instance.Stack, instance.Source, instance.Level));
                    MarkDirtyForModifiers(target, effect);
                    SyncLevelFromStack(effect, target, instance, eventMagnitude);
                }
            }
            else if (stackPolicy.OnReapply == StackReapply.ExtendCapped)
            {
                // Extends duration only, without touching the stack — cap = new duration × ExtendCapMultiplier.
                // "New duration" includes DurationPerLevel/DurationScale (DR applies to new instances only).
                var newDuration = ComputeMergedDurationTicks(effect, target, instance, eventMagnitude);
                var cap = ToTicksFloor((BigNum)newDuration * stackPolicy.ExtendCapMultiplier);
                var extended = instance.RemainingTicks + newDuration;
                instance.RemainingTicks = extended < cap ? extended : cap;
                instance.DurationTicks = instance.RemainingTicks;
            }

            if (stackPolicy.OnReapply != StackReapply.ExtendCapped
                && stackPolicy.RefreshDurationOnReapply && effect.DurationType == EffectDurationType.Duration)
            {
                instance.RemainingTicks = ComputeMergedDurationTicks(effect, target, instance, eventMagnitude);
                instance.DurationTicks = instance.RemainingTicks;
            }

            if (stackPolicy.ResetPeriodOnReapply && effect.PeriodTicks > 0)
            {
                instance.PeriodCountdown = effect.PeriodTicks;
            }
        }

        // LevelFromStack — after Stack changes, syncs Level and re-evaluates/re-attaches the modifiers
        // that were snapshotted at apply time (periodic effects are excluded: they evaluate Level directly each period).
        private void SyncLevelFromStack(CompiledEffect effect, EffectTarget target, EffectInstance instance, BigNum eventMagnitude)
        {
            if (!effect.Stack.LevelFromStack || instance.Level == instance.Stack) return;

            instance.Level = instance.Stack;
            if (instance.Enabled) AbilityGrantHook?.Invoke(target, instance, true);
            if (effect.PeriodTicks > 0) return;

            target.Attributes.DetachModifiers(instance);
            var hasSource = _resolver.TryResolve(instance.Source, out var source) && source is not null;
            var modifiers = effect.Modifiers;
            for (var i = 0; i < modifiers.Length; i++)
            {
                var modifier = modifiers[i];
                var magnitude = EvaluateMagnitude(modifier, target, source, hasSource, instance.Level, instance.Stack, eventMagnitude);
                target.Attributes.AttachModifier(instance, i, modifier.AttributeId, modifier.Op, magnitude, modifier.ScaleWithStack);
            }
        }

        // BigNum → tick (int) truncation toward zero. Exponent stays small (DurationTicks × ExtendCapMultiplier
        // scale), but the iteration count is capped by digit bounds so extreme authored values saturate to
        // int.MaxValue instead of looping forever.
        private static int ToTicksFloor(BigNum value)
        {
            if (value.Sign <= 0) return 0;

            var mantissa = value.Mantissa;
            if (value.Exponent >= 0)
            {
                var steps = value.Exponent > 10 ? 10 : value.Exponent;
                for (var i = 0; i < steps; i++)
                {
                    if (mantissa > int.MaxValue) return int.MaxValue;
                    mantissa *= 10;
                }
            }
            else
            {
                var steps = -value.Exponent > 19 ? 19 : -value.Exponent;
                for (var i = 0; i < steps; i++) mantissa /= 10;
            }

            return mantissa > int.MaxValue ? int.MaxValue : (int)mantissa;
        }

        // Stack overflow: fires the OnStackOverflow chain edges first, then enqueues OverflowEffectId if
        // the policy is ApplyEffect (level always inherited from the instance). Afterward the stack resets
        // to 1 when ClearStacksOnOverflow (existing effect/modifiers kept, only stacks cleared), otherwise
        // clamps to MaxStack as before.
        private void HandleStackOverflow(CompiledEffect effect, EffectTarget target, EffectInstance instance, BigNum eventMagnitude)
        {
            FireChain(effect.Chains, ChainTrigger.OnStackOverflow, instance.Source, instance.Level, target.Id, target);

            if (effect.Stack.OnOverflow == StackOverflow.ApplyEffect)
            {
                EnqueueApply(effect.OverflowEffectId, instance.Source, target.Id, instance.Level);
            }

            var newStack = effect.Stack.ClearStacksOnOverflow ? 1 : effect.Stack.MaxStack;
            if (newStack != instance.Stack)
            {
                instance.Stack = newStack;
                target.RaiseEffectEvent(new EffectLifecycleEvent(
                    EffectLifecycleKind.StackChanged, instance.Id, instance.EffectId, instance.Stack, instance.Source, instance.Level));
                MarkDirtyForModifiers(target, effect);
                SyncLevelFromStack(effect, target, instance, eventMagnitude);
            }
        }

        // New instance: rent → insert in ascending Id order → attach modifiers (unless periodic) → grant
        // GrantedTags → Applied event → fire OnApplication chains. Periodic effects (PeriodTicks > 0) only
        // adjust Base each period via the same path as Instant and never attach modifiers persistently.
        // For Duration effects with DR, ProcessPendingApply computed the duration (and immunity) before
        // RemoveOnApply and passed it as precomputedDurationTicks — this method uses that value as-is and
        // never calls ApplyDrHistory again (prevents double history mutation). Duration without DR is
        // computed here (clamped to a minimum of 1 tick).
        private void CreateInstance(
            CompiledEffect effect, EffectTarget target, in PendingApply pending, int precomputedDurationTicks,
            BigNum eventMagnitude)
        {
            var hasSource = _resolver.TryResolve(pending.Source, out var source) && source is not null;

            var remainingTicks = -1;
            if (effect.DurationType == EffectDurationType.Duration)
            {
                if (effect.DrCategory.IsValid)
                {
                    // If DR-immune (0 ticks or less), ProcessPendingApply already dropped silently and
                    // returned, so reaching this path guarantees precomputedDurationTicks > 0.
                    remainingTicks = precomputedDurationTicks;
                }
                else
                {
                    var value = ComputeScaledDuration(effect, target, source, hasSource, pending.Level, stack: 1, eventMagnitude);
                    var ticks = ToTicksFloor(value);
                    remainingTicks = ticks <= 0 ? 1 : ticks;   // minimum 1 tick.
                }
            }

            var periodCountdown = effect.PeriodTicks > 0 ? effect.PeriodTicks : -1;
            var instance = EffectInstance.Rent(
                _instancePool, _nextInstanceId++, pending.EffectId, pending.Source, pending.Level, stack: 1,
                remainingTicks, periodCountdown, CurrentTick);
            target.InsertActive(instance, effect);

            if (effect.PeriodTicks == 0)
            {
                var modifiers = effect.Modifiers;
                for (var i = 0; i < modifiers.Length; i++)
                {
                    var modifier = modifiers[i];
                    var magnitude = EvaluateMagnitude(modifier, target, source, hasSource, pending.Level, instance.Stack, eventMagnitude);
                    target.Attributes.AttachModifier(instance, i, modifier.AttributeId, modifier.Op, magnitude, modifier.ScaleWithStack);
                }
            }

            var grantedTags = effect.GrantedTags;
            for (var i = 0; i < grantedTags.Length; i++)
            {
                target.Tags.Add(grantedTags[i]);
            }

            AbilityGrantHook?.Invoke(target, instance, true);

            target.RaiseEffectEvent(new EffectLifecycleEvent(EffectLifecycleKind.Applied, instance.Id, pending.EffectId, instance.Stack, instance.Source, instance.Level));

            FireChain(effect.Chains, ChainTrigger.OnApplication, pending.Source, pending.Level, target.Id, target);
        }

        private static void MarkDirtyForModifiers(EffectTarget target, CompiledEffect effect)
        {
            var modifiers = effect.Modifiers;
            for (var i = 0; i < modifiers.Length; i++)
            {
                target.Attributes.MarkDirty(modifiers[i].AttributeId);
            }
        }

        // Phase 2: iterate targets in TargetId order and their timed instances (Duration or periodic) in Id
        // order; the other instances do nothing here. Instances created this tick are skipped. Period firing runs before the expiry check,
        // so the final period fire on the expiring tick is still applied. Normal expiry (the
        // complete-removal path of ExpireInstance) fires OnCompleteNormal chains, and those apply requests
        // carry over to the next tick since this tick's phase 1 has already passed.
        private void AdvanceTime()
        {
            var count = _resolver.TargetIds.Count;
            for (var t = 0; t < count; t++)
            {
                var target = _resolver.TargetAt(t);

                var active = target.TimedEffects;
                _expiryScratch.Clear();
                for (var i = 0; i < active.Count; i++)
                {
                    var instance = active[i];
                    if (instance.CreatedTick == CurrentTick) continue;   // created this tick; starts advancing next tick

                    var effect = _catalog.GetEffect(instance.EffectId);

                    if (effect.PeriodTicks > 0)
                    {
                        instance.PeriodCountdown--;
                        if (instance.PeriodCountdown == 0)
                        {
                            // Period firing has no triggering event — always zero.
                            FirePeriodic(effect, target, instance, BigNum.Zero);
                            instance.PeriodCountdown = effect.PeriodTicks;
                        }
                    }

                    if (effect.DurationType == EffectDurationType.Duration)
                    {
                        instance.RemainingTicks--;
                        if (instance.RemainingTicks <= 0)
                        {
                            _expiryScratch.Add(instance);
                        }
                    }
                }

                for (var i = 0; i < _expiryScratch.Count; i++)
                {
                    ExpireInstance(target, _expiryScratch[i]);
                }
            }
        }

        // Period elapsed: same path as Instant — Modifiers adjust Base (Add-only, enforced at Build), Executions run.
        private void FirePeriodic(CompiledEffect effect, EffectTarget target, EffectInstance instance, BigNum eventMagnitude)
        {
            var hasSource = _resolver.TryResolve(instance.Source, out var source) && source is not null;
            ExecuteOnBase(effect, target, source, hasSource, instance.Source, instance.Level, instance.Stack, eventMagnitude);
        }

        // Expiry: with the RemoveOneAndRefresh stack policy and stacks remaining, drop one stack and reset
        // the duration (not a normal completion, so no OnCompleteNormal fires). Otherwise remove fully via
        // RemoveInstanceCompletely and fire the OnCompleteNormal chain. Premature removal via
        // RemoveByTags/RemoveById takes the same helper with OnCompletePrematurely (RemoveInstancePrematurely).
        private void ExpireInstance(EffectTarget target, EffectInstance instance)
        {
            var effect = _catalog.GetEffect(instance.EffectId);
            if (effect.Stack.OnExpiration == StackExpiration.RemoveOneAndRefresh && instance.Stack > 1)
            {
                instance.Stack--;
                instance.RemainingTicks = effect.DurationTicks;
                instance.DurationTicks = instance.RemainingTicks;
                target.RaiseEffectEvent(new EffectLifecycleEvent(
                    EffectLifecycleKind.StackChanged, instance.Id, instance.EffectId, instance.Stack, instance.Source, instance.Level));
                MarkDirtyForModifiers(target, effect);
                SyncLevelFromStack(effect, target, instance, BigNum.Zero);
                return;
            }

            RemoveInstanceCompletely(target, instance, effect, EffectLifecycleKind.Expired, ChainTrigger.OnCompleteNormal);
        }

        // Shared complete-removal path — used by normal expiry (ExpireInstance) and premature removal
        // (RemoveByTags/RemoveById). Detaches modifiers, reclaims GrantedTags, removes from the active
        // list, returns the instance to the pool, then raises the eventKind lifecycle event and fires the
        // chainTrigger chains. The firing instance's Source and Level are inherited as-is.
        private void RemoveInstanceCompletely(
            EffectTarget target, EffectInstance instance, CompiledEffect effect,
            EffectLifecycleKind eventKind, ChainTrigger chainTrigger)
        {
            var source = instance.Source;
            var level = instance.Level;

            target.Attributes.DetachModifiers(instance);
            var grantedTags = effect.GrantedTags;
            for (var i = 0; i < grantedTags.Length; i++)
            {
                target.Tags.Remove(grantedTags[i]);
            }

            AbilityGrantHook?.Invoke(target, instance, false);

            target.RemoveActive(instance, effect);
            target.RaiseEffectEvent(new EffectLifecycleEvent(eventKind, instance.Id, instance.EffectId, instance.Stack, instance.Source, instance.Level));
            EffectInstance.Return(_instancePool, instance);

            FireChain(effect.Chains, chainTrigger, source, level, target.Id, target);
        }

        // Phases 3-5 per target in one pass: rebuild what phases 1-2 dirtied, evaluate Ongoing conditions,
        // rebuild what the toggles dirtied. Equivalent to running each phase across all targets because the
        // three read and write only the target's own state (captured modifier magnitudes, own clamp bounds,
        // source-less Ongoing conditions, own tags and ability grants).
        private void SettleAttributes()
        {
            var count = _resolver.TargetIds.Count;
            for (var t = 0; t < count; t++)
            {
                var target = _resolver.TargetAt(t);
                target.Attributes.RebuildDirty();
                EvaluateOngoing(target);
                target.Attributes.RebuildDirty();
            }
        }

        // Phase 4: evaluates the target's Ongoing instances in Id order against the Current values rebuilt
        // in phase 3, once per tick. Toggles only when the result changes — OFF reclaims
        // GrantedTags and marks modifiers dirty, ON grants GrantedTags and marks dirty. The instance
        // itself is not removed (RebuildDirty's RebuildSlot skips modifiers of !Enabled instances, so
        // leaving them attached still excludes them from aggregation). GrantedTags is a counted container,
        // so Add/Remove are symmetric.
        private void EvaluateOngoing(EffectTarget target)
        {
            var active = target.OngoingEffects;
            for (var i = 0; i < active.Count; i++)
            {
                var instance = active[i];
                var effect = _catalog.GetEffect(instance.EffectId);

                // Ongoing evaluation runs every tick with no triggering event — always zero. Build
                // already rejects EventMagnitude in OngoingConditions, so this value is unreachable.
                var satisfied = EvaluateConditions(effect.OngoingConditions, target, source: null, hasSource: false, BigNum.Zero);
                if (satisfied == instance.Enabled) continue;

                instance.Enabled = satisfied;
                var grantedTags = effect.GrantedTags;
                if (satisfied)
                {
                    for (var g = 0; g < grantedTags.Length; g++) target.Tags.Add(grantedTags[g]);
                }
                else
                {
                    for (var g = 0; g < grantedTags.Length; g++) target.Tags.Remove(grantedTags[g]);
                }

                AbilityGrantHook?.Invoke(target, instance, satisfied);

                MarkDirtyForModifiers(target, effect);
            }
        }
    }
}
