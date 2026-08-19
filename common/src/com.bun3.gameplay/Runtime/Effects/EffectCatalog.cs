#nullable enable
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Effects
{
    /// <summary><see cref="EffectCatalogBuilder"/>.Build로 검증까지 끝난, 변경되지 않는 효과 카탈로그입니다.</summary>
    public sealed class EffectCatalog
    {
        private readonly Dictionary<string, int> _nameToId;
        private readonly CompiledEffectSpec[] _specs;
        private readonly string[] _buildWarnings;

        internal EffectCatalog(
            Dictionary<string, int> nameToId, CompiledEffectSpec[] specs, List<string> buildWarnings)
        {
            _nameToId = nameToId;
            _specs = specs;
            _buildWarnings = buildWarnings.ToArray();
        }

        /// <summary>카탈로그에 등록된 효과 수입니다.</summary>
        public int Count => _specs.Length;

        /// <summary>이름으로 효과 id를 찾거나 없으면 예외를 던집니다.</summary>
        /// <param name="name">조회할 효과 이름입니다.</param>
        /// <returns>찾은 효과 id입니다.</returns>
        /// <exception cref="KeyNotFoundException">이름이 등록되어 있지 않은 경우입니다.</exception>
        public int GetRequiredId(string name)
        {
            if (TryGetId(name, out var id))
            {
                return id;
            }

            throw new KeyNotFoundException($"등록되지 않은 효과 이름입니다: {name}");
        }

        /// <summary>이름으로 효과 id를 시도합니다.</summary>
        /// <param name="name">조회할 효과 이름입니다.</param>
        /// <param name="id">찾은 효과 id입니다.</param>
        /// <returns>이름이 등록되어 있으면 true입니다.</returns>
        public bool TryGetId(string name, out int id) => _nameToId.TryGetValue(name, out id);

        /// <summary>id로 컴파일된 스펙을 가져옵니다.</summary>
        internal CompiledEffectSpec GetSpec(int id) => _specs[id];

        /// <summary>Build 시 발견된 체인 순환 경고들입니다. 예외를 던지지 않고 여기 쌓입니다.</summary>
        public IReadOnlyList<string> BuildWarnings => _buildWarnings;
    }

    /// <summary>컴파일된 속성 수정자입니다.</summary>
    internal sealed class CompiledModifier
    {
        internal CompiledModifier(
            ushort attributeId, AttributeModifierOp op,
            Operand? @base, Operand? perLevel, IMagnitudeCalc? calc,
            BigNum[]? byLevel, LevelTail tail, BigNum increment, bool scaleWithStack)
        {
            AttributeId = attributeId;
            Op = op;
            Base = @base;
            PerLevel = perLevel;
            Calc = calc;
            ByLevel = byLevel;
            Tail = tail;
            Increment = increment;
            ScaleWithStack = scaleWithStack;
        }

        /// <summary>수정할 대상 속성 id입니다.</summary>
        internal ushort AttributeId { get; }

        /// <summary>수정자 연산 종류입니다.</summary>
        internal AttributeModifierOp Op { get; }

        /// <summary>레벨 무관 기본 크기이며 <see cref="Calc"/>·<see cref="ByLevel"/>이 있으면 null입니다.</summary>
        internal Operand? Base { get; }

        /// <summary>레벨당 추가 크기입니다.</summary>
        internal Operand? PerLevel { get; }

        /// <summary>해석된 크기 계산 계약이며 상수/속성/레벨 테이블 기반 크기면 null입니다.</summary>
        internal IMagnitudeCalc? Calc { get; }

        /// <summary>레벨 테이블(표기 ②③④가 컴파일된 밀집 배열)이며, 없으면(표기 ①·Calc) null입니다.</summary>
        internal BigNum[]? ByLevel { get; }

        /// <summary><see cref="ByLevel"/>의 길이(MaxLevel)를 넘는 레벨을 다루는 방식입니다.</summary>
        internal LevelTail Tail { get; }

        /// <summary>Tail이 Extrapolate일 때 레벨당 증분입니다.</summary>
        internal BigNum Increment { get; }

        /// <summary>스택 수에 비례해 크기를 배율할지 여부입니다.</summary>
        internal bool ScaleWithStack { get; }
    }

    /// <summary>CompiledModifier와 같은 표기(①~④·CalcTag)로 해석된, 속성 소유 없는 단독 크기입니다.
    /// ChanceToApply처럼 특정 AttributeId에 매이지 않는 크기 정의에 씁니다.</summary>
    internal sealed class CompiledMagnitude
    {
        internal CompiledMagnitude(
            Operand? @base, Operand? perLevel, IMagnitudeCalc? calc,
            BigNum[]? byLevel, LevelTail tail, BigNum increment)
        {
            Base = @base;
            PerLevel = perLevel;
            Calc = calc;
            ByLevel = byLevel;
            Tail = tail;
            Increment = increment;
        }

        /// <summary>레벨 무관 기본 크기이며 <see cref="Calc"/>·<see cref="ByLevel"/>이 있으면 null입니다.</summary>
        internal Operand? Base { get; }

        /// <summary>레벨당 추가 크기입니다.</summary>
        internal Operand? PerLevel { get; }

        /// <summary>해석된 크기 계산 계약이며 상수/속성/레벨 테이블 기반 크기면 null입니다.</summary>
        internal IMagnitudeCalc? Calc { get; }

        /// <summary>레벨 테이블(표기 ②③④가 컴파일된 밀집 배열)이며, 없으면(표기 ①·Calc) null입니다.</summary>
        internal BigNum[]? ByLevel { get; }

        /// <summary><see cref="ByLevel"/>의 길이(MaxLevel)를 넘는 레벨을 다루는 방식입니다.</summary>
        internal LevelTail Tail { get; }

        /// <summary>Tail이 Extrapolate일 때 레벨당 증분입니다.</summary>
        internal BigNum Increment { get; }
    }

    /// <summary>컴파일된 효과 실행입니다.</summary>
    internal sealed class CompiledExecution
    {
        internal CompiledExecution(IExecutionCalc calc, Operand[] inputs)
        {
            Calc = calc;
            Inputs = inputs;
        }

        /// <summary>해석된 실행 계약입니다.</summary>
        internal IExecutionCalc Calc { get; }

        /// <summary>실행에 전달할 입력 피연산자들입니다.</summary>
        internal Operand[] Inputs { get; }
    }

    /// <summary>컴파일된 조건입니다.</summary>
    internal readonly struct CompiledCondition
    {
        internal CompiledCondition(Operand left, ComparisonOp op, Operand right)
        {
            Left = left;
            Op = op;
            Right = right;
        }

        /// <summary>좌변 피연산자입니다.</summary>
        internal Operand Left { get; }

        /// <summary>비교 연산자입니다.</summary>
        internal ComparisonOp Op { get; }

        /// <summary>우변 피연산자입니다.</summary>
        internal Operand Right { get; }
    }

    /// <summary>컴파일된 체인 엣지입니다.</summary>
    internal sealed class CompiledChain
    {
        internal CompiledChain(
            ChainTrigger trigger, int effectId, ITargetSelector? selector, BigNum[] selectorParams,
            CompiledCondition[] conditions, ChainLevelRule levelRule, int fixedLevel)
        {
            Trigger = trigger;
            EffectId = effectId;
            Selector = selector;
            SelectorParams = selectorParams;
            Conditions = conditions;
            LevelRule = levelRule;
            FixedLevel = fixedLevel;
        }

        /// <summary>발동 시점입니다.</summary>
        internal ChainTrigger Trigger { get; }

        /// <summary>발동할 대상 효과의 id입니다.</summary>
        internal int EffectId { get; }

        /// <summary>해석된 대상 선택 계약이며 없으면 null입니다(원본 대상 그대로 사용).</summary>
        internal ITargetSelector? Selector { get; }

        /// <summary>대상 선택에 전달할 매개변수들입니다.</summary>
        internal BigNum[] SelectorParams { get; }

        /// <summary>발동 조건들입니다.</summary>
        internal CompiledCondition[] Conditions { get; }

        /// <summary>대상 효과의 레벨 결정 규칙입니다.</summary>
        internal ChainLevelRule LevelRule { get; }

        /// <summary>LevelRule이 Fixed일 때 사용할 레벨입니다.</summary>
        internal int FixedLevel { get; }
    }

    /// <summary>Build에서 검증·해석까지 끝난 효과 스펙입니다.</summary>
    internal sealed class CompiledEffectSpec
    {
        internal CompiledEffectSpec(
            string name,
            EffectDurationType durationType,
            int durationTicks,
            int periodTicks,
            StackPolicy stack,
            CompiledModifier[] modifiers,
            CompiledExecution[] executions,
            CompiledCondition[] applicationConditions,
            CompiledCondition[] ongoingConditions,
            GameplayTag[] grantedTags,
            GameplayTag[] assetTags,
            GameplayTag[] immunityTags,
            CompiledChain[] chains,
            int overflowEffectId,
            GameplayTag[] removeOnApplyTags,
            CompiledMagnitude? chanceToApply,
            BigNum[]? durationPerLevel,
            CompiledMagnitude? durationScale,
            GameplayTag drCategory,
            int drWindowTicks,
            BigNum[] drStageMultipliers)
        {
            Name = name;
            DurationType = durationType;
            DurationTicks = durationTicks;
            PeriodTicks = periodTicks;
            Stack = stack;
            Modifiers = modifiers;
            Executions = executions;
            ApplicationConditions = applicationConditions;
            OngoingConditions = ongoingConditions;
            GrantedTags = grantedTags;
            AssetTags = assetTags;
            ImmunityTags = immunityTags;
            Chains = chains;
            OverflowEffectId = overflowEffectId;
            RemoveOnApplyTags = removeOnApplyTags;
            ChanceToApply = chanceToApply;
            DurationPerLevel = durationPerLevel;
            DurationScale = durationScale;
            DrCategory = drCategory;
            DrWindowTicks = drWindowTicks;
            DrStageMultipliers = drStageMultipliers;
        }

        /// <summary>효과 이름입니다.</summary>
        internal string Name { get; }

        /// <summary>지속 방식입니다.</summary>
        internal EffectDurationType DurationType { get; }

        /// <summary>지속 틱 수입니다.</summary>
        internal int DurationTicks { get; }

        /// <summary>주기 실행 간격(틱)입니다.</summary>
        internal int PeriodTicks { get; }

        /// <summary>스택 정책입니다.</summary>
        internal StackPolicy Stack { get; }

        /// <summary>컴파일된 속성 수정자들입니다.</summary>
        internal CompiledModifier[] Modifiers { get; }

        /// <summary>컴파일된 효과 실행들입니다.</summary>
        internal CompiledExecution[] Executions { get; }

        /// <summary>컴파일된 적용 조건들입니다.</summary>
        internal CompiledCondition[] ApplicationConditions { get; }

        /// <summary>컴파일된 지속 조건들입니다.</summary>
        internal CompiledCondition[] OngoingConditions { get; }

        /// <summary>해석된 부여 태그들입니다.</summary>
        internal GameplayTag[] GrantedTags { get; }

        /// <summary>해석된 자산 태그들입니다.</summary>
        internal GameplayTag[] AssetTags { get; }

        /// <summary>해석된 면역 태그들입니다.</summary>
        internal GameplayTag[] ImmunityTags { get; }

        /// <summary>컴파일된 체인 엣지들입니다.</summary>
        internal CompiledChain[] Chains { get; }

        /// <summary>스택 초과 시 대신 적용할 효과의 id이며 -1이면 없습니다.</summary>
        internal int OverflowEffectId { get; }

        /// <summary>해석된 RemoveOnApply 태그들입니다. 이 효과가 적용될 때 매칭되는 대상의 활성
        /// 효과를 즉시 제거합니다.</summary>
        internal GameplayTag[] RemoveOnApplyTags { get; }

        /// <summary>적용 확률을 정하는 컴파일된 크기이며 null이면 항상 적용됩니다.</summary>
        internal CompiledMagnitude? ChanceToApply { get; }

        /// <summary>레벨별 지속 틱(표기 ②만)이며 없으면(<see cref="EffectSpec.DurationTicks"/> 단일값
        /// 사용) null입니다. 스펙 §15 G3.</summary>
        internal BigNum[]? DurationPerLevel { get; }

        /// <summary>적용 시 1회 평가되는 지속시간 배수이며 없으면 null(배수 1과 동일)입니다. 스펙 §15 G3.</summary>
        internal CompiledMagnitude? DurationScale { get; }

        /// <summary>DR 계열 분류 태그이며 미사용이면 <see cref="GameplayTag.None"/>입니다. 스펙 §15 G6.</summary>
        internal GameplayTag DrCategory { get; }

        /// <summary>DR 적용 횟수 리셋 창(틱)입니다. 스펙 §15 G6.</summary>
        internal int DrWindowTicks { get; }

        /// <summary>DR 단계별 지속시간 배수이며 <see cref="DrCategory"/>가 미사용이면 빈 배열입니다.
        /// 스펙 §15 G6.</summary>
        internal BigNum[] DrStageMultipliers { get; }
    }
}
