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
                compiled[i] = CompileSpec(_specs[i], nameToId, tags, seams, attributes);
            }

            var warnings = DetectChainCycles(compiled);
            return new EffectCatalog(nameToId, compiled, warnings);
        }

        private static CompiledEffectSpec CompileSpec(
            EffectSpec spec, Dictionary<string, int> nameToId,
            TagCatalog tags, SeamRegistry seams, AttributeRegistry attributes)
        {
            ValidateDurationTypeFields(spec);
            ValidateExecutionEligibility(spec);
            ValidateStackOverflowConsistency(spec);
            ValidateInstantOrPeriodicModifierOps(spec);

            var grantedTags = ResolveTags(spec.GrantedTags, spec.Name, tags, "부여");
            var assetTags = ResolveTags(spec.AssetTags, spec.Name, tags, "자산");
            var immunityTags = ResolveTags(spec.ImmunityTags, spec.Name, tags, "면역");

            var modifiers = new CompiledModifier[spec.Modifiers.Count];
            for (var i = 0; i < spec.Modifiers.Count; i++)
            {
                var m = spec.Modifiers[i];
                if (!attributes.Contains(m.AttributeId))
                {
                    throw new InvalidOperationException(
                        $"효과 '{spec.Name}': 미등록 속성 {m.AttributeId}을(를) 참조합니다.");
                }

                var (magBase, perLevel, calc) = ResolveMagnitude(m.Magnitude, spec.Name, tags, seams, attributes);
                modifiers[i] = new CompiledModifier(m.AttributeId, m.Op, magBase, perLevel, calc, m.ScaleWithStack);
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
                chains[i] = ResolveChain(spec.Chains[i], spec.Name, nameToId, tags, seams, attributes);
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

            return new CompiledEffectSpec(
                spec.Name, spec.DurationType, spec.DurationTicks, spec.PeriodTicks, spec.Stack,
                modifiers, executions, applicationConditions, ongoingConditions,
                grantedTags, assetTags, immunityTags, chains, overflowEffectId);
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
                    if (spec.DurationTicks <= 0)
                    {
                        throw new InvalidOperationException(
                            $"효과 '{spec.Name}': Duration은 DurationTicks가 0보다 커야 합니다.");
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

        // 규칙 6·9: MagnitudeDef의 CalcTag XOR Base, PerLevel은 Base 있을 때만.
        // 규칙 7: Base/PerLevel의 속성 참조 검증.
        private static (Operand? Base, Operand? PerLevel, IMagnitudeCalc? Calc) ResolveMagnitude(
            MagnitudeDef def, string specName, TagCatalog tags, SeamRegistry seams, AttributeRegistry attributes)
        {
            var hasCalc = !string.IsNullOrEmpty(def.CalcTag);
            var hasBase = def.Base.HasValue;
            if (hasCalc == hasBase)
            {
                throw new InvalidOperationException(
                    $"효과 '{specName}': MagnitudeDef는 CalcTag 또는 Base 중 하나만 가져야 합니다.");
            }

            if (!hasBase && def.PerLevel.HasValue)
            {
                throw new InvalidOperationException(
                    $"효과 '{specName}': PerLevel은 Base가 있을 때만 사용할 수 있습니다.");
            }

            if (hasCalc)
            {
                var calc = ResolveMagnitudeCalc(def.CalcTag!, specName, tags, seams);
                return (null, null, calc);
            }

            ValidateOperandAttribute(def.Base!.Value, specName, attributes);
            if (def.PerLevel.HasValue)
            {
                ValidateOperandAttribute(def.PerLevel.Value, specName, attributes);
            }

            return (def.Base, def.PerLevel, null);
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

        // 규칙 6·8: SelectorTag(있으면)와 EffectName 해석.
        private static CompiledChain ResolveChain(
            ChainEdgeDef edge, string specName, Dictionary<string, int> nameToId,
            TagCatalog tags, SeamRegistry seams, AttributeRegistry attributes)
        {
            if (string.IsNullOrEmpty(edge.EffectName) || !nameToId.TryGetValue(edge.EffectName, out var effectId))
            {
                throw new InvalidOperationException(
                    $"효과 '{specName}': 체인 대상 효과를 해석할 수 없습니다: {edge.EffectName}");
            }

            ITargetSelector? selector = null;
            if (!string.IsNullOrEmpty(edge.SelectorTag))
            {
                selector = ResolveTargetSelector(edge.SelectorTag!, specName, tags, seams);
            }

            var conditions = ResolveConditions(edge.Conditions, specName, attributes, allowSourceAttribute: true);
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
