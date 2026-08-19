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
    /// Compiles authoring-time magnitude, seam-reference, and tag-reference declarations into their
    /// runtime forms. Shared by the effect and ability catalog builders — both use the same
    /// <see cref="MagnitudeDef"/> vocabulary, so the notation rules live in one place.
    /// </summary>
    internal static class MagnitudeCompiler
    {
        // Exactly one MagnitudeDef notation: Base(+PerLevel) | PerLevelValues | Formula | CurveKeys | CalcTag.
        // Level-table notations require maxLevel >= 1.
        internal static (
            Operand? Base, Operand? PerLevel, IMagnitudeCalc? Calc, GameplayTag CalcTagIndex,
            BigNum[]? ByLevel, LevelTail Tail, BigNum Increment) Resolve(
            MagnitudeDef def, string ownerName, int maxLevel, TagCatalog tags, SeamRegistry seams, AttributeRegistry attributes)
        {
            var hasCalc = !string.IsNullOrEmpty(def.CalcTag);
            var hasBase = def.Base.HasValue;
            var hasPerLevelValues = def.PerLevelValues != null;
            var hasFormula = !string.IsNullOrEmpty(def.Formula);
            var hasCurveKeys = def.CurveKeys != null;
            var formCount = (hasCalc ? 1 : 0) + (hasBase ? 1 : 0) + (hasPerLevelValues ? 1 : 0)
                             + (hasFormula ? 1 : 0) + (hasCurveKeys ? 1 : 0);
            if (formCount != 1)
            {
                throw new InvalidOperationException(
                    $"{ownerName}: MagnitudeDef must have exactly one of Base(+PerLevel), "
                    + "PerLevelValues, Formula, CurveKeys, or CalcTag.");
            }

            if (!hasBase && def.PerLevel.HasValue)
            {
                throw new InvalidOperationException(
                    $"{ownerName}: PerLevel can only be used together with Base.");
            }

            if (hasCalc)
            {
                var calc = ResolveMagnitudeCalc(def.CalcTag!, ownerName, tags, seams, out var calcTag);
                return (null, null, calc, calcTag, null, LevelTail.Clamp, BigNum.Zero);
            }

            if (hasBase)
            {
                ValidateOperandAttribute(def.Base!.Value, ownerName, attributes);
                if (def.PerLevel.HasValue)
                {
                    ValidateOperandAttribute(def.PerLevel.Value, ownerName, attributes);
                }

                return (def.Base, def.PerLevel, null, GameplayTag.None, null, LevelTail.Clamp, BigNum.Zero);
            }

            if (maxLevel < 1)
            {
                throw new InvalidOperationException(
                    $"{ownerName}: PerLevelValues/Formula/CurveKeys require MaxLevel >= 1 to be declared.");
            }

            var byLevel = hasPerLevelValues
                ? CompilePerLevelValues(def.PerLevelValues!, ownerName, maxLevel)
                : hasFormula
                    ? CompileFormula(def.Formula!, ownerName, maxLevel)
                    : CompileCurveKeys(def.CurveKeys!, ownerName, maxLevel);

            var tail = def.Tail;
            var increment = def.ExtrapolateIncrement;
            if (tail == LevelTail.Extrapolate && increment.IsZero && byLevel.Length > 1)
            {
                increment = byLevel[byLevel.Length - 1] - byLevel[byLevel.Length - 2];
            }

            return (null, null, null, GameplayTag.None, byLevel, tail, increment);
        }

        private static BigNum[] CompilePerLevelValues(List<BigNum> values, string ownerName, int maxLevel)
        {
            if (values.Count != maxLevel)
            {
                throw new InvalidOperationException(
                    $"{ownerName}: PerLevelValues length ({values.Count}) must match MaxLevel ({maxLevel}).");
            }

            return values.ToArray();
        }

        // Deterministic formula — pre-evaluated at Build time by substituting levels 1..MaxLevel for x.
        private static BigNum[] CompileFormula(string formula, string ownerName, int maxLevel)
        {
            if (!BigNumFormula.TryCompile(formula, out var compiled, out var validationError))
            {
                throw new InvalidOperationException($"{ownerName}: Formula is invalid — {validationError}");
            }

            var byLevel = new BigNum[maxLevel];
            for (var level = 1; level <= maxLevel; level++)
            {
                if (!compiled!.TryEvaluate(level, out byLevel[level - 1]))
                {
                    throw new InvalidOperationException(
                        $"{ownerName}: Formula evaluation failed (level {level}): {formula}");
                }
            }

            return byLevel;
        }

        // Sparse keys + linear interpolation — keys in ascending level order, no duplicates, first key at
        // level 1. Values between keys interpolate linearly; beyond the last key the last value holds
        // (the Tail policy past MaxLevel is handled by the runtime evaluation helper).
        private static BigNum[] CompileCurveKeys(List<LevelKey> keys, string ownerName, int maxLevel)
        {
            if (keys.Count == 0 || keys[0].Level != 1)
            {
                throw new InvalidOperationException($"{ownerName}: the first CurveKeys key must be level 1.");
            }

            for (var i = 1; i < keys.Count; i++)
            {
                if (keys[i].Level <= keys[i - 1].Level)
                {
                    throw new InvalidOperationException(
                        $"{ownerName}: CurveKeys must be in ascending level order without duplicates.");
                }
            }

            if (keys[keys.Count - 1].Level > maxLevel)
            {
                throw new InvalidOperationException(
                    $"{ownerName}: the last CurveKeys key level ({keys[keys.Count - 1].Level}) "
                    + $"exceeds MaxLevel ({maxLevel}).");
            }

            var byLevel = new BigNum[maxLevel];
            var keyIndex = 0;
            for (var level = 1; level <= maxLevel; level++)
            {
                while (keyIndex + 1 < keys.Count && keys[keyIndex + 1].Level <= level) keyIndex++;

                var k0 = keys[keyIndex];
                if (keyIndex + 1 >= keys.Count)
                {
                    byLevel[level - 1] = k0.Value;   // at or beyond the last key
                    continue;
                }

                var k1 = keys[keyIndex + 1];
                byLevel[level - 1] = k0.Value
                                      + (k1.Value - k0.Value) * (level - k0.Level) / (k1.Level - k0.Level);
            }

            return byLevel;
        }

        internal static IMagnitudeCalc ResolveMagnitudeCalc(
            string calcTag, string ownerName, TagCatalog tags, SeamRegistry seams, out GameplayTag tag)
        {
            if (!tags.TryGet(calcTag, out tag) || !seams.TryGetMagnitudeCalc(tag, out var calc) || calc is null)
            {
                throw new InvalidOperationException(
                    $"{ownerName}: cannot resolve magnitude calc tag: {calcTag}");
            }

            return calc;
        }

        internal static IExecutionCalc ResolveExecutionCalc(
            string calcTag, string ownerName, TagCatalog tags, SeamRegistry seams, out GameplayTag tag)
        {
            if (!tags.TryGet(calcTag, out tag) || !seams.TryGetExecutionCalc(tag, out var calc) || calc is null)
            {
                throw new InvalidOperationException(
                    $"{ownerName}: cannot resolve execution calc tag: {calcTag}");
            }

            return calc;
        }

        internal static ITargetSelector ResolveTargetSelector(
            string selectorTag, string ownerName, TagCatalog tags, SeamRegistry seams, out GameplayTag tag)
        {
            if (!tags.TryGet(selectorTag, out tag)
                || !seams.TryGetTargetSelector(tag, out var selector)
                || selector is null)
            {
                throw new InvalidOperationException(
                    $"{ownerName}: cannot resolve target selector tag: {selectorTag}");
            }

            return selector;
        }

        internal static GameplayTag[] ResolveTags(List<string> paths, string ownerName, TagCatalog tags, string what)
        {
            var result = new GameplayTag[paths.Count];
            for (var i = 0; i < paths.Count; i++)
            {
                if (!tags.TryGet(paths[i], out result[i]))
                {
                    throw new InvalidOperationException(
                        $"{ownerName}: cannot resolve {what} tag: {paths[i]}");
                }
            }

            return result;
        }

        // Operand attribute reference validation; Constant and EventMagnitude carry no attribute id and are exempt.
        internal static void ValidateOperandAttribute(Operand operand, string ownerName, AttributeRegistry attributes)
        {
            if ((operand.Kind == OperandKind.Attribute || operand.Kind == OperandKind.SourceAttribute)
                && !attributes.Contains(operand.AttributeId))
            {
                throw new InvalidOperationException(
                    $"{ownerName}: references unregistered attribute {operand.AttributeId}.");
            }
        }
    }
}
