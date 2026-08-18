#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Effects
{
    /// <summary>효과 스펙을 수집한 뒤 Build에서 일괄 검증·컴파일하는 빌더입니다.</summary>
    public sealed class EffectCatalogBuilder
    {
        private readonly List<EffectSpec> _specs = new List<EffectSpec>();

        /// <summary>효과 스펙을 등록합니다. 검증은 Build에서 일괄 수행합니다.</summary>
        /// <param name="spec">등록할 효과 스펙입니다.</param>
        /// <exception cref="ArgumentNullException"><paramref name="spec"/>이 null인 경우입니다.</exception>
        public void Add(EffectSpec spec)
        {
            if (spec is null) throw new ArgumentNullException(nameof(spec));
            _specs.Add(spec);
        }

        /// <summary>등록된 스펙들을 검증하고 컴파일해 효과 카탈로그를 만듭니다.</summary>
        /// <param name="tags">태그 해석에 사용할 카탈로그입니다.</param>
        /// <param name="seams">CalcTag/SelectorTag 해석에 사용할 시섬 레지스트리입니다.</param>
        /// <param name="attributes">Operand의 속성 참조 검증에 사용할 속성 레지스트리입니다.</param>
        /// <returns>구축된 효과 카탈로그입니다.</returns>
        /// <exception cref="InvalidOperationException">스펙 §10의 검증 규칙(1~9) 중 하나라도 위반한 경우입니다.</exception>
        public EffectCatalog Build(TagCatalog tags, SeamRegistry seams, AttributeRegistry attributes)
        {
            // 1패스: 이름 -> id. 전방 참조(뒤에 나오는 효과로의 체인)를 허용하기 위해 먼저 전부 채운다.
            var nameToId = new Dictionary<string, int>(_specs.Count, StringComparer.Ordinal);
            for (var i = 0; i < _specs.Count; i++)
            {
                var name = _specs[i].Name;
                if (string.IsNullOrEmpty(name))
                {
                    throw new InvalidOperationException($"효과 이름은 비어 있을 수 없습니다(등록 순번 {i}).");
                }

                if (!nameToId.TryAdd(name, i))
                {
                    throw new InvalidOperationException($"효과 이름이 중복되었습니다: {name}");
                }
            }

            // 2패스: 스펙별 해석·검증·컴파일.
            var compiled = new CompiledEffectSpec[_specs.Count];
            for (var i = 0; i < _specs.Count; i++)
            {
                compiled[i] = CompileSpec(_specs[i], _specs, nameToId, tags, seams, attributes);
            }

            var warnings = DetectChainCycles(compiled);
            DetectLevelFromStackScaleWithStackWarnings(_specs, warnings);
            return new EffectCatalog(nameToId, compiled, warnings);
        }

        private static CompiledEffectSpec CompileSpec(
            EffectSpec spec, List<EffectSpec> allSpecs, Dictionary<string, int> nameToId,
            TagCatalog tags, SeamRegistry seams, AttributeRegistry attributes)
        {
            ValidateDurationTypeFields(spec);
            ValidateExecutionEligibility(spec);
            ValidateStackOverflowConsistency(spec);
            ValidateInstantOrPeriodicModifierOps(spec);
            ValidateStackVocabConsistency(spec);
            ValidateDurationScaleFields(spec);
            ValidateDrFields(spec);

            var grantedTags = ResolveTags(spec.GrantedTags, spec.Name, tags, "부여");
            var assetTags = ResolveTags(spec.AssetTags, spec.Name, tags, "자산");
            var immunityTags = ResolveTags(spec.ImmunityTags, spec.Name, tags, "면역");
            var removeOnApplyTags = ResolveTags(spec.RemoveOnApplyTags, spec.Name, tags, "RemoveOnApply");

            var modifiers = new CompiledModifier[spec.Modifiers.Count];
            for (var i = 0; i < spec.Modifiers.Count; i++)
            {
                var m = spec.Modifiers[i];
                if (!attributes.Contains(m.AttributeId))
                {
                    throw new InvalidOperationException(
                        $"효과 '{spec.Name}': 미등록 속성 {m.AttributeId}을(를) 참조합니다.");
                }

                var (magBase, perLevel, calc, byLevel, tail, increment) =
                    ResolveMagnitude(m.Magnitude, spec, tags, seams, attributes);
                modifiers[i] = new CompiledModifier(
                    m.AttributeId, m.Op, magBase, perLevel, calc, byLevel, tail, increment, m.ScaleWithStack);
            }

            var executions = new CompiledExecution[spec.Executions.Count];
            for (var i = 0; i < spec.Executions.Count; i++)
            {
                var e = spec.Executions[i];
                var calc = ResolveExecutionCalc(e.CalcTag, spec.Name, tags, seams);
                var inputs = new Operand[e.Inputs.Count];
                for (var j = 0; j < e.Inputs.Count; j++)
                {
                    ValidateOperandAttribute(e.Inputs[j], spec.Name, attributes);
                    inputs[j] = e.Inputs[j];
                }

                executions[i] = new CompiledExecution(calc, inputs);
            }

            var applicationConditions = ResolveConditions(
                spec.ApplicationConditions, spec.Name, attributes, allowSourceAttribute: true);
            var ongoingConditions = ResolveConditions(
                spec.OngoingConditions, spec.Name, attributes, allowSourceAttribute: false);

            var chains = new CompiledChain[spec.Chains.Count];
            for (var i = 0; i < spec.Chains.Count; i++)
            {
                chains[i] = ResolveChain(spec.Chains[i], spec, allSpecs, nameToId, tags, seams, attributes);
            }

            var overflowEffectId = -1;
            if (spec.Stack.OnOverflow == StackOverflow.ApplyEffect)
            {
                var overflowName = spec.Stack.OverflowEffectName;
                if (string.IsNullOrEmpty(overflowName) || !nameToId.TryGetValue(overflowName!, out overflowEffectId))
                {
                    throw new InvalidOperationException(
                        $"효과 '{spec.Name}': OverflowEffectName을 해석할 수 없습니다: {overflowName}");
                }
            }

            CompiledMagnitude? chanceToApply = null;
            if (spec.ChanceToApply != null)
            {
                var (chanceBase, chancePerLevel, chanceCalc, chanceByLevel, chanceTail, chanceIncrement) =
                    ResolveMagnitude(spec.ChanceToApply, spec, tags, seams, attributes);
                chanceToApply = new CompiledMagnitude(
                    chanceBase, chancePerLevel, chanceCalc, chanceByLevel, chanceTail, chanceIncrement);
            }

            var durationPerLevel = spec.DurationPerLevel?.ToArray();

            CompiledMagnitude? durationScale = null;
            if (spec.DurationScale != null)
            {
                var (scaleBase, scalePerLevel, scaleCalc, scaleByLevel, scaleTail, scaleIncrement) =
                    ResolveMagnitude(spec.DurationScale, spec, tags, seams, attributes);
                durationScale = new CompiledMagnitude(
                    scaleBase, scalePerLevel, scaleCalc, scaleByLevel, scaleTail, scaleIncrement);
            }

            var drCategory = GameplayTag.None;
            if (!string.IsNullOrEmpty(spec.DrCategory) && !tags.TryGet(spec.DrCategory!, out drCategory))
            {
                throw new InvalidOperationException(
                    $"효과 '{spec.Name}': DR 계열 태그를 해석할 수 없습니다: {spec.DrCategory}");
            }

            var drStageMultipliers = spec.DrStageMultipliers.Count > 0
                ? spec.DrStageMultipliers.ToArray()
                : Array.Empty<BigNum>();

            return new CompiledEffectSpec(
                spec.Name, spec.DurationType, spec.DurationTicks, spec.PeriodTicks, spec.Stack,
                modifiers, executions, applicationConditions, ongoingConditions,
                grantedTags, assetTags, immunityTags, chains, overflowEffectId,
                removeOnApplyTags, chanceToApply, durationPerLevel, durationScale,
                drCategory, spec.DrWindowTicks, drStageMultipliers);
        }

        // 규칙 2·3: DurationType별 필드 제약.
        private static void ValidateDurationTypeFields(EffectSpec spec)
        {
            switch (spec.DurationType)
            {
                case EffectDurationType.Instant:
                    if (spec.DurationTicks != 0 || spec.PeriodTicks != 0)
                    {
                        throw new InvalidOperationException(
                            $"효과 '{spec.Name}': Instant는 DurationTicks/PeriodTicks가 0이어야 합니다.");
                    }

                    if (spec.OngoingConditions.Count != 0)
                    {
                        throw new InvalidOperationException(
                            $"효과 '{spec.Name}': Instant는 OngoingConditions를 가질 수 없습니다.");
                    }

                    if (spec.GrantedTags.Count != 0)
                    {
                        throw new InvalidOperationException(
                            $"효과 '{spec.Name}': Instant는 GrantedTags를 가질 수 없습니다.");
                    }

                    if (spec.Stack.MaxStack != 0)
                    {
                        throw new InvalidOperationException(
                            $"효과 '{spec.Name}': Instant는 Stack.MaxStack이 0이어야 합니다.");
                    }

                    break;

                case EffectDurationType.Duration:
                    // G3: DurationPerLevel이 있으면 DurationTicks는 대신 쓰이지 않는(0인) 상태여야
                    // 한다 — 상호 배타 규칙은 ValidateDurationScaleFields가 검증한다.
                    if (spec.DurationPerLevel == null && spec.DurationTicks <= 0)
                    {
                        throw new InvalidOperationException(
                            $"효과 '{spec.Name}': Duration은 DurationTicks가 0보다 커야 합니다(또는 DurationPerLevel을 쓰세요).");
                    }

                    break;

                case EffectDurationType.Infinite:
                    if (spec.DurationTicks != 0)
                    {
                        throw new InvalidOperationException(
                            $"효과 '{spec.Name}': Infinite는 DurationTicks가 0이어야 합니다.");
                    }

                    break;
            }
        }

        // 규칙 4: Executions는 Instant 또는 PeriodTicks > 0에서만 허용.
        private static void ValidateExecutionEligibility(EffectSpec spec)
        {
            if (spec.Executions.Count > 0
                && spec.DurationType != EffectDurationType.Instant
                && spec.PeriodTicks <= 0)
            {
                throw new InvalidOperationException(
                    $"효과 '{spec.Name}': Executions는 Instant이거나 PeriodTicks > 0일 때만 허용됩니다.");
            }
        }

        // 규칙: Instant 또는 주기 실행(PeriodTicks > 0) 스펙의 수정자는 Add만 허용한다.
        // Multiply/Override는 ΣMulPct·override 집계(Duration/Infinite 무주기 전용) 의미라
        // 인스턴스 없이 즉시 적용되는 경로엔 없다 — 퍼센트 증감은 Add + 자기참조 피연산자
        // (예: Operand.Attribute(id, -0.3) = "현재 값의 30% 감소")로 표현하는 것과 등가다.
        private static void ValidateInstantOrPeriodicModifierOps(EffectSpec spec)
        {
            if (spec.DurationType != EffectDurationType.Instant && spec.PeriodTicks <= 0) return;

            for (var i = 0; i < spec.Modifiers.Count; i++)
            {
                if (spec.Modifiers[i].Op != AttributeModifierOp.Add)
                {
                    throw new InvalidOperationException(
                        $"효과 '{spec.Name}': Instant/주기 실행 수정자는 Add만 허용됩니다 — " +
                        "퍼센트 증감은 Add + 자기참조 피연산자(예: Operand.Attribute(id, -0.3))로 표현하세요.");
                }
            }
        }

        // 규칙 5: 스택 없음(MaxStack == 0)인데 스택 전용 정책이 설정된 경우 거부.
        private static void ValidateStackOverflowConsistency(EffectSpec spec)
        {
            if (spec.Stack.MaxStack == 0
                && (spec.Stack.OnOverflow == StackOverflow.ApplyEffect
                    || spec.Stack.OnReapply == StackReapply.AddStack))
            {
                throw new InvalidOperationException(
                    $"효과 '{spec.Name}': Stack.MaxStack이 0이면 ApplyEffect/AddStack 정책을 쓸 수 없습니다.");
            }
        }

        // G4·G5: LevelFromStack/ExtendCapped 어휘의 자체 정합 규칙.
        private static void ValidateStackVocabConsistency(EffectSpec spec)
        {
            if (spec.Stack.LevelFromStack && spec.Stack.MaxStack == 0)
            {
                throw new InvalidOperationException(
                    $"효과 '{spec.Name}': LevelFromStack은 Stack.MaxStack이 0보다 커야 합니다.");
            }

            if (spec.Stack.OnReapply == StackReapply.ExtendCapped)
            {
                if (spec.DurationType != EffectDurationType.Duration)
                {
                    throw new InvalidOperationException(
                        $"효과 '{spec.Name}': StackReapply.ExtendCapped는 DurationType이 Duration이어야 합니다.");
                }

                if (spec.Stack.ExtendCapMultiplier < BigNum.One)
                {
                    throw new InvalidOperationException(
                        $"효과 '{spec.Name}': Stack.ExtendCapMultiplier는 1 이상이어야 합니다.");
                }
            }
        }

        // G3: DurationPerLevel·DurationScale 정합 규칙 — 둘 다 Duration 전용이고, DurationPerLevel은
        // DurationTicks와 상호 배타(있으면 DurationTicks는 0)이며 MaxLevel·길이가 맞아야 한다.
        private static void ValidateDurationScaleFields(EffectSpec spec)
        {
            if (spec.DurationPerLevel != null)
            {
                if (spec.DurationType != EffectDurationType.Duration)
                {
                    throw new InvalidOperationException(
                        $"효과 '{spec.Name}': DurationPerLevel은 DurationType이 Duration이어야 합니다.");
                }

                if (spec.DurationTicks != 0)
                {
                    throw new InvalidOperationException(
                        $"효과 '{spec.Name}': DurationPerLevel은 DurationTicks와 함께 쓸 수 없습니다(상호 배타).");
                }

                if (spec.MaxLevel < 1)
                {
                    throw new InvalidOperationException(
                        $"효과 '{spec.Name}': DurationPerLevel은 MaxLevel >= 1 선언이 필요합니다.");
                }

                if (spec.DurationPerLevel.Count != spec.MaxLevel)
                {
                    throw new InvalidOperationException(
                        $"효과 '{spec.Name}': DurationPerLevel 길이({spec.DurationPerLevel.Count})가 "
                        + $"MaxLevel({spec.MaxLevel})과 일치해야 합니다.");
                }
            }

            if (spec.DurationScale != null && spec.DurationType != EffectDurationType.Duration)
            {
                throw new InvalidOperationException(
                    $"효과 '{spec.Name}': DurationScale은 DurationType이 Duration이어야 합니다.");
            }
        }

        // G6: DR 필드 정합 규칙 — DrCategory가 있으면 Duration 전용, 창은 1틱 이상, 단계 배수는
        // 비어있지 않고 전부 0 이상이어야 한다.
        private static void ValidateDrFields(EffectSpec spec)
        {
            if (string.IsNullOrEmpty(spec.DrCategory)) return;

            if (spec.DurationType != EffectDurationType.Duration)
            {
                throw new InvalidOperationException(
                    $"효과 '{spec.Name}': DrCategory는 DurationType이 Duration이어야 합니다.");
            }

            if (spec.DrWindowTicks < 1)
            {
                throw new InvalidOperationException(
                    $"효과 '{spec.Name}': DrCategory가 있으면 DrWindowTicks는 1 이상이어야 합니다.");
            }

            if (spec.DrStageMultipliers.Count == 0)
            {
                throw new InvalidOperationException(
                    $"효과 '{spec.Name}': DrCategory가 있으면 DrStageMultipliers가 비어있을 수 없습니다.");
            }

            for (var i = 0; i < spec.DrStageMultipliers.Count; i++)
            {
                if (spec.DrStageMultipliers[i].Sign < 0)
                {
                    throw new InvalidOperationException(
                        $"효과 '{spec.Name}': DrStageMultipliers는 0 이상이어야 합니다(인덱스 {i}).");
                }
            }
        }

        // 라이더(T2 리뷰): LevelFromStack 스펙에 ScaleWithStack=true인 Modifier가 있으면 레벨 커브 ×
        // 스택 배수가 복합 적용된다 — 오류는 아니지만(의도된 콘텐츠일 수 있음) 저작자가 인지하도록
        // Build 경고에 쌓는다.
        private static void DetectLevelFromStackScaleWithStackWarnings(List<EffectSpec> specs, List<string> warnings)
        {
            for (var i = 0; i < specs.Count; i++)
            {
                var spec = specs[i];
                if (!spec.Stack.LevelFromStack) continue;

                for (var m = 0; m < spec.Modifiers.Count; m++)
                {
                    if (!spec.Modifiers[m].ScaleWithStack) continue;
                    warnings.Add(
                        $"[warn] 효과 '{spec.Name}': LevelFromStack과 ScaleWithStack=true가 함께 켜져 있어 "
                        + "레벨 커브 × 스택 배수가 복합 적용됩니다.");
                    break;
                }
            }
        }

        // 규칙 6: 일반 분류 태그는 카탈로그에서만 해석.
        private static GameplayTag[] ResolveTags(List<string> paths, string specName, TagCatalog tags, string what)
        {
            var result = new GameplayTag[paths.Count];
            for (var i = 0; i < paths.Count; i++)
            {
                if (!tags.TryGet(paths[i], out result[i]))
                {
                    throw new InvalidOperationException(
                        $"효과 '{specName}': {what} 태그를 해석할 수 없습니다: {paths[i]}");
                }
            }

            return result;
        }

        // 규칙 6·9: MagnitudeDef 표기(Base(+PerLevel) | PerLevelValues | Formula | CurveKeys | CalcTag) 중
        // 정확히 하나. 레벨 테이블 표기(②③④)는 spec.MaxLevel >= 1 필수 — 스펙 §15.1.
        private static (
            Operand? Base, Operand? PerLevel, IMagnitudeCalc? Calc,
            BigNum[]? ByLevel, LevelTail Tail, BigNum Increment) ResolveMagnitude(
            MagnitudeDef def, EffectSpec spec, TagCatalog tags, SeamRegistry seams, AttributeRegistry attributes)
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
                    $"효과 '{spec.Name}': MagnitudeDef는 Base(+PerLevel)·PerLevelValues·Formula·CurveKeys·"
                    + "CalcTag 중 정확히 하나만 가져야 합니다.");
            }

            if (!hasBase && def.PerLevel.HasValue)
            {
                throw new InvalidOperationException(
                    $"효과 '{spec.Name}': PerLevel은 Base가 있을 때만 사용할 수 있습니다.");
            }

            if (hasCalc)
            {
                var calc = ResolveMagnitudeCalc(def.CalcTag!, spec.Name, tags, seams);
                return (null, null, calc, null, LevelTail.Clamp, BigNum.Zero);
            }

            if (hasBase)
            {
                ValidateOperandAttribute(def.Base!.Value, spec.Name, attributes);
                if (def.PerLevel.HasValue)
                {
                    ValidateOperandAttribute(def.PerLevel.Value, spec.Name, attributes);
                }

                return (def.Base, def.PerLevel, null, null, LevelTail.Clamp, BigNum.Zero);
            }

            if (spec.MaxLevel < 1)
            {
                throw new InvalidOperationException(
                    $"효과 '{spec.Name}': PerLevelValues/Formula/CurveKeys는 MaxLevel >= 1 선언이 필요합니다.");
            }

            var byLevel = hasPerLevelValues
                ? CompilePerLevelValues(def.PerLevelValues!, spec)
                : hasFormula
                    ? CompileFormula(def.Formula!, spec)
                    : CompileCurveKeys(def.CurveKeys!, spec);

            var tail = def.Tail;
            var increment = def.ExtrapolateIncrement;
            if (tail == LevelTail.Extrapolate && increment.IsZero && byLevel.Length > 1)
            {
                increment = byLevel[byLevel.Length - 1] - byLevel[byLevel.Length - 2];
            }

            return (null, null, null, byLevel, tail, increment);
        }

        // 표기 ②: 명시 배열 — 길이가 곧 MaxLevel과 일치해야 한다.
        private static BigNum[] CompilePerLevelValues(List<BigNum> values, EffectSpec spec)
        {
            if (values.Count != spec.MaxLevel)
            {
                throw new InvalidOperationException(
                    $"효과 '{spec.Name}': PerLevelValues 길이({values.Count})가 MaxLevel({spec.MaxLevel})과 일치해야 합니다.");
            }

            return values.ToArray();
        }

        // 표기 ③: 결정론 수식 — 레벨 1..MaxLevel을 각각 x에 대입해 저작/Build 시점에 사전 평가한다.
        private static BigNum[] CompileFormula(string formula, EffectSpec spec)
        {
            if (!BigNumFormula.TryValidate(formula, out var validationError))
            {
                throw new InvalidOperationException($"효과 '{spec.Name}': Formula가 유효하지 않습니다 — {validationError}");
            }

            var byLevel = new BigNum[spec.MaxLevel];
            for (var level = 1; level <= spec.MaxLevel; level++)
            {
                if (!BigNumFormula.TryEvaluate(formula, level, out byLevel[level - 1]))
                {
                    throw new InvalidOperationException(
                        $"효과 '{spec.Name}': Formula 평가에 실패했습니다(레벨 {level}): {formula}");
                }
            }

            return byLevel;
        }

        // 표기 ④: 희소 키 + 선형 보간 — 레벨 오름차순·중복 금지·첫 키 레벨 1. 키 사이는 선형 보간,
        // 마지막 키 뒤는 마지막 값(그 이후 Tail 정책은 런타임 평가 헬퍼가 담당).
        private static BigNum[] CompileCurveKeys(List<LevelKey> keys, EffectSpec spec)
        {
            if (keys.Count == 0 || keys[0].Level != 1)
            {
                throw new InvalidOperationException($"효과 '{spec.Name}': CurveKeys의 첫 키는 레벨 1이어야 합니다.");
            }

            for (var i = 1; i < keys.Count; i++)
            {
                if (keys[i].Level <= keys[i - 1].Level)
                {
                    throw new InvalidOperationException(
                        $"효과 '{spec.Name}': CurveKeys는 레벨 오름차순이어야 하며 중복을 허용하지 않습니다.");
                }
            }

            if (keys[keys.Count - 1].Level > spec.MaxLevel)
            {
                throw new InvalidOperationException(
                    $"효과 '{spec.Name}': CurveKeys의 마지막 키 레벨({keys[keys.Count - 1].Level})이 "
                    + $"MaxLevel({spec.MaxLevel})을 초과합니다.");
            }

            var byLevel = new BigNum[spec.MaxLevel];
            var keyIndex = 0;
            for (var level = 1; level <= spec.MaxLevel; level++)
            {
                while (keyIndex + 1 < keys.Count && keys[keyIndex + 1].Level <= level) keyIndex++;

                var k0 = keys[keyIndex];
                if (keyIndex + 1 >= keys.Count)
                {
                    byLevel[level - 1] = k0.Value;   // 마지막 키 뒤(또는 정확히 마지막 키)
                    continue;
                }

                var k1 = keys[keyIndex + 1];
                byLevel[level - 1] = k0.Value
                                      + (k1.Value - k0.Value) * (level - k0.Level) / (k1.Level - k0.Level);
            }

            return byLevel;
        }

        private static IMagnitudeCalc ResolveMagnitudeCalc(
            string calcTag, string specName, TagCatalog tags, SeamRegistry seams)
        {
            if (!tags.TryGet(calcTag, out var tag) || !seams.TryGetMagnitudeCalc(tag, out var calc) || calc is null)
            {
                throw new InvalidOperationException(
                    $"효과 '{specName}': 크기 계산 태그를 해석할 수 없습니다: {calcTag}");
            }

            return calc;
        }

        private static IExecutionCalc ResolveExecutionCalc(
            string calcTag, string specName, TagCatalog tags, SeamRegistry seams)
        {
            if (!tags.TryGet(calcTag, out var tag) || !seams.TryGetExecutionCalc(tag, out var calc) || calc is null)
            {
                throw new InvalidOperationException(
                    $"효과 '{specName}': 효과 실행 태그를 해석할 수 없습니다: {calcTag}");
            }

            return calc;
        }

        private static ITargetSelector ResolveTargetSelector(
            string selectorTag, string specName, TagCatalog tags, SeamRegistry seams)
        {
            if (!tags.TryGet(selectorTag, out var tag)
                || !seams.TryGetTargetSelector(tag, out var selector)
                || selector is null)
            {
                throw new InvalidOperationException(
                    $"효과 '{specName}': 대상 선택 태그를 해석할 수 없습니다: {selectorTag}");
            }

            return selector;
        }

        // 규칙 7: Operand의 속성 참조 검증. Constant는 검사 대상이 아니다.
        private static void ValidateOperandAttribute(Operand operand, string specName, AttributeRegistry attributes)
        {
            if (operand.Kind != OperandKind.Constant && !attributes.Contains(operand.AttributeId))
            {
                throw new InvalidOperationException(
                    $"효과 '{specName}': 미등록 속성 {operand.AttributeId}을(를) 참조합니다.");
            }
        }

        // 규칙 7: OngoingConditions는 SourceAttribute 피연산자를 가질 수 없다.
        private static CompiledCondition[] ResolveConditions(
            List<ConditionDef> defs, string specName, AttributeRegistry attributes, bool allowSourceAttribute)
        {
            var result = new CompiledCondition[defs.Count];
            for (var i = 0; i < defs.Count; i++)
            {
                var c = defs[i];
                if (!allowSourceAttribute
                    && (c.Left.Kind == OperandKind.SourceAttribute || c.Right.Kind == OperandKind.SourceAttribute))
                {
                    throw new InvalidOperationException(
                        $"효과 '{specName}': OngoingConditions는 SourceAttribute 피연산자를 가질 수 없습니다.");
                }

                ValidateOperandAttribute(c.Left, specName, attributes);
                ValidateOperandAttribute(c.Right, specName, attributes);
                result[i] = new CompiledCondition(c.Left, c.Op, c.Right);
            }

            return result;
        }

        // 규칙 6·8: SelectorTag(있으면)와 EffectName 해석. LevelRule.Fixed는 대상 스펙에 MaxLevel이
        // 선언된 경우 FixedLevel이 그 MaxLevel을 넘지 않는지도 검증한다.
        private static CompiledChain ResolveChain(
            ChainEdgeDef edge, EffectSpec ownerSpec, List<EffectSpec> allSpecs, Dictionary<string, int> nameToId,
            TagCatalog tags, SeamRegistry seams, AttributeRegistry attributes)
        {
            if (string.IsNullOrEmpty(edge.EffectName) || !nameToId.TryGetValue(edge.EffectName, out var effectId))
            {
                throw new InvalidOperationException(
                    $"효과 '{ownerSpec.Name}': 체인 대상 효과를 해석할 수 없습니다: {edge.EffectName}");
            }

            if (edge.LevelRule == ChainLevelRule.Fixed)
            {
                var targetMaxLevel = allSpecs[effectId].MaxLevel;
                if (targetMaxLevel > 0 && edge.FixedLevel > targetMaxLevel)
                {
                    throw new InvalidOperationException(
                        $"효과 '{ownerSpec.Name}': 체인 대상 '{edge.EffectName}'의 FixedLevel({edge.FixedLevel})이 "
                        + $"MaxLevel({targetMaxLevel})을 초과합니다.");
                }
            }

            ITargetSelector? selector = null;
            if (!string.IsNullOrEmpty(edge.SelectorTag))
            {
                selector = ResolveTargetSelector(edge.SelectorTag!, ownerSpec.Name, tags, seams);
            }

            var conditions = ResolveConditions(edge.Conditions, ownerSpec.Name, attributes, allowSourceAttribute: true);
            return new CompiledChain(
                edge.Trigger, effectId, selector, edge.SelectorParams.ToArray(),
                conditions, edge.LevelRule, edge.FixedLevel);
        }

        // 규칙 10: OnApplication 엣지만 따라가는 DFS 회색/검정 채색으로 순환을 찾는다.
        // 순환 경유 스펙 중 Duration/Period를 가진 것이 있으면 "low", 없으면 "high".
        private static List<string> DetectChainCycles(CompiledEffectSpec[] compiled)
        {
            var warnings = new List<string>();
            var adjacency = new List<int>[compiled.Length];
            for (var i = 0; i < compiled.Length; i++)
            {
                var list = new List<int>();
                foreach (var chain in compiled[i].Chains)
                {
                    if (chain.Trigger == ChainTrigger.OnApplication)
                    {
                        list.Add(chain.EffectId);
                    }
                }

                adjacency[i] = list;
            }

            var color = new byte[compiled.Length];   // 0=백색, 1=회색(스택 위), 2=검정(완료)
            var stack = new List<int>();

            void Visit(int node)
            {
                color[node] = 1;
                stack.Add(node);
                foreach (var next in adjacency[node])
                {
                    if (color[next] == 1)
                    {
                        var startIndex = stack.IndexOf(next);
                        var cycleNodes = stack.GetRange(startIndex, stack.Count - startIndex);
                        var hasDurationOrPeriod = cycleNodes.Exists(
                            n => compiled[n].DurationTicks > 0 || compiled[n].PeriodTicks > 0);
                        var severity = hasDurationOrPeriod ? "low" : "high";
                        var names = string.Join(" -> ", cycleNodes.ConvertAll(n => compiled[n].Name));
                        warnings.Add($"[{severity}] 체인 순환: {names} -> {compiled[next].Name}");
                    }
                    else if (color[next] == 0)
                    {
                        Visit(next);
                    }
                }

                stack.RemoveAt(stack.Count - 1);
                color[node] = 2;
            }

            for (var i = 0; i < compiled.Length; i++)
            {
                if (color[i] == 0)
                {
                    Visit(i);
                }
            }

            return warnings;
        }
    }
}
