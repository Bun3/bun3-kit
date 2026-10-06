// EffectPipeline partial — phase 1 — draining the apply queue: immunity, chance, conditions, DR, instant execution, instance creation and reapply merges.
#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;

namespace Bun3.Gameplay.Effects
{
    public sealed partial class EffectPipeline
    {
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

            Track(target);
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
            Track(target);
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
            CatchUp(target);
            target.NextDueTick = long.MinValue;
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
                    RaiseEffectEvent(new EffectLifecycleEvent(
                        EffectLifecycleKind.StackChanged, target.Id, instance.Id, instance.EffectId, instance.Stack, instance.Source, instance.Level));
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
                RaiseEffectEvent(new EffectLifecycleEvent(
                    EffectLifecycleKind.StackChanged, target.Id, instance.Id, instance.EffectId, instance.Stack, instance.Source, instance.Level));
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

            RaiseEffectEvent(new EffectLifecycleEvent(EffectLifecycleKind.Applied, target.Id, instance.Id, pending.EffectId, instance.Stack, instance.Source, instance.Level));

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
    }
}
