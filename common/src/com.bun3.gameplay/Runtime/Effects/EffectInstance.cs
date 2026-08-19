#nullable enable
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;

namespace Bun3.Gameplay.Effects
{
    /// <summary>
    /// 대상에 적용된 효과 하나의 런타임 인스턴스입니다. World가 발급하는 단조 증가 <see cref="Id"/>가
    /// 속성 집계의 canonical 순서 근거입니다. 생성·반환은 내부 정적 풀을 거칩니다.
    /// </summary>
    public sealed class EffectInstance : IAttributeModifierSource
    {
        // ponytail: 프로세스 전역 static 풀 — 스레드당 월드 1개 전제(동시에 여러 월드가 한 스레드를
        // 공유하거나 여러 스레드가 풀을 동시에 건드리면 안전하지 않다). 필요해지면 풀을 월드/파이프라인
        // 소유 인스턴스로 옮긴다.
        private static readonly Stack<EffectInstance> Pool = new Stack<EffectInstance>();

        private EffectInstance()
        {
        }

        /// <summary>World가 발급한 단조 증가 id입니다.</summary>
        public ulong Id { get; private set; }

        /// <summary>효과 스펙 id입니다.</summary>
        public int SpecId { get; private set; }

        /// <summary>Duration 효과의 남은 틱 수입니다. Infinite/Instant는 의미가 없습니다.</summary>
        public int RemainingTicks { get; internal set; }

        /// <summary>다음 주기 실행까지 남은 틱 수입니다. 주기 실행이 없으면 -1입니다.</summary>
        public int PeriodCountdown { get; internal set; }

        /// <summary>현재 스택 수입니다.</summary>
        public int Stack { get; internal set; }

        /// <summary>효과 레벨입니다. internal 세터는 LevelFromStack 병합 동기화 전용입니다.</summary>
        public int Level { get; internal set; }

        /// <summary>이 효과를 적용한 시전자입니다.</summary>
        public TargetId Source { get; private set; }

        /// <summary>Ongoing 조건 등으로 집계에서 제외되었는지 여부입니다. false면 집계에서 건너뜁니다.</summary>
        public bool Enabled { get; internal set; }

        /// <summary>
        /// 이 인스턴스가 생성된 파이프라인 틱입니다. 컨트롤러 룰링: 생성된 틱에는 수명(RemainingTicks)·
        /// 주기(PeriodCountdown)가 진행되지 않고, 다음 틱부터 진행됩니다.
        /// </summary>
        public long CreatedTick { get; private set; }

        /// <summary>풀에서 인스턴스를 빌리고 필드를 초기화합니다.</summary>
        internal static EffectInstance Rent(
            ulong id, int specId, TargetId source, int level, int stack,
            int remainingTicks, int periodCountdown, long createdTick)
        {
            var instance = Pool.Count > 0 ? Pool.Pop() : new EffectInstance();
            instance.Id = id;
            instance.SpecId = specId;
            instance.Source = source;
            instance.Level = level;
            instance.Stack = stack;
            instance.RemainingTicks = remainingTicks;
            instance.PeriodCountdown = periodCountdown;
            instance.CreatedTick = createdTick;
            instance.Enabled = true;
            return instance;
        }

        /// <summary>인스턴스를 비활성화하고 풀에 반환합니다.</summary>
        internal static void Return(EffectInstance instance)
        {
            instance.Enabled = false;
            Pool.Push(instance);
        }
    }
}
