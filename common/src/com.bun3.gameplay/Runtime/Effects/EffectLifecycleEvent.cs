#nullable enable
namespace Bun3.Gameplay.Effects
{
    /// <summary>효과 인스턴스 생애주기 이벤트의 종류입니다.</summary>
    public enum EffectLifecycleKind : byte
    {
        /// <summary>새로 적용되었습니다.</summary>
        Applied = 0,
        /// <summary>지속시간이 다해 정상적으로 만료되었습니다.</summary>
        Expired = 1,
        /// <summary>지속 조건 등으로 조기 제거되었습니다.</summary>
        RemovedPrematurely = 2,
        /// <summary>스택 수가 변경되었습니다.</summary>
        StackChanged = 3,
    }

    /// <summary>효과 인스턴스의 생애주기 이벤트 하나입니다.</summary>
    public readonly struct EffectLifecycleEvent
    {
        /// <summary>이벤트를 만듭니다.</summary>
        /// <param name="kind">이벤트 종류입니다.</param>
        /// <param name="instanceId">대상 효과 인스턴스의 id입니다.</param>
        /// <param name="specId">효과 스펙 id입니다.</param>
        /// <param name="stack">이벤트 시점의 스택 수입니다.</param>
        public EffectLifecycleEvent(EffectLifecycleKind kind, ulong instanceId, int specId, int stack)
        {
            Kind = kind;
            InstanceId = instanceId;
            SpecId = specId;
            Stack = stack;
        }

        /// <summary>이벤트 종류입니다.</summary>
        public EffectLifecycleKind Kind { get; }

        /// <summary>대상 효과 인스턴스의 id입니다.</summary>
        public ulong InstanceId { get; }

        /// <summary>효과 스펙 id입니다.</summary>
        public int SpecId { get; }

        /// <summary>이벤트 시점의 스택 수입니다.</summary>
        public int Stack { get; }
    }
}
