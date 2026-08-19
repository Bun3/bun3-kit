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
    /// decrement, expiry; fires OnCompleteNormal chains) → (3) <see cref="RebuildAll"/> (rebuilds slots
    /// marked dirty by 1–2) → (4) <see cref="EvaluateOngoing"/> (ongoing-condition toggles) →
    /// (5) <see cref="RebuildToggled"/> (rebuilds slots marked dirty by 4) → (6) event finalization
    /// (no pipeline work — the game drains the lifecycle/attribute-change event buffers itself).
    /// </summary>
    public sealed class EffectPipeline
    {
        private struct PendingApply
        {
            internal int SpecId;
            internal TargetId Source;
            internal TargetId Target;
            internal int Level;
        }

        private readonly EffectCatalog _catalog;
        private readonly IEffectTargetResolver _resolver;
        private readonly IRng _rng;
        private readonly int _applyBudgetPerTick;
        private readonly Queue<PendingApply> _queue = new Queue<PendingApply>();
        private readonly List<EffectInstance> _expiryScratch = new List<EffectInstance>();
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
        /// <param name="catalog">Effect catalog for spec lookup.</param>
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
            _applyBudgetPerTick = applyBudgetPerTick;
        }

        /// <summary>Enqueues an effect apply request. Actual application happens on the next <see cref="Tick"/>.</summary>
        /// <param name="specId">Effect spec id to apply.</param>
        /// <param name="source">Caster target identifier.</param>
        /// <param name="target">Receiving target identifier.</param>
        /// <param name="level">Effect level.</param>
        public void EnqueueApply(int specId, TargetId source, TargetId target, int level = 1)
        {
            _queue.Enqueue(new PendingApply { SpecId = specId, Source = source, Target = target, Level = level });
        }

        /// <summary>Processes one tick in the six-phase order documented on the class.</summary>
        public void Tick()
        {
            DrainApplications();
            AdvanceTime();
            RebuildAll();
            EvaluateOngoing();
            RebuildToggled();
            // Phase 6, event finalization: no pipeline work. Lifecycle events
            // (EffectTarget.PendingEffectEvents) and attribute-change events (AttributeSet.PendingChanges)
            // are read and drained via Clear* by the game (caller) after this tick.

            CurrentTick++;
        }

        /// <summary>
        /// Removes every active instance on the target whose spec asset tags have any query tag as
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
            if (!_resolver.TryResolve(target, out var effectTarget) || effectTarget is null) return 0;

            var queryCount = query.ExactKindCount;
            Span<GameplayTag> queryTags = stackalloc GameplayTag[queryCount];
            query.CopyExactTags(queryTags);

            var tagCatalog = effectTarget.Tags.Catalog;
            var active = effectTarget.ActiveEffects;
            _removalScratch.Clear();
            for (var i = 0; i < active.Count; i++)
            {
                var instance = active[i];
                if (MatchesDispelQuery(_catalog.GetSpec(instance.SpecId).AssetTags, queryTags, tagCatalog))
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

        // Matches when any explicit query tag is self-or-ancestor of any spec asset tag.
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
                target, instance, _catalog.GetSpec(instance.SpecId),
                EffectLifecycleKind.RemovedPrematurely, ChainTrigger.OnCompletePrematurely);

        // Phase 1: drain the apply queue up to the per-tick budget. Requests newly enqueued this tick by
        // chains (OnApplication/OnStackOverflow) are processed in the same while loop as long as budget
        // remains — the budget is the only cap.
        private void DrainApplications()
        {
            var budget = _applyBudgetPerTick;
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

            var spec = _catalog.GetSpec(pending.SpecId);

            if (IsImmune(target, spec))
            {
                ImmuneDropCount++;
                return;
            }

            var hasSource = _resolver.TryResolve(pending.Source, out var source) && source is not null;

            if (!RollChance(spec, target, source, hasSource, pending.Level))
            {
                ChanceDropCount++;
                return;
            }

            if (!EvaluateConditions(spec.ApplicationConditions, target, source, hasSource))
            {
                ConditionDropCount++;
                return;
            }

            // DR immunity must be decided before the RemoveOnApply side effect — otherwise an immune
            // application would observably dispel the target's existing effects first (every other drop
            // path — Immune/Chance/Condition — also short-circuits before side effects). DR applies only
            // to the new-instance path, so when an active instance of the same spec exists (merge target)
            // we skip it and keep the existing merge behavior. ApplyDrHistory mutates history, so it is
            // called exactly once — the ticks computed here are passed through to CreateInstance.
            var precomputedDurationTicks = -1;
            if (spec.DurationType == EffectDurationType.Duration && spec.DrCategory.IsValid
                && FindActiveBySpec(target, pending.SpecId) == null)
            {
                var value = ComputeScaledDuration(spec, target, source, hasSource, pending.Level, stack: 1);
                value *= ApplyDrHistory(target, spec);
                var ticks = ToTicksFloor(value);
                if (ticks <= 0)
                {
                    DrImmuneDropCount++;
                    return;
                }

                precomputedDurationTicks = ticks;
            }

            RemoveOnApply(target, spec, pending.SpecId);

            if (spec.DurationType == EffectDurationType.Instant)
            {
                ApplyInstant(spec, target, source, hasSource, in pending);
            }
            else
            {
                ApplyDurationOrInfinite(spec, target, in pending, precomputedDurationTicks);
            }
        }

        // ChanceToApply roll — null always passes. Scales the evaluated chance to [0,1] integer space
        // (×2^32) and compares against NextUInt32() as BigNum integers — no floating point; the
        // chance<=0 / chance>=1 boundaries fall out of the same comparison.
        private bool RollChance(
            CompiledEffectSpec spec, EffectTarget target, EffectTarget? source, bool hasSource, int level)
        {
            var chance = spec.ChanceToApply;
            if (chance is null) return true;

            var probability = EvaluateMagnitudeCore(
                chance.Base, chance.PerLevel, chance.Calc, chance.ByLevel, chance.Tail, chance.Increment,
                target, source, hasSource, level, stack: 1);
            var roll = (BigNum)(long)_rng.NextUInt32();
            return roll < probability * RngRollRange;
        }

        // RemoveOnApplyTags — just before applying, prematurely removes every active instance whose spec
        // AssetTags hierarchically match these tags. The same spec (about to merge) is excluded.
        private void RemoveOnApply(EffectTarget target, CompiledEffectSpec spec, int incomingSpecId)
        {
            if (spec.RemoveOnApplyTags.Length == 0) return;
            var active = target.ActiveEffects;
            if (active.Count == 0) return;

            var tagCatalog = target.Tags.Catalog;
            _removalScratch.Clear();
            for (var i = 0; i < active.Count; i++)
            {
                var instance = active[i];
                if (instance.SpecId == incomingSpecId) continue;   // merge takes precedence — exclude
                var otherAssetTags = _catalog.GetSpec(instance.SpecId).AssetTags;
                if (MatchesDispelQuery(otherAssetTags, spec.RemoveOnApplyTags, tagCatalog))
                    _removalScratch.Add(instance);
            }

            for (var i = 0; i < _removalScratch.Count; i++)
            {
                RemoveInstancePrematurely(target, _removalScratch[i]);
            }

            _removalScratch.Clear();
        }

        // Checks whether any immunity tag of the target's active instances is self-or-ancestor of any
        // asset tag on the incoming spec.
        private bool IsImmune(EffectTarget target, CompiledEffectSpec incomingSpec)
        {
            if (incomingSpec.AssetTags.Length == 0) return false;
            var active = target.ActiveEffects;
            if (active.Count == 0) return false;

            var tagCatalog = target.Tags.Catalog;
            for (var i = 0; i < active.Count; i++)
            {
                var instance = active[i];
                if (!instance.Enabled) continue;
                var immunityTags = _catalog.GetSpec(instance.SpecId).ImmunityTags;
                for (var j = 0; j < immunityTags.Length; j++)
                {
                    for (var k = 0; k < incomingSpec.AssetTags.Length; k++)
                    {
                        if (tagCatalog.IsAncestorOrSelf(immunityTags[j], incomingSpec.AssetTags[k]))
                            return true;
                    }
                }
            }

            return false;
        }

        private static bool EvaluateConditions(
            CompiledCondition[] conditions, EffectTarget target, EffectTarget? source, bool hasSource)
        {
            for (var i = 0; i < conditions.Length; i++)
            {
                if (!EvaluateCondition(conditions[i], target, source, hasSource))
                    return false;
            }

            return true;
        }

        private static bool EvaluateCondition(
            CompiledCondition condition, EffectTarget target, EffectTarget? source, bool hasSource)
        {
            var left = EvaluateOperand(condition.Left, target, source, hasSource);
            var right = EvaluateOperand(condition.Right, target, source, hasSource);
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

        // Constant → value; Attribute → target Current × factor; SourceAttribute → source Current × factor (0 when source unresolved).
        private static BigNum EvaluateOperand(Operand operand, EffectTarget target, EffectTarget? source, bool hasSource)
        {
            switch (operand.Kind)
            {
                case OperandKind.Constant:
                    return operand.Value;
                case OperandKind.Attribute:
                    return target.Attributes.GetCurrent(operand.AttributeId) * operand.Value;
                default:   // SourceAttribute
                    return hasSource ? source!.Attributes.GetCurrent(operand.AttributeId) * operand.Value : BigNum.Zero;
            }
        }

        private BigNum EvaluateMagnitude(
            CompiledModifier modifier, EffectTarget target, EffectTarget? source, bool hasSource, int level, int stack) =>
            EvaluateMagnitudeCore(
                modifier.Base, modifier.PerLevel, modifier.Calc, modifier.ByLevel, modifier.Tail, modifier.Increment,
                target, source, hasSource, level, stack);

        private BigNum EvaluateMagnitudeCore(
            Operand? @base, Operand? perLevel, IMagnitudeCalc? calc, BigNum[]? byLevel, LevelTail tail, BigNum increment,
            EffectTarget target, EffectTarget? source, bool hasSource, int level, int stack)
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

            var value = EvaluateOperand(@base!.Value, target, source, hasSource);
            if (perLevel.HasValue)
            {
                value += EvaluateOperand(perLevel.Value, target, source, hasSource) * (level - 1);
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
            CompiledEffectSpec spec, EffectTarget target, EffectTarget? source, bool hasSource, int level, int stack)
        {
            var baseDuration = spec.DurationPerLevel != null
                ? EvaluateByLevel(spec.DurationPerLevel, LevelTail.Clamp, BigNum.Zero, level)
                : (BigNum)spec.DurationTicks;

            if (spec.DurationScale == null) return baseDuration;

            var scale = spec.DurationScale;
            var scaleValue = EvaluateMagnitudeCore(
                scale.Base, scale.PerLevel, scale.Calc, scale.ByLevel, scale.Tail, scale.Increment,
                target, source, hasSource, level, stack);
            return baseDuration * scaleValue;
        }

        // "New duration" used by merges (Refresh/ExtendCapped) — DR applies only to the new-instance
        // path, so it is not composed here. Clamps to a minimum of 1 tick after truncation.
        private int ComputeMergedDurationTicks(CompiledEffectSpec spec, EffectTarget target, EffectInstance instance)
        {
            var hasSource = _resolver.TryResolve(instance.Source, out var source) && source is not null;
            var value = ComputeScaledDuration(spec, target, source, hasSource, instance.Level, instance.Stack);
            var ticks = ToTicksFloor(value);
            return ticks <= 0 ? 1 : ticks;
        }

        // Reads and updates DR history and returns the duration multiplier for this application. If the
        // window (DrWindowTicks) has elapsed, the count resets before staging — the first application
        // (count 0) gets multiplier 1, the nth (n>=1) gets DrStageMultipliers[min(n-1, length-1)].
        // Regardless of whether the application succeeds or fizzles, the count is always incremented and
        // lastAppliedTick refreshed (an immune application also extends the window).
        private BigNum ApplyDrHistory(EffectTarget target, CompiledEffectSpec spec)
        {
            var index = target.FindOrCreateDrHistory(spec.DrCategory.Index);
            ref var entry = ref target.DrHistoryAt(index);

            if (entry.LastAppliedTick + spec.DrWindowTicks < CurrentTick)
            {
                entry.AppliedCount = 0;
            }

            var stage = entry.AppliedCount;
            var multiplier = stage == 0
                ? BigNum.One
                : spec.DrStageMultipliers[Math.Min(stage - 1, spec.DrStageMultipliers.Length - 1)];

            entry.AppliedCount++;
            entry.LastAppliedTick = CurrentTick;
            return multiplier;
        }

        private void ApplyInstant(
            CompiledEffectSpec spec, EffectTarget target, EffectTarget? source, bool hasSource, in PendingApply pending)
        {
            var modifiers = spec.Modifiers;
            for (var i = 0; i < modifiers.Length; i++)
            {
                var modifier = modifiers[i];
                var magnitude = EvaluateMagnitude(modifier, target, source, hasSource, pending.Level, stack: 1);
                ApplyModifierToBase(target, modifier, magnitude);
            }

            var executions = spec.Executions;
            for (var i = 0; i < executions.Length; i++)
            {
                RunExecution(executions[i], target, source, hasSource, pending.Source, pending.Target, pending.Level, stack: 1);
            }

            FireChain(spec.Chains, ChainTrigger.OnApplication, pending.Source, pending.Level, pending.Target, target);
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
                    // Unreachable: EffectCatalogBuilder forces Op to Add for Instant/periodic specs.
                    throw new InvalidOperationException($"Instant modifiers only allow Add: {modifier.Op}");
            }
        }

        private void RunExecution(
            CompiledExecution execution, EffectTarget target, EffectTarget? source, bool hasSource,
            TargetId sourceId, TargetId targetId, int level, int stack)
        {
            // ponytail: input counts are authoring-spec scale, so stackalloc suffices — switch to a heap array if large inputs become a real need.
            Span<BigNum> inputs = stackalloc BigNum[execution.Inputs.Length];
            for (var i = 0; i < execution.Inputs.Length; i++)
            {
                inputs[i] = EvaluateOperand(execution.Inputs[i], target, source, hasSource);
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
            CompiledChain[] chains, ChainTrigger trigger, TargetId source, int level,
            TargetId originTargetId, EffectTarget originTarget)
        {
            for (var i = 0; i < chains.Length; i++)
            {
                var chain = chains[i];
                if (chain.Trigger != trigger) continue;

                var hasSource = _resolver.TryResolve(source, out var sourceTarget) && sourceTarget is not null;
                if (!EvaluateConditions(chain.Conditions, originTarget, sourceTarget, hasSource)) continue;

                var chainLevel = chain.LevelRule == ChainLevelRule.Inherit ? level : chain.FixedLevel;

                if (chain.Selector is null)
                {
                    EnqueueApply(chain.EffectId, source, originTargetId, chainLevel);
                    continue;
                }

                // ponytail: target counts are authoring-spec scale, so a 32-slot stack buffer suffices — on overflow only the first 32 are applied.
                Span<TargetId> targets = stackalloc TargetId[32];
                var selectorCtx = new SelectorContext(source, chain.SelectorParams, _rng);
                var count = chain.Selector.Select(in selectorCtx, targets);
                for (var k = 0; k < count; k++)
                {
                    EnqueueApply(chain.EffectId, source, targets[k], chainLevel);
                }
            }
        }

        // Duration/Infinite: if the target has an active instance with the same SpecId, merge via the
        // stack policy; otherwise create a new instance, attach modifiers, and grant tags.
        // precomputedDurationTicks is the duration (with DR composed) that ProcessPendingApply computed
        // before RemoveOnApply (-1 when DR is unused or a merge target exists) and CreateInstance uses it
        // as-is — prevents a duplicate ApplyDrHistory call.
        private void ApplyDurationOrInfinite(
            CompiledEffectSpec spec, EffectTarget target, in PendingApply pending, int precomputedDurationTicks)
        {
            var existing = FindActiveBySpec(target, pending.SpecId);
            if (existing != null)
            {
                MergeReapply(spec, target, existing);
                return;
            }

            CreateInstance(spec, target, in pending, precomputedDurationTicks);
        }

        private static EffectInstance? FindActiveBySpec(EffectTarget target, int specId)
        {
            var active = target.ActiveEffects;
            for (var i = 0; i < active.Count; i++)
            {
                if (active[i].SpecId == specId) return active[i];
            }

            return null;
        }

        // Reapply merge: AddStack raises the stack and routes the overflow to HandleStackOverflow; both
        // Refresh and AddStack reset duration/period timers per policy. StackChanged is raised only when
        // the stack value actually changes. A merge is not a new application, so no OnApplication chain fires here.
        private void MergeReapply(CompiledEffectSpec spec, EffectTarget target, EffectInstance instance)
        {
            var stackPolicy = spec.Stack;
            if (stackPolicy.OnReapply == StackReapply.AddStack)
            {
                // Build guarantees MaxStack > 0 for the AddStack policy.
                var wouldBe = instance.Stack + stackPolicy.AddStackCount;
                if (wouldBe > stackPolicy.MaxStack)
                {
                    HandleStackOverflow(spec, target, instance);
                }
                else if (wouldBe != instance.Stack)
                {
                    instance.Stack = wouldBe;
                    target.RaiseEffectEvent(new EffectLifecycleEvent(
                        EffectLifecycleKind.StackChanged, instance.Id, instance.SpecId, instance.Stack));
                    MarkDirtyForModifiers(target, spec);
                    SyncLevelFromStack(spec, target, instance);
                }
            }
            else if (stackPolicy.OnReapply == StackReapply.ExtendCapped)
            {
                // Extends duration only, without touching the stack — cap = new duration × ExtendCapMultiplier.
                // "New duration" includes DurationPerLevel/DurationScale (DR applies to new instances only).
                var newDuration = ComputeMergedDurationTicks(spec, target, instance);
                var cap = ToTicksFloor((BigNum)newDuration * stackPolicy.ExtendCapMultiplier);
                var extended = instance.RemainingTicks + newDuration;
                instance.RemainingTicks = extended < cap ? extended : cap;
            }

            if (stackPolicy.OnReapply != StackReapply.ExtendCapped
                && stackPolicy.RefreshDurationOnReapply && spec.DurationType == EffectDurationType.Duration)
            {
                instance.RemainingTicks = ComputeMergedDurationTicks(spec, target, instance);
            }

            if (stackPolicy.ResetPeriodOnReapply && spec.PeriodTicks > 0)
            {
                instance.PeriodCountdown = spec.PeriodTicks;
            }
        }

        // LevelFromStack — after Stack changes, syncs Level and re-evaluates/re-attaches the modifiers
        // that were snapshotted at apply time (periodic effects are excluded: they evaluate Level directly each period).
        private void SyncLevelFromStack(CompiledEffectSpec spec, EffectTarget target, EffectInstance instance)
        {
            if (!spec.Stack.LevelFromStack || instance.Level == instance.Stack) return;

            instance.Level = instance.Stack;
            if (spec.PeriodTicks > 0) return;

            target.Attributes.DetachModifiers(instance);
            var hasSource = _resolver.TryResolve(instance.Source, out var source) && source is not null;
            var modifiers = spec.Modifiers;
            for (var i = 0; i < modifiers.Length; i++)
            {
                var modifier = modifiers[i];
                var magnitude = EvaluateMagnitude(modifier, target, source, hasSource, instance.Level, instance.Stack);
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
        private void HandleStackOverflow(CompiledEffectSpec spec, EffectTarget target, EffectInstance instance)
        {
            FireChain(spec.Chains, ChainTrigger.OnStackOverflow, instance.Source, instance.Level, target.Id, target);

            if (spec.Stack.OnOverflow == StackOverflow.ApplyEffect)
            {
                EnqueueApply(spec.OverflowEffectId, instance.Source, target.Id, instance.Level);
            }

            var newStack = spec.Stack.ClearStacksOnOverflow ? 1 : spec.Stack.MaxStack;
            if (newStack != instance.Stack)
            {
                instance.Stack = newStack;
                target.RaiseEffectEvent(new EffectLifecycleEvent(
                    EffectLifecycleKind.StackChanged, instance.Id, instance.SpecId, instance.Stack));
                MarkDirtyForModifiers(target, spec);
                SyncLevelFromStack(spec, target, instance);
            }
        }

        // New instance: rent → insert in ascending Id order → attach modifiers (unless periodic) → grant
        // GrantedTags → Applied event → fire OnApplication chains. Periodic effects (PeriodTicks > 0) only
        // adjust Base each period via the same path as Instant and never attach modifiers persistently.
        // For Duration specs with DR, ProcessPendingApply computed the duration (and immunity) before
        // RemoveOnApply and passed it as precomputedDurationTicks — this method uses that value as-is and
        // never calls ApplyDrHistory again (prevents double history mutation). Duration without DR is
        // computed here (clamped to a minimum of 1 tick).
        private void CreateInstance(
            CompiledEffectSpec spec, EffectTarget target, in PendingApply pending, int precomputedDurationTicks)
        {
            var hasSource = _resolver.TryResolve(pending.Source, out var source) && source is not null;

            var remainingTicks = -1;
            if (spec.DurationType == EffectDurationType.Duration)
            {
                if (spec.DrCategory.IsValid)
                {
                    // If DR-immune (0 ticks or less), ProcessPendingApply already dropped silently and
                    // returned, so reaching this path guarantees precomputedDurationTicks > 0.
                    remainingTicks = precomputedDurationTicks;
                }
                else
                {
                    var value = ComputeScaledDuration(spec, target, source, hasSource, pending.Level, stack: 1);
                    var ticks = ToTicksFloor(value);
                    remainingTicks = ticks <= 0 ? 1 : ticks;   // minimum 1 tick.
                }
            }

            var periodCountdown = spec.PeriodTicks > 0 ? spec.PeriodTicks : -1;
            var instance = EffectInstance.Rent(
                _nextInstanceId++, pending.SpecId, pending.Source, pending.Level, stack: 1,
                remainingTicks, periodCountdown, CurrentTick);
            target.InsertActive(instance);

            if (spec.PeriodTicks == 0)
            {
                var modifiers = spec.Modifiers;
                for (var i = 0; i < modifiers.Length; i++)
                {
                    var modifier = modifiers[i];
                    var magnitude = EvaluateMagnitude(modifier, target, source, hasSource, pending.Level, instance.Stack);
                    target.Attributes.AttachModifier(instance, i, modifier.AttributeId, modifier.Op, magnitude, modifier.ScaleWithStack);
                }
            }

            var grantedTags = spec.GrantedTags;
            for (var i = 0; i < grantedTags.Length; i++)
            {
                target.Tags.Add(grantedTags[i]);
            }

            target.RaiseEffectEvent(new EffectLifecycleEvent(EffectLifecycleKind.Applied, instance.Id, pending.SpecId, instance.Stack));

            FireChain(spec.Chains, ChainTrigger.OnApplication, pending.Source, pending.Level, target.Id, target);
        }

        private static void MarkDirtyForModifiers(EffectTarget target, CompiledEffectSpec spec)
        {
            var modifiers = spec.Modifiers;
            for (var i = 0; i < modifiers.Length; i++)
            {
                target.Attributes.MarkDirty(modifiers[i].AttributeId);
            }
        }

        // Phase 2: iterate targets in TargetId order and instances in Id order (the active list is already
        // canonical). Instances created this tick are skipped. Period firing runs before the expiry check,
        // so the final period fire on the expiring tick is still applied. Normal expiry (the
        // complete-removal path of ExpireInstance) fires OnCompleteNormal chains, and those apply requests
        // carry over to the next tick since this tick's phase 1 has already passed.
        private void AdvanceTime()
        {
            var targetIds = _resolver.TargetIds;
            for (var t = 0; t < targetIds.Count; t++)
            {
                if (!_resolver.TryResolve(targetIds[t], out var target) || target is null) continue;

                var active = target.ActiveEffects;
                _expiryScratch.Clear();
                for (var i = 0; i < active.Count; i++)
                {
                    var instance = active[i];
                    if (instance.CreatedTick == CurrentTick) continue;   // created this tick; starts advancing next tick

                    var spec = _catalog.GetSpec(instance.SpecId);

                    if (spec.PeriodTicks > 0)
                    {
                        instance.PeriodCountdown--;
                        if (instance.PeriodCountdown == 0)
                        {
                            FirePeriodic(spec, target, instance);
                            instance.PeriodCountdown = spec.PeriodTicks;
                        }
                    }

                    if (spec.DurationType == EffectDurationType.Duration)
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
        private void FirePeriodic(CompiledEffectSpec spec, EffectTarget target, EffectInstance instance)
        {
            var hasSource = _resolver.TryResolve(instance.Source, out var source) && source is not null;

            var modifiers = spec.Modifiers;
            for (var i = 0; i < modifiers.Length; i++)
            {
                var modifier = modifiers[i];
                var magnitude = EvaluateMagnitude(modifier, target, source, hasSource, instance.Level, instance.Stack);
                ApplyModifierToBase(target, modifier, magnitude);
            }

            var executions = spec.Executions;
            for (var i = 0; i < executions.Length; i++)
            {
                RunExecution(executions[i], target, source, hasSource, instance.Source, target.Id, instance.Level, instance.Stack);
            }
        }

        // Expiry: with the RemoveOneAndRefresh stack policy and stacks remaining, drop one stack and reset
        // the duration (not a normal completion, so no OnCompleteNormal fires). Otherwise remove fully via
        // RemoveInstanceCompletely and fire the OnCompleteNormal chain. Premature removal via
        // RemoveByTags/RemoveById takes the same helper with OnCompletePrematurely (RemoveInstancePrematurely).
        private void ExpireInstance(EffectTarget target, EffectInstance instance)
        {
            var spec = _catalog.GetSpec(instance.SpecId);
            if (spec.Stack.OnExpiration == StackExpiration.RemoveOneAndRefresh && instance.Stack > 1)
            {
                instance.Stack--;
                instance.RemainingTicks = spec.DurationTicks;
                target.RaiseEffectEvent(new EffectLifecycleEvent(
                    EffectLifecycleKind.StackChanged, instance.Id, instance.SpecId, instance.Stack));
                MarkDirtyForModifiers(target, spec);
                return;
            }

            RemoveInstanceCompletely(target, instance, spec, EffectLifecycleKind.Expired, ChainTrigger.OnCompleteNormal);
        }

        // Shared complete-removal path — used by normal expiry (ExpireInstance) and premature removal
        // (RemoveByTags/RemoveById). Detaches modifiers, reclaims GrantedTags, removes from the active
        // list, returns the instance to the pool, then raises the eventKind lifecycle event and fires the
        // chainTrigger chains. The firing instance's Source and Level are inherited as-is.
        private void RemoveInstanceCompletely(
            EffectTarget target, EffectInstance instance, CompiledEffectSpec spec,
            EffectLifecycleKind eventKind, ChainTrigger chainTrigger)
        {
            var source = instance.Source;
            var level = instance.Level;

            target.Attributes.DetachModifiers(instance);
            var grantedTags = spec.GrantedTags;
            for (var i = 0; i < grantedTags.Length; i++)
            {
                target.Tags.Remove(grantedTags[i]);
            }

            target.RemoveActive(instance);
            target.RaiseEffectEvent(new EffectLifecycleEvent(eventKind, instance.Id, instance.SpecId, instance.Stack));
            EffectInstance.Return(instance);

            FireChain(spec.Chains, chainTrigger, source, level, target.Id, target);
        }

        // Phase 3: first rebuild — re-aggregates only the dirty slots marked by phases 1-2.
        private void RebuildAll() => RebuildDirtyAttributes();

        // Phase 5: second rebuild — re-aggregates only the dirty slots newly marked by the phase-4 Ongoing
        // toggles. Effectively a RebuildDirty re-run; does nothing when nothing is dirty.
        private void RebuildToggled() => RebuildDirtyAttributes();

        private void RebuildDirtyAttributes()
        {
            var targetIds = _resolver.TargetIds;
            for (var t = 0; t < targetIds.Count; t++)
            {
                if (_resolver.TryResolve(targetIds[t], out var target) && target is not null)
                {
                    target.Attributes.RebuildDirty();
                }
            }
        }

        // Phase 4: evaluates only instances with Ongoing conditions, targets in TargetId order, instances
        // in Id order (the active list is already canonical). Evaluation uses the Current values rebuilt
        // in phase 3 and runs once per tick. Toggles only when the result changes — OFF reclaims
        // GrantedTags and marks modifiers dirty, ON grants GrantedTags and marks dirty. The instance
        // itself is not removed (RebuildDirty's RebuildSlot skips modifiers of !Enabled instances, so
        // leaving them attached still excludes them from aggregation). GrantedTags is a counted container,
        // so Add/Remove are symmetric.
        private void EvaluateOngoing()
        {
            var targetIds = _resolver.TargetIds;
            for (var t = 0; t < targetIds.Count; t++)
            {
                if (!_resolver.TryResolve(targetIds[t], out var target) || target is null) continue;

                var active = target.ActiveEffects;
                for (var i = 0; i < active.Count; i++)
                {
                    var instance = active[i];
                    var spec = _catalog.GetSpec(instance.SpecId);
                    if (spec.OngoingConditions.Length == 0) continue;

                    var satisfied = EvaluateConditions(spec.OngoingConditions, target, source: null, hasSource: false);
                    if (satisfied == instance.Enabled) continue;

                    instance.Enabled = satisfied;
                    var grantedTags = spec.GrantedTags;
                    if (satisfied)
                    {
                        for (var g = 0; g < grantedTags.Length; g++) target.Tags.Add(grantedTags[g]);
                    }
                    else
                    {
                        for (var g = 0; g < grantedTags.Length; g++) target.Tags.Remove(grantedTags[g]);
                    }

                    MarkDirtyForModifiers(target, spec);
                }
            }
        }
    }
}
