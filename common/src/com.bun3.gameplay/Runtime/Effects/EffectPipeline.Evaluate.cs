// EffectPipeline partial — conditions, operands, magnitudes and duration scaling.
#nullable enable
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;

namespace Bun3.Gameplay.Effects
{
    public sealed partial class EffectPipeline
    {
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
    }
}
