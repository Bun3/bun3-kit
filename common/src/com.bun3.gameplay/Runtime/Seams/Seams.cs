#nullable enable
using System;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Seams
{
    /// <summary>크기(피해, 회복 등)를 계산하는 계약입니다.</summary>
    public interface IMagnitudeCalc
    {
        /// <summary>주어진 컨텍스트에서 크기를 계산합니다.</summary>
        /// <param name="ctx">계산에 사용할 컨텍스트입니다.</param>
        /// <returns>계산된 크기 값입니다.</returns>
        BigNum Calculate(in MagnitudeContext ctx);
    }

    /// <summary>효과 실행을 수행하는 계약입니다.</summary>
    public interface IExecutionCalc
    {
        /// <summary>주어진 컨텍스트에서 효과를 실행합니다.</summary>
        /// <param name="ctx">실행에 사용할 컨텍스트입니다.</param>
        void Execute(ref ExecutionContext ctx);
    }

    /// <summary>대상을 선택하는 계약입니다.</summary>
    public interface ITargetSelector
    {
        /// <summary>주어진 컨텍스트에서 대상을 선택합니다.</summary>
        /// <param name="ctx">선택에 사용할 컨텍스트입니다.</param>
        /// <param name="results">선택된 대상 식별자들을 저장할 배열입니다.</param>
        /// <returns>선택된 대상의 개수입니다.</returns>
        int Select(in SelectorContext ctx, System.Span<TargetId> results);
    }

    /// <summary>크기 계산에 필요한 컨텍스트입니다. 소스가 미해석이면 <see cref="SourceAttr"/>는 항상 0입니다.</summary>
    public readonly ref struct MagnitudeContext
    {
        private readonly EffectTarget _target;
        private readonly EffectTarget? _source;

        internal MagnitudeContext(
            EffectTarget target, EffectTarget? source, bool hasSource, int level, int stack, long worldTick)
        {
            _target = target;
            _source = source;
            HasSource = hasSource;
            Level = level;
            Stack = stack;
            WorldTick = worldTick;
        }

        /// <summary>시전자가 해석되었는지 나타냅니다.</summary>
        public bool HasSource { get; }

        /// <summary>효과 레벨입니다.</summary>
        public int Level { get; }

        /// <summary>효과 스택 수입니다.</summary>
        public int Stack { get; }

        /// <summary>계산 시점의 월드 틱입니다.</summary>
        public long WorldTick { get; }

        /// <summary>시전자 속성의 Current 값을 가져옵니다. 시전자가 미해석이면 0입니다.</summary>
        /// <param name="attributeId">조회할 속성 id입니다.</param>
        public BigNum SourceAttr(ushort attributeId) =>
            HasSource ? _source!.Attributes.GetCurrent(attributeId) : BigNum.Zero;

        /// <summary>대상 속성의 Current 값을 가져옵니다.</summary>
        /// <param name="attributeId">조회할 속성 id입니다.</param>
        public BigNum TargetAttr(ushort attributeId) => _target.Attributes.GetCurrent(attributeId);

        /// <summary>대상이 주어진 태그를 보유하고 있는지(자신 또는 자손 계층 포함) 확인합니다.</summary>
        /// <param name="tag">조회할 태그입니다.</param>
        public bool TargetHasTag(GameplayTag tag) => _target.Tags.Has(tag);

        /// <summary>시전자가 주어진 태그를 보유하고 있는지(자신 또는 자손 계층 포함) 확인합니다.
        /// 시전자가 미해석이면 항상 false입니다.</summary>
        /// <param name="tag">조회할 태그입니다.</param>
        public bool SourceHasTag(GameplayTag tag) => HasSource && _source!.Tags.Has(tag);
    }

    /// <summary>효과 실행에 필요한 컨텍스트입니다. 소스가 미해석이면 <see cref="SourceAttr"/>는 항상 0입니다.</summary>
    public ref struct ExecutionContext
    {
        private readonly EffectPipeline _pipeline;
        private readonly EffectTarget _target;
        private readonly EffectTarget? _source;
        private readonly TargetId _sourceId;
        private readonly TargetId _targetId;
        private readonly ReadOnlySpan<BigNum> _inputs;

        internal ExecutionContext(
            EffectPipeline pipeline, EffectTarget target, EffectTarget? source, bool hasSource,
            TargetId sourceId, TargetId targetId, int level, int stack, long worldTick,
            ReadOnlySpan<BigNum> inputs, IRng rng)
        {
            _pipeline = pipeline;
            _target = target;
            _source = source;
            _sourceId = sourceId;
            _targetId = targetId;
            HasSource = hasSource;
            Level = level;
            Stack = stack;
            WorldTick = worldTick;
            _inputs = inputs;
            Rng = rng;
        }

        /// <summary>시전자가 해석되었는지 나타냅니다.</summary>
        public bool HasSource { get; }

        /// <summary>효과 레벨입니다.</summary>
        public int Level { get; }

        /// <summary>효과 스택 수입니다.</summary>
        public int Stack { get; }

        /// <summary>계산 시점의 월드 틱입니다.</summary>
        public long WorldTick { get; }

        /// <summary>이 실행에서 쓸 수 있는 난수 생성기입니다.</summary>
        public IRng Rng { get; }

        /// <summary>시전자 속성의 Current 값을 가져옵니다. 시전자가 미해석이면 0입니다.</summary>
        /// <param name="attributeId">조회할 속성 id입니다.</param>
        public BigNum SourceAttr(ushort attributeId) =>
            HasSource ? _source!.Attributes.GetCurrent(attributeId) : BigNum.Zero;

        /// <summary>대상 속성의 Current 값을 가져옵니다.</summary>
        /// <param name="attributeId">조회할 속성 id입니다.</param>
        public BigNum TargetAttr(ushort attributeId) => _target.Attributes.GetCurrent(attributeId);

        /// <summary>미리 평가된 입력 피연산자 값을 가져옵니다.</summary>
        /// <param name="index">입력 목록 안의 인덱스입니다.</param>
        public BigNum Input(int index) => _inputs[index];

        /// <summary>대상이 주어진 태그를 보유하고 있는지(자신 또는 자손 계층 포함) 확인합니다.</summary>
        /// <param name="tag">조회할 태그입니다.</param>
        public bool TargetHasTag(GameplayTag tag) => _target.Tags.Has(tag);

        /// <summary>시전자가 주어진 태그를 보유하고 있는지(자신 또는 자손 계층 포함) 확인합니다.
        /// 시전자가 미해석이면 항상 false입니다.</summary>
        /// <param name="tag">조회할 태그입니다.</param>
        public bool SourceHasTag(GameplayTag tag) => HasSource && _source!.Tags.Has(tag);

        /// <summary>대상 속성의 Base를 직접 씁니다. 항상 클램프·전파·이벤트 규칙을 통과합니다.</summary>
        /// <param name="attributeId">쓸 속성 id입니다.</param>
        /// <param name="value">쓸 값입니다.</param>
        public void WriteTarget(ushort attributeId, BigNum value) => _target.Attributes.SetBase(attributeId, value);

        /// <summary>같은 시전자·대상·레벨로 다른 효과를 적용 큐에 적재합니다. 직접 재진입 적용은 금지됩니다.</summary>
        /// <param name="specId">적용할 효과 스펙 id입니다.</param>
        public void ApplyToTarget(int specId) => _pipeline.EnqueueApply(specId, _sourceId, _targetId, Level);
    }

    /// <summary>대상 선택에 필요한 컨텍스트입니다.</summary>
    public readonly ref struct SelectorContext
    {
        private readonly ReadOnlySpan<BigNum> _params;

        internal SelectorContext(TargetId source, ReadOnlySpan<BigNum> parameters, IRng rng)
        {
            Source = source;
            _params = parameters;
            Rng = rng;
        }

        /// <summary>선택을 촉발한 시전자입니다.</summary>
        public TargetId Source { get; }

        /// <summary>이 선택에서 쓸 수 있는 난수 생성기입니다.</summary>
        public IRng Rng { get; }

        /// <summary>선택에 전달된 매개변수 수입니다.</summary>
        public int ParamCount => _params.Length;

        /// <summary>선택에 전달된 매개변수 값을 가져옵니다.</summary>
        /// <param name="index">매개변수 목록 안의 인덱스입니다.</param>
        public BigNum Param(int index) => _params[index];
    }
}
