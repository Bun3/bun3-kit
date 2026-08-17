#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;

namespace Bun3.Gameplay.Effects
{
    /// <summary>
    /// 효과 적용 큐를 드레인하고 Instant 경로를 처리하는 파이프라인입니다.
    /// 이 단계는 페이즈 ① (드레인·면역·적용조건·Instant 적용)만 구현합니다 — 주기 실행·만료·
    /// 지속 조건 재평가 등 나머지 페이즈는 후속 확장에서 채워집니다.
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
        private ulong _nextInstanceId = 1;

        /// <summary>대상 해석 실패로 조용히 드롭된 적용 요청 수입니다.</summary>
        internal long UnresolvedTargetDropCount { get; private set; }

        /// <summary>면역 태그로 차단된 적용 요청 수입니다.</summary>
        internal long ImmuneDropCount { get; private set; }

        /// <summary>적용 조건 미충족으로 차단된 적용 요청 수입니다.</summary>
        internal long ConditionDropCount { get; private set; }

        /// <summary>지금까지 처리된 틱 수입니다.</summary>
        public long CurrentTick { get; private set; }

        /// <summary>효과 카탈로그·대상 리졸버·난수 생성기와 틱당 적용 예산으로 파이프라인을 만듭니다.</summary>
        /// <param name="catalog">스펙 조회에 쓸 효과 카탈로그입니다.</param>
        /// <param name="resolver">TargetId를 EffectTarget으로 바꾸는 리졸버입니다.</param>
        /// <param name="rng">실행 계산에 전달할 난수 생성기입니다.</param>
        /// <param name="applyBudgetPerTick">한 틱에 드레인할 최대 적용 요청 수입니다.</param>
        public EffectPipeline(EffectCatalog catalog, IEffectTargetResolver resolver, IRng rng, int applyBudgetPerTick = 64)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            if (applyBudgetPerTick <= 0)
                throw new ArgumentOutOfRangeException(nameof(applyBudgetPerTick), "틱당 적용 예산은 1 이상이어야 합니다.");
            _applyBudgetPerTick = applyBudgetPerTick;
        }

        /// <summary>효과 적용 요청을 큐에 적재합니다. 실제 적용은 다음 <see cref="Tick"/>에서 처리됩니다.</summary>
        /// <param name="specId">적용할 효과 스펙 id입니다.</param>
        /// <param name="source">시전자 대상 식별자입니다.</param>
        /// <param name="target">적용받을 대상 식별자입니다.</param>
        /// <param name="level">효과 레벨입니다.</param>
        public void EnqueueApply(int specId, TargetId source, TargetId target, int level = 1)
        {
            _queue.Enqueue(new PendingApply { SpecId = specId, Source = source, Target = target, Level = level });
        }

        /// <summary>한 틱을 처리합니다. 적용 큐를 예산만큼 드레인합니다.</summary>
        public void Tick()
        {
            var budget = _applyBudgetPerTick;
            while (budget > 0 && _queue.Count > 0)
            {
                budget--;
                var pending = _queue.Dequeue();
                ProcessPendingApply(in pending);
            }

            CurrentTick++;
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

            if (!EvaluateApplicationConditions(spec, target, source, hasSource, pending.Level))
            {
                ConditionDropCount++;
                return;
            }

            if (spec.DurationType == EffectDurationType.Instant)
            {
                ApplyInstant(spec, target, source, hasSource, in pending);
            }
            else
            {
                ApplyDurationOrInfinite(spec, target, in pending);
            }
        }

        // 대상의 활성 인스턴스가 가진 면역 태그가 신규 스펙의 자산 태그를 (자신 포함) 조상으로 갖는지 검사한다.
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

        private static bool EvaluateApplicationConditions(
            CompiledEffectSpec spec, EffectTarget target, EffectTarget? source, bool hasSource, int level)
        {
            var conditions = spec.ApplicationConditions;
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

        // Constant→값, Attribute→대상 Current×계수, SourceAttribute→소스 Current×계수(소스 미해석 시 0).
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
            CompiledModifier modifier, EffectTarget target, EffectTarget? source, bool hasSource, int level, int stack)
        {
            if (modifier.Calc != null)
            {
                var ctx = new MagnitudeContext(target, source, hasSource, level, stack, CurrentTick);
                return modifier.Calc.Calculate(in ctx);
            }

            var value = EvaluateOperand(modifier.Base!.Value, target, source, hasSource);
            if (modifier.PerLevel.HasValue)
            {
                value += EvaluateOperand(modifier.PerLevel.Value, target, source, hasSource) * (level - 1);
            }

            return value;
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
                RunExecution(executions[i], target, source, hasSource, in pending);
            }
        }

        // Instant는 인스턴스가 없으므로 수정자가 즉시 Base에 반영되는 영구 변경으로 해석한다.
        // Multiply/Override는 ΣMulPct·override 집계(Duration/Infinite 전용) 의미라 Instant엔 없다 —
        // 퍼센트 증감은 Add + 자기참조 피연산자(예: Operand.Attribute(Hp, -0.3))로 표현한다.
        private static void ApplyModifierToBase(EffectTarget target, CompiledModifier modifier, BigNum magnitude)
        {
            switch (modifier.Op)
            {
                case AttributeModifierOp.Add:
                    target.Attributes.AddBase(modifier.AttributeId, magnitude);
                    break;
                default:
                    // EffectCatalogBuilder가 Instant/주기 실행 스펙의 Op를 Add로 강제하므로 도달하지 않는다.
                    throw new InvalidOperationException($"Instant 수정자는 Add만 허용됩니다: {modifier.Op}");
            }
        }

        private void RunExecution(
            CompiledExecution execution, EffectTarget target, EffectTarget? source, bool hasSource, in PendingApply pending)
        {
            // ponytail: 입력 개수는 저작 스펙 규모라 stackalloc으로 충분 — 대량 입력이 실제로 필요해지면 힙 배열로 교체.
            Span<BigNum> inputs = stackalloc BigNum[execution.Inputs.Length];
            for (var i = 0; i < execution.Inputs.Length; i++)
            {
                inputs[i] = EvaluateOperand(execution.Inputs[i], target, source, hasSource);
            }

            var ctx = new ExecutionContext(
                this, target, source, hasSource, pending.Source, pending.Target,
                pending.Level, stack: 1, CurrentTick, inputs, _rng);
            execution.Calc.Execute(ref ctx);
        }

        // Duration/Infinite: 인스턴스를 만들어 활성 목록에 꽂고 Applied 이벤트를 낸다.
        // 수정자 부착·주기 실행·지속 조건 재평가는 후속 확장에서 채워진다.
        private void ApplyDurationOrInfinite(CompiledEffectSpec spec, EffectTarget target, in PendingApply pending)
        {
            var remainingTicks = spec.DurationType == EffectDurationType.Duration ? spec.DurationTicks : -1;
            var periodCountdown = spec.PeriodTicks > 0 ? spec.PeriodTicks : -1;
            var instance = EffectInstance.Rent(
                _nextInstanceId++, pending.SpecId, pending.Source, pending.Level, stack: 1, remainingTicks, periodCountdown);
            target.InsertActive(instance);
            target.RaiseEffectEvent(new EffectLifecycleEvent(EffectLifecycleKind.Applied, instance.Id, pending.SpecId, instance.Stack));
        }
    }
}
