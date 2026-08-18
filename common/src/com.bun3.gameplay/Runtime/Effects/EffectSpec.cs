#nullable enable
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;

namespace Bun3.Gameplay.Effects
{
    /// <summary>효과의 지속 방식입니다.</summary>
    public enum EffectDurationType : byte
    {
        /// <summary>즉시 적용되고 곧바로 사라집니다.</summary>
        Instant = 0,
        /// <summary>정해진 틱 수만큼 지속됩니다.</summary>
        Duration = 1,
        /// <summary>명시적으로 제거되기 전까지 무한히 지속됩니다.</summary>
        Infinite = 2,
    }

    /// <summary>스택형 효과에 재적용될 때의 동작입니다.</summary>
    public enum StackReapply : byte
    {
        /// <summary>지속시간만 갱신합니다.</summary>
        Refresh = 0,
        /// <summary>스택 하나를 추가합니다.</summary>
        AddStack = 1,
    }

    /// <summary>스택이 소멸(만료)될 때의 동작입니다.</summary>
    public enum StackExpiration : byte
    {
        /// <summary>모든 스택을 한 번에 제거합니다.</summary>
        ClearAll = 0,
        /// <summary>스택 하나만 제거하고 지속시간을 갱신합니다.</summary>
        RemoveOneAndRefresh = 1,
    }

    /// <summary>최대 스택 수를 초과해 재적용될 때의 동작입니다.</summary>
    public enum StackOverflow : byte
    {
        /// <summary>재적용을 거부합니다.</summary>
        Deny = 0,
        /// <summary>지정된 다른 효과를 대신 적용합니다.</summary>
        ApplyEffect = 1,
    }

    /// <summary>체인 효과가 발동하는 시점입니다.</summary>
    public enum ChainTrigger : byte
    {
        /// <summary>원본 효과가 적용되는 시점입니다.</summary>
        OnApplication = 0,
        /// <summary>원본 효과가 정상적으로 종료되는 시점입니다.</summary>
        OnCompleteNormal = 1,
        /// <summary>원본 효과가 조기(비정상)에 종료되는 시점입니다.</summary>
        OnCompletePrematurely = 2,
        /// <summary>원본 효과가 스택 초과를 겪는 시점입니다.</summary>
        OnStackOverflow = 3,
    }

    /// <summary>체인으로 발동하는 효과의 레벨을 결정하는 규칙입니다.</summary>
    public enum ChainLevelRule : byte
    {
        /// <summary>원본 효과의 레벨을 그대로 물려받습니다.</summary>
        Inherit = 0,
        /// <summary>고정된 레벨을 사용합니다.</summary>
        Fixed = 1,
    }

    /// <summary>레벨 테이블에서 MaxLevel을 넘는 레벨을 다루는 방식입니다.</summary>
    public enum LevelTail : byte
    {
        /// <summary>MaxLevel의 마지막 값을 그대로 유지합니다.</summary>
        Clamp = 0,
        /// <summary>마지막 값에서 레벨당 증분을 더해 선형 외삽합니다.</summary>
        Extrapolate = 1,
    }

    /// <summary>희소 레벨 커브의 키 하나입니다(레벨, 값).</summary>
    public sealed class LevelKey
    {
        /// <summary>레벨입니다.</summary>
        public int Level { get; set; }

        /// <summary>해당 레벨의 값입니다.</summary>
        public BigNum Value { get; set; }
    }

    /// <summary>
    /// 크기 정의입니다. 레벨 스케일링 표기는 상호 배타 계열 5종 중 정확히 하나입니다 —
    /// ① <see cref="Base"/>(+<see cref="PerLevel"/>) 선형, ② <see cref="PerLevelValues"/> 명시 배열,
    /// ③ <see cref="Formula"/> 결정론 수식, ④ <see cref="CurveKeys"/> 희소 키+선형 보간,
    /// 또는 SeamRegistry에 등록된 <see cref="CalcTag"/>. 스펙 §15.1 참고.
    /// </summary>
    public sealed class MagnitudeDef
    {
        /// <summary>레벨 무관 기본 크기입니다(표기 ①). 다른 표기들과는 배타적입니다.</summary>
        public Operand? Base { get; set; }

