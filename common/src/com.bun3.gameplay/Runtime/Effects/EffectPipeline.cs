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
    /// 효과 적용 큐를 드레인하고 Instant/Duration/Infinite 경로·체인·Ongoing 토글까지 처리하는
    /// 파이프라인입니다. 한 틱은 여섯 페이즈로 진행됩니다 — ①<see cref="DrainApplications"/>(예산·면역·
    /// 적용조건·적용, OnApplication/OnStackOverflow 체인 발화) → ②<see cref="AdvanceTime"/>(주기 발화·수명
    /// 감소·만료, OnCompleteNormal 체인 발화) → ③<see cref="RebuildAll"/>(①②가 표시한 dirty 재집계) →
    /// ④<see cref="EvaluateOngoing"/>(지속 조건 토글) → ⑤<see cref="RebuildToggled"/>(④가 표시한 dirty
    /// 재집계) → ⑥이벤트 확정(별도 동작 없음 — 생애주기/속성 변경 이벤트 버퍼는 게임이 직접 드레인).
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

        /// <summary>대상 해석 실패로 조용히 드롭된 적용 요청 수입니다.</summary>
        internal long UnresolvedTargetDropCount { get; private set; }

        /// <summary>면역 태그로 차단된 적용 요청 수입니다.</summary>
        internal long ImmuneDropCount { get; private set; }

        /// <summary>적용 조건 미충족으로 차단된 적용 요청 수입니다.</summary>
        internal long ConditionDropCount { get; private set; }

        /// <summary>ChanceToApply 롤 실패로 조용히 무산된 적용 요청 수입니다.</summary>
        internal long ChanceDropCount { get; private set; }

        /// <summary>G6 DR 면역(단계 배수 합성 결과 지속 0틱 이하)으로 조용히 무산된 적용 요청 수입니다.</summary>
        internal long DrImmuneDropCount { get; private set; }

        // G2: [0,1) 결정론 롤 스케일 — _rng.NextUInt32()의 치역(2^32)을 BigNum 정수 비교로 다루기 위한 상수.
        private static readonly BigNum RngRollRange = (BigNum)4_294_967_296L;

        /// <summary>지금까지 처리된 틱 수입니다. internal 세터는 스냅샷 복원 재생 결정론 전용입니다.</summary>
        public long CurrentTick { get; internal set; }

        /// <summary>다음에 발급할 인스턴스 id입니다. 스냅샷 복원 후 재생 결정론을 맞추려면 스냅샷
        /// 시점의 값을 저장해뒀다가 복원 시 그대로 되돌려야 합니다(대기 큐가 비어있는 시점에서만
        /// 안전 — 대기 큐 항목이 있으면 그 항목이 발급받을 id까지 함께 재현해야 하므로 범위 밖입니다).</summary>
        internal ulong NextInstanceId
        {
            get => _nextInstanceId;
            set => _nextInstanceId = value;
        }

        /// <summary>예산 초과로 이번 틱에 드레인되지 못하고 큐에 남은 적용 요청 수입니다.</summary>
        public int PendingApplyCount => _queue.Count;

        /// <summary>효과 카탈로그·대상 리졸버·난수 생성기와 틱당 적용 예산으로 파이프라인을 만듭니다.</summary>
        /// <param name="catalog">스펙 조회에 쓸 효과 카탈로그입니다.</param>
        /// <param name="resolver">TargetId를 EffectTarget으로 바꾸는 리졸버입니다.</param>
        /// <param name="rng">실행 계산·대상 선택에 전달할 난수 생성기입니다.</param>
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

        /// <summary>한 틱을 여섯 페이즈 순서로 처리합니다. 클래스 문서에 각 페이즈의 순서·역할이 정리되어 있습니다.</summary>
        public void Tick()
        {
            DrainApplications();
            AdvanceTime();
            RebuildAll();
            EvaluateOngoing();
            RebuildToggled();
            // ⑥ 이벤트 확정: 이 페이즈에서 파이프라인이 하는 동작은 없다. 생애주기 이벤트
            // (EffectTarget.PendingEffectEvents)와 속성 변경 이벤트(AttributeSet.PendingChanges)는
            // 게임(호출자)이 이번 틱 처리가 끝난 뒤 직접 읽고 Clear*로 드레인한다.

            CurrentTick++;
        }

        /// <summary>
        /// 대상의 활성 인스턴스 중 query의 태그 하나라도 인스턴스 스펙의 자산 태그를 자신-또는-조상으로 갖는
        /// 것을 전부 제거합니다(Id 오름차순). 수정자 분리·GrantedTags 회수·활성 목록에서의 제거는 이 호출
        /// 안에서 즉시 일어나지만, 그로 인한 속성 Current 재반영은 다음 틱의 재계산 페이즈까지 미뤄집니다.
        /// 제거는 만료와 같은 정리 경로를 타되 <see cref="EffectLifecycleKind.RemovedPrematurely"/> 이벤트와
        /// <see cref="ChainTrigger.OnCompletePrematurely"/> 체인을 발화합니다(OnCompleteNormal은 발화하지 않습니다).
        /// </summary>
        /// <param name="target">대상 식별자입니다.</param>
        /// <param name="query">디스펠 질의 태그 컨테이너입니다.</param>
        /// <returns>제거된 인스턴스 수입니다. 대상이 해석되지 않으면 0입니다.</returns>
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

        /// <summary>대상의 활성 인스턴스 하나를 id로 제거합니다. 수정자 해제는 이 호출 안에서 즉시
        /// 일어나지만 속성 Current 반영은 다음 틱 재계산 페이즈입니다. 제거 경로·이벤트·체인은
        /// <see cref="RemoveByTags"/>와 같습니다(<see cref="ChainTrigger.OnCompletePrematurely"/>).</summary>
        /// <param name="target">대상 식별자입니다.</param>
        /// <param name="instanceId">제거할 인스턴스 id입니다.</param>
        /// <returns>인스턴스를 찾아 제거했으면 <see langword="true"/>이고, 대상이 해석되지 않거나
        /// 해당 id가 없으면 <see langword="false"/>입니다.</returns>
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

        // query의 명시 태그 하나라도 스펙 자산 태그 하나를 자신-또는-조상으로 가지면 매칭.
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

        // 페이즈 ①: 적용 큐를 틱당 예산만큼 드레인한다. 체인(OnApplication/OnStackOverflow)이 같은 틱에
        // 새로 적재한 요청도 예산이 남아있는 한 같은 while에서 이어서 처리된다 — 예산이 유일한 상한이다.
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

            // G6: DR 면역 판정은 RemoveOnApply(부수효과, G1)보다 먼저 끝내야 한다 — 그래야 면역인
            // 적용이 대상의 기존 효과를 먼저 디스펠해버리는 관측 가능한 왜곡이 생기지 않는다(다른 드랍
            // 경로 — Immune/Chance/Condition — 도 전부 부수효과 이전에 단락한다). DR은 "신규 생성" 경로
            // 전용이라 이미 같은 스펙의 활성 인스턴스가 있으면(병합 대상) 여기서 판정하지 않고 기존
            // 병합 동작을 그대로 둔다. ApplyDrHistory는 이력을 변이시키므로 정확히 한 번만 호출한다 —
            // 여기서 계산한 틱을 CreateInstance로 그대로 전달한다.
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

        // G2: ChanceToApply 롤 — null이면 항상 통과. 평가값 chance를 [0,1] 정수 스케일(×2^32)로 올려
        // NextUInt32() 롤과 BigNum 정수 비교한다(부동소수 없이 chance<=0/chance>=1 양끝도 같은 식으로 자연히 처리됨).
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

        // G1: RemoveOnApplyTags — 적용 직전, 대상의 활성 인스턴스 중 스펙 AssetTags가 이 태그들과
        // 계층 매칭되는 것을 전부 조기 제거한다. 같은 스펙(곧 병합될 인스턴스)은 제외한다.
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
                if (instance.SpecId == incomingSpecId) continue;   // 병합이 우선 — 제거 대상 제외
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
            CompiledModifier modifier, EffectTarget target, EffectTarget? source, bool hasSource, int level, int stack) =>
            EvaluateMagnitudeCore(
                modifier.Base, modifier.PerLevel, modifier.Calc, modifier.ByLevel, modifier.Tail, modifier.Increment,
                target, source, hasSource, level, stack);

        // CompiledModifier(속성 소유)와 CompiledMagnitude(G2 ChanceToApply 등 속성 무관 크기)가 공유하는
        // 표기 ①~④·CalcTag 평가 로직.
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

        // 레벨 테이블(표기 ②③④가 컴파일된 밀집 배열) 조회 — 0 이하 레벨은 1로, MaxLevel 이내는 그대로,
        // 초과분은 Tail 정책(Clamp=마지막 값 유지, Extrapolate=마지막 값 + 증분 × 초과 레벨 수)을 따른다.
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

        // G3: DurationPerLevel(있으면) × DurationScale(있으면, 없으면 배수 1) — 아직 절사·클램프 전의
        // 연속값이다. CreateInstance(신규 생성)와 MergeReapply의 Refresh/ExtendCapped(신규 지속 재계산)가
        // 이 헬퍼를 공유한다.
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

        // G3/G5: 병합(Refresh/ExtendCapped)이 쓰는 "신규 지속" — DR(G6)은 신규 생성 경로 전용이라
        // 여기서는 합성하지 않는다. 절사 후 0 이하면 최소 1틱으로 클램프한다.
        private int ComputeMergedDurationTicks(CompiledEffectSpec spec, EffectTarget target, EffectInstance instance)
        {
            var hasSource = _resolver.TryResolve(instance.Source, out var source) && source is not null;
            var value = ComputeScaledDuration(spec, target, source, hasSource, instance.Level, instance.Stack);
            var ticks = ToTicksFloor(value);
            return ticks <= 0 ? 1 : ticks;
        }

        // G6: DR 이력을 조회·갱신하고 이번 적용에 쓸 지속시간 배수를 반환한다. 창(DrWindowTicks)이
        // 지났으면 카운트를 리셋한 뒤 단계를 매긴다 — 첫 적용(카운트 0)은 배수 1, n번째(n≥1)는
        // DrStageMultipliers[min(n-1, 길이-1)]. 적용 성공·무산 여부와 무관하게 항상 카운트 +1·
        // lastAppliedTick 갱신까지 마친다(면역도 창을 연장하는 WoW 의미론).
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
            CompiledExecution execution, EffectTarget target, EffectTarget? source, bool hasSource,
            TargetId sourceId, TargetId targetId, int level, int stack)
        {
            // ponytail: 입력 개수는 저작 스펙 규모라 stackalloc으로 충분 — 대량 입력이 실제로 필요해지면 힙 배열로 교체.
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

        // 체인 발화 공통 헬퍼. trigger가 일치하는 엣지마다: 엣지 조건을 (원 대상 현재 상태 +
        // 발화 인스턴스의 Source) 기준으로 평가 → Selector 없으면 원 대상 그대로, 있으면
        // SelectorContext(발화 인스턴스의 Source 승계)로 대상들을 뽑아 → 각 대상에 EnqueueApply
        // (source 승계, 레벨은 LevelRule Inherit면 발화 레벨 그대로, Fixed면 엣지의 FixedLevel).
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

                // ponytail: 대상 수는 저작 스펙 규모라 스택 버퍼 32칸으로 충분 — 넘치면 앞 32개만 적용된다.
                Span<TargetId> targets = stackalloc TargetId[32];
                var selectorCtx = new SelectorContext(source, chain.SelectorParams, _rng);
                var count = chain.Selector.Select(in selectorCtx, targets);
                for (var k = 0; k < count; k++)
                {
                    EnqueueApply(chain.EffectId, source, targets[k], chainLevel);
                }
            }
        }

        // Duration/Infinite: 대상에 동일 SpecId 활성 인스턴스가 있으면 스택 정책으로 병합하고,
        // 없으면 새 인스턴스를 만들어 수정자 부착·태그 부여까지 끝낸다. precomputedDurationTicks는
        // ProcessPendingApply가 RemoveOnApply 이전에 DR(G6)까지 합성해 미리 계산해둔 지속 틱이며
        // (DR 미사용/병합 대상이면 -1) CreateInstance가 그대로 쓴다 — ApplyDrHistory 중복 호출 방지.
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

        // 재적용 병합: AddStack이면 스택을 늘리되 초과분은 HandleStackOverflow로 넘기고, Refresh/AddStack
        // 공통으로 정책에 따라 지속시간·주기 타이머를 리셋한다. 스택 값이 실제로 바뀔 때만 StackChanged를 낸다.
        // 병합은 신규 적용이 아니므로 OnApplication 체인은 여기서 발화하지 않는다.
        private void MergeReapply(CompiledEffectSpec spec, EffectTarget target, EffectInstance instance)
        {
            var stackPolicy = spec.Stack;
            if (stackPolicy.OnReapply == StackReapply.AddStack)
            {
                // Build 단계에서 AddStack 정책은 MaxStack > 0을 보장한다.
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
                // G5: 스택은 건드리지 않고 지속시간만 연장한다 — 판데믹 상한 = 신규 지속 × ExtendCapMultiplier.
                // G3: "신규 지속"은 DurationPerLevel/DurationScale까지 반영된 값이다(DR은 신규 생성 전용).
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

        // G4: LevelFromStack — Stack이 바뀐 뒤 Level을 동기화하고, 적용 시점 스냅샷이던 수정자를
        // 새 Level로 재평가·재부착한다(주기 효과는 매 주기 Level을 직접 평가하므로 대상이 아니다).
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

        // G5: BigNum → 틱(int) 0 방향 절사. Exponent는 DurationTicks×ExtendCapMultiplier 규모라 작지만,
        // 저작 실수로 극단값이 들어와도 무한 루프 없이 int.MaxValue로 포화되도록 반복 횟수를 자릿수 상한으로 막는다.
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

        // 스택 초과: OnStackOverflow 체인 엣지를 먼저 발화하고, 정책이 ApplyEffect면 OverflowEffectId도
        // (레벨은 항상 인스턴스 레벨 승계로) 큐에 적재한다. 그 뒤 ClearStacksOnOverflow면 스택을 1로
        // 리셋(기존 효과·수정자는 유지하고 중첩만 비운다)하고, 아니면 기존처럼 MaxStack으로 클램프한다.
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

        // 새 인스턴스: 렌트→Id 오름차순 삽입→(주기 효과가 아니면) 수정자 부착→GrantedTags 부여→Applied→
        // OnApplication 체인 발화. 주기 효과(PeriodTicks > 0)는 매 주기 Instant와 동일한 경로로 Base에
        // 가감할 뿐 지속 부착 대상이 아니다. G3/G6: DR이 있는 Duration 스펙은 ProcessPendingApply가
        // RemoveOnApply보다 먼저 지속시간(및 면역 여부)을 계산해 precomputedDurationTicks로 넘긴다 —
        // 여기서는 그 값을 그대로 쓸 뿐 ApplyDrHistory를 다시 부르지 않는다(이력 이중 변이 방지).
        // DR이 없는 Duration은 여기서 계산(최소 1틱 클램프).
        private void CreateInstance(
            CompiledEffectSpec spec, EffectTarget target, in PendingApply pending, int precomputedDurationTicks)
        {
            var hasSource = _resolver.TryResolve(pending.Source, out var source) && source is not null;

            var remainingTicks = -1;
            if (spec.DurationType == EffectDurationType.Duration)
            {
                if (spec.DrCategory.IsValid)
                {
                    // 면역(0틱 이하)이었다면 ProcessPendingApply가 이미 조용히 무산·return했으므로
                    // 이 경로에 도달했다는 것 자체가 precomputedDurationTicks > 0을 보장한다.
                    remainingTicks = precomputedDurationTicks;
                }
                else
                {
                    var value = ComputeScaledDuration(spec, target, source, hasSource, pending.Level, stack: 1);
                    var ticks = ToTicksFloor(value);
                    remainingTicks = ticks <= 0 ? 1 : ticks;   // G3: 최소 1틱.
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

        // 페이즈 ②: 대상을 TargetId 순으로, 인스턴스를 Id 순(활성 목록이 이미 canonical)으로 순회한다.
        // 이번 틱 생성분은 건너뛴다(컨트롤러 룰링). 주기 발화가 만료 검사보다 먼저 처리되므로
        // 만료되는 그 틱의 마지막 주기 발화도 반영된다. 정상 만료(ExpireInstance의 완전 제거 경로)는
        // OnCompleteNormal 체인을 발화하며, 그 적용 요청은 이번 틱 ①을 이미 지났으므로 다음 틱으로 넘어간다.
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
                    if (instance.CreatedTick == CurrentTick) continue;   // 생성 틱은 다음 틱부터 진행

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

        // 주기 도래: Instant와 동일한 경로 — Modifiers는 Base 가감(Add-only, Build 단계 보장), Executions 실행.
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

        // 만료: 스택 정책이 RemoveOneAndRefresh고 스택이 남아있으면 하나만 줄이고 지속시간을 리셋한다
        // (이 경로는 정상 종료가 아니므로 OnCompleteNormal을 발화하지 않는다). 그 외에는 RemoveInstanceCompletely로
        // 완전 제거 후 OnCompleteNormal 체인을 발화한다. RemoveByTags/RemoveById로 인한 조기 제거는
        // OnCompletePrematurely로 같은 헬퍼를 탄다(RemoveInstancePrematurely).
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

        // 완전 제거 공용 경로 — 정상 만료(ExpireInstance)와 조기 제거(RemoveByTags/RemoveById)가 공유한다.
        // 수정자 분리·GrantedTags 회수·활성 목록 제거·풀 반환까지 마친 뒤 eventKind로 생애주기 이벤트를
        // 올리고 chainTrigger 체인을 발화한다. 발화 인스턴스의 Source·Level을 그대로 승계한다.
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

        // 페이즈 ③: 1차 재계산 — ①②가 표시한 dirty 슬롯만 재집계한다.
        private void RebuildAll() => RebuildDirtyAttributes();

        // 페이즈 ⑤: 2차 재계산 — ④ Ongoing 토글이 새로 표시한 dirty 슬롯만 재집계한다.
        // 사실상 RebuildDirty 재호출이며, dirty가 없으면 아무 일도 하지 않는다.
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

        // 페이즈 ④: Ongoing 조건을 가진 인스턴스만, 대상을 TargetId 순으로, 인스턴스를 Id 순(활성 목록이
        // 이미 canonical)으로 평가한다. 평가는 ③에서 재집계된 Current 기준이며 틱당 한 번만 수행한다.
        // 결과가 바뀔 때만 토글한다 — OFF는 GrantedTags 회수 + 수정자 dirty 표시, ON은 GrantedTags 부여 +
        // dirty 표시. 인스턴스 자체는 제거하지 않는다(RebuildDirty의 RebuildSlot이 !Enabled 인스턴스의
        // 수정자를 건너뛰므로 부착 상태 그대로 두어도 집계에서 빠진다). GrantedTags는 카운트 컨테이너라
        // Add/Remove가 대칭이다.
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
