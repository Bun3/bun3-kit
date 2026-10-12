// EffectPipeline partial — phase 2: due-tick time advance, lazy catch-up and target ownership.
#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Numerics;

namespace Bun3.Gameplay.Effects
{
    public sealed partial class EffectPipeline
    {
        private long _lastAdvancedTick = long.MinValue;

        /// <summary>Sets the clock between ticks, as a snapshot restore does: phase 2 last ran on the previous tick.</summary>
        internal void RestoreClock(long currentTick)
        {
            CurrentTick = currentTick;
            _lastAdvancedTick = long.MinValue;
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
                if (target.Owner == this && target.NextDueTick > CurrentTick) continue;

                AdvanceVisitCount++;
                Track(target);
                CatchUp(target);
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

                target.AdvancedThrough = CurrentTick;
                target.NextDueTick = ComputeNextDueTick(target, CurrentTick);
            }
        }

        /// <summary>Number of targets phase 2 processed — targets with nothing due are skipped.</summary>
        internal long AdvanceVisitCount { get; private set; }

        // Last tick whose time step (phase 2) has run.
        private long AdvancedThroughTick => _lastAdvancedTick == CurrentTick ? CurrentTick : CurrentTick - 1;

        /// <summary>Applies the time steps a skipped target still owes, up to the last advanced tick. Before its
        /// due tick a target only counts down, so subtracting the skipped steps at once is exact. An instance
        /// created on a later tick than the baseline owes only the steps after its creation tick.</summary>
        internal void CatchUp(EffectTarget target)
        {
            var advancedThrough = AdvancedThroughTick;
            var baseline = target.AdvancedThrough;
            if (baseline != EffectTarget.UnknownTick && baseline >= advancedThrough) return;   // already ahead, e.g. advanced earlier in this time step
            target.AdvancedThrough = advancedThrough;
            if (baseline == EffectTarget.UnknownTick) return;

            var timed = target.TimedEffects;
            for (var i = 0; i < timed.Count; i++)
            {
                var instance = timed[i];
                var owed = advancedThrough - Math.Max(baseline, instance.CreatedTick);
                if (owed <= 0) continue;
                var effect = _catalog.GetEffect(instance.EffectId);
                if (effect.PeriodTicks > 0) instance.PeriodCountdown -= (int)owed;
                if (effect.DurationType == EffectDurationType.Duration) instance.RemainingTicks -= (int)owed;
            }
        }

        // Earliest tick on which a timed instance fires (countdown reaches 0) or expires (remaining reaches 0
        // or below), counted from the later of the current tick and the instance's creation tick.
        private long ComputeNextDueTick(EffectTarget target, long tick)
        {
            var due = long.MaxValue;
            var timed = target.TimedEffects;
            for (var i = 0; i < timed.Count; i++)
            {
                var instance = timed[i];
                var effect = _catalog.GetEffect(instance.EffectId);
                var from = Math.Max(tick, instance.CreatedTick);
                if (effect.PeriodTicks > 0 && instance.PeriodCountdown > 0) due = Math.Min(due, from + instance.PeriodCountdown);
                if (effect.DurationType == EffectDurationType.Duration) due = Math.Min(due, from + Math.Max(1, instance.RemainingTicks));
            }

            return due;
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
                instance.RemainingTicks = effect.DurationTicks;
                instance.DurationTicks = instance.RemainingTicks;
                SetStack(effect, target, instance, instance.Stack - 1, BigNum.Zero);
                return;
            }

            RemoveInstanceCompletely(target, instance, effect, EffectLifecycleKind.Expired, ChainTrigger.OnCompleteNormal);
        }

        /// <summary>Makes this pipeline the target's owner: it advances the target's time and records its
        /// attribute changes. Time owed under a previous owner is settled first and the baseline restarts here;
        /// instances created on another clock count as already existing, so they advance on the next time step.</summary>
        internal void Track(EffectTarget target)
        {
            if (target.Owner == this) return;
            target.SyncTime();
            target.Owner = this;
            target.ResetTimeBaseline();
            target.Attributes.AttachChangeLog(_attributeChanges, target.Id);
            var active = target.ActiveEffects;
            for (var i = 0; i < active.Count; i++)
            {
                if (active[i].CreatedTick >= CurrentTick) active[i].CreatedTick = CurrentTick - 1;
            }
        }

        /// <summary>Releases a target this pipeline owns, settling its time first. Time stops until it is tracked again.</summary>
        internal void Untrack(EffectTarget target)
        {
            if (target.Owner != this) return;
            CatchUp(target);
            target.Owner = null;
            target.ResetTimeBaseline();
            target.Attributes.AttachChangeLog(null, default);
        }
    }
}