        /// <summary>레벨 1당 추가되는 크기입니다(표기 ①). <see cref="Base"/>가 있을 때만 사용할 수 있습니다.</summary>
        public Operand? PerLevel { get; set; }

        /// <summary>레벨별 명시 값 배열입니다(표기 ②). 길이가 곧 <see cref="EffectSpec.MaxLevel"/>이어야 합니다.</summary>
        public List<BigNum>? PerLevelValues { get; set; }

        /// <summary>결정론 수식입니다(표기 ③). <see cref="Numerics.BigNumFormula"/> 문법을 따르며
        /// 변수 x에 레벨을 대입해 평가합니다.</summary>
        public string? Formula { get; set; }

        /// <summary>희소 레벨 키 목록입니다(표기 ④). 레벨 오름차순, 중복 금지, 첫 키는 레벨 1이어야 합니다.
        /// 키 사이는 선형 보간, 마지막 키 뒤는 <see cref="Tail"/> 정책을 따릅니다.</summary>
        public List<LevelKey>? CurveKeys { get; set; }

        /// <summary>SeamRegistry에 등록된 크기 계산 태그입니다. 다른 표기들과는 배타적입니다.</summary>
        public string? CalcTag { get; set; }

        /// <summary>레벨 테이블(②③④)이 MaxLevel을 넘는 레벨을 다루는 방식입니다.</summary>
        public LevelTail Tail { get; set; }

        /// <summary><see cref="Tail"/>이 Extrapolate일 때 레벨당 증분입니다. 0이면 배열의 마지막
        /// 두 값의 차로 자동 계산됩니다.</summary>
        public BigNum ExtrapolateIncrement { get; set; }
    }

    /// <summary>속성 수정자 정의입니다.</summary>
    public sealed class ModifierDef
    {
        /// <summary>수정할 대상 속성 id입니다.</summary>
        public ushort AttributeId { get; set; }

        /// <summary>수정자 연산 종류입니다.</summary>
        public AttributeModifierOp Op { get; set; }

        /// <summary>수정 크기 정의입니다.</summary>
        public MagnitudeDef Magnitude { get; set; } = new MagnitudeDef();

        /// <summary>스택 수에 비례해 크기를 배율할지 여부입니다. 기본값은 true입니다.</summary>
        public bool ScaleWithStack { get; set; } = true;
    }

    /// <summary>효과 실행(부수효과) 정의입니다.</summary>
    public sealed class ExecutionDef
    {
        /// <summary>SeamRegistry에 등록된 실행 태그입니다.</summary>
        public string CalcTag { get; set; } = string.Empty;

        /// <summary>실행에 전달할 입력 피연산자들입니다.</summary>
        public List<Operand> Inputs { get; set; } = new List<Operand>();
    }

    /// <summary>조건 정의 — 좌변과 우변을 비교 연산자로 비교합니다.</summary>
    public sealed class ConditionDef
    {
        /// <summary>좌변 피연산자입니다.</summary>
        public Operand Left { get; set; }

        /// <summary>비교 연산자입니다.</summary>
        public ComparisonOp Op { get; set; }

        /// <summary>우변 피연산자입니다.</summary>
        public Operand Right { get; set; }
    }

    /// <summary>체인 엣지 정의 — 원본 효과에서 다른 효과로 이어지는 발동 조건입니다.</summary>
    public sealed class ChainEdgeDef
    {
        /// <summary>발동 시점입니다.</summary>
        public ChainTrigger Trigger { get; set; }

        /// <summary>발동할 대상 효과의 이름입니다.</summary>
        public string EffectName { get; set; } = string.Empty;

        /// <summary>SeamRegistry에 등록된 대상 선택 태그이며 없으면 null입니다(원본 대상 그대로 사용).</summary>
        public string? SelectorTag { get; set; }

        /// <summary>대상 선택에 전달할 매개변수들입니다.</summary>
        public List<BigNum> SelectorParams { get; set; } = new List<BigNum>();

        /// <summary>발동 조건들이며 전부 만족해야 발동합니다.</summary>
        public List<ConditionDef> Conditions { get; set; } = new List<ConditionDef>();

        /// <summary>대상 효과의 레벨 결정 규칙입니다.</summary>
        public ChainLevelRule LevelRule { get; set; }

        /// <summary><see cref="LevelRule"/>이 Fixed일 때 사용할 레벨입니다.</summary>
        public int FixedLevel { get; set; }
    }

    /// <summary>스택 정책입니다.</summary>
    public sealed class StackPolicy
    {
        /// <summary>최대 스택 수이며 0이면 스택을 사용하지 않는 효과입니다.</summary>
        public int MaxStack { get; set; }

        /// <summary>스택 가능한 효과가 재적용될 때의 동작입니다.</summary>
        public StackReapply OnReapply { get; set; }

        /// <summary>재적용마다 추가할 스택 수입니다. 기본값은 1입니다.</summary>
        public int AddStackCount { get; set; } = 1;

        /// <summary>재적용 시 지속시간을 갱신할지 여부입니다. 기본값은 true입니다.</summary>
        public bool RefreshDurationOnReapply { get; set; } = true;

        /// <summary>재적용 시 주기 실행 타이머를 리셋할지 여부입니다.</summary>
        public bool ResetPeriodOnReapply { get; set; }

        /// <summary>스택이 소멸될 때의 동작입니다.</summary>
        public StackExpiration OnExpiration { get; set; }

        /// <summary>최대 스택 수를 초과할 때의 동작입니다.</summary>
        public StackOverflow OnOverflow { get; set; }

        /// <summary><see cref="OnOverflow"/>가 ApplyEffect일 때 대신 적용할 효과의 이름입니다.</summary>
        public string? OverflowEffectName { get; set; }

        /// <summary>초과 적용 시 기존 스택을 모두 지울지 여부입니다.</summary>
        public bool ClearStacksOnOverflow { get; set; }
    }

    /// <summary>
    /// 효과 하나의 저작 스펙입니다. 로더가 채우는 프로퍼티 가방이며,
    /// <see cref="Effects.EffectCatalogBuilder"/>가 검증한 뒤 컴파일합니다.
    /// </summary>
    public sealed class EffectSpec
    {
        /// <summary>카탈로그 내에서 고유해야 하는 효과 이름입니다.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>레벨 테이블 표기(②③④) 사용 시 필요한 최대 레벨입니다. 0이면 미선언입니다.</summary>
        public int MaxLevel { get; set; }

        /// <summary>지속 방식입니다.</summary>
        public EffectDurationType DurationType { get; set; }

        /// <summary>지속 틱 수입니다. Instant/Infinite는 0이어야 합니다.</summary>
        public int DurationTicks { get; set; }

        /// <summary>주기 실행 간격(틱)이며 0이면 주기 실행이 없습니다.</summary>
        public int PeriodTicks { get; set; }

        /// <summary>스택 정책입니다.</summary>
        public StackPolicy Stack { get; set; } = new StackPolicy();

        /// <summary>속성 수정자들입니다.</summary>
        public List<ModifierDef> Modifiers { get; set; } = new List<ModifierDef>();

        /// <summary>효과 실행(부수효과)들입니다.</summary>
        public List<ExecutionDef> Executions { get; set; } = new List<ExecutionDef>();

        /// <summary>적용 조건들이며 전부 만족해야 적용됩니다.</summary>
        public List<ConditionDef> ApplicationConditions { get; set; } = new List<ConditionDef>();

        /// <summary>지속 조건들이며 하나라도 깨지면 효과가 제거되지 않고 비활성(enabled=false)으로
        /// 토글되어 수정자·부여 태그가 꺼집니다. 조건이 다시 충족되면 활성으로 되돌아옵니다.</summary>
        public List<ConditionDef> OngoingConditions { get; set; } = new List<ConditionDef>();

        /// <summary>효과가 적용되어 있는 동안 대상에게 부여되는 태그들입니다.</summary>
        public List<string> GrantedTags { get; set; } = new List<string>();

        /// <summary>효과 자체를 분류하는 자산 태그들입니다.</summary>
        public List<string> AssetTags { get; set; } = new List<string>();

        /// <summary>이 효과가 활성인 동안, AssetTags가 이 태그들의 자손-또는-자신인 다른 효과의
        /// 적용을 차단합니다.</summary>
        public List<string> ImmunityTags { get; set; } = new List<string>();

        /// <summary>다른 효과로 이어지는 체인 엣지들입니다.</summary>
        public List<ChainEdgeDef> Chains { get; set; } = new List<ChainEdgeDef>();
    }
}
