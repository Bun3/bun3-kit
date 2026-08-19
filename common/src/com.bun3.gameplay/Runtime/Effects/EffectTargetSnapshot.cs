#nullable enable
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;

namespace Bun3.Gameplay.Effects
{
    /// <summary>
    /// <see cref="EffectTarget.CreateSnapshot"/>이 만든, 한 대상의 결정론적 상태를 메모리에 깊은
    /// 복사한 불변 스냅샷입니다. 저장 대상은 속성 Base(선언 순서)와 활성 효과 인스턴스 목록(Id
    /// 오름차순, 각 인스턴스가 부착한 수정자 행 포함)뿐입니다 — Current는 저장하지 않습니다(복원이
    /// 수정자 재부착 후 전체 재계산으로 재구성하며, 그 결정론이 비트 동일성을 보장합니다). 보유 태그도
    /// 저장하지 않습니다 — 슬라이스 2 범위에서 태그는 전부 활성 인스턴스의 GrantedTags를 경유해서만
    /// 붙으므로 인스턴스 복원이 재부여로 충분합니다. 대기 적용 큐·파이프라인 틱 카운터·다음 발급
    /// Id는 이 스냅샷의 책임 밖입니다(호출자가 별도로 관리). 불투명 토큰입니다 — 멤버가 전부
    /// internal이며 <see cref="EffectTarget.CreateSnapshot"/>/<see cref="EffectTarget.RestoreSnapshot"/>
    /// 전용이고, 호출자는 인스턴스를 보관·전달만 할 뿐 내부를 들여다보지 않습니다.
    /// </summary>
    public sealed class EffectTargetSnapshot
    {
        internal EffectTargetSnapshot(
            TargetId targetId, BigNum[] attributeBases, InstanceRow[] instances, DrHistoryRow[] drHistory)
        {
            TargetId = targetId;
            AttributeBases = attributeBases;
            Instances = instances;
            DrHistory = drHistory;
        }

        /// <summary>이 스냅샷이 속한 대상 식별자입니다. 다른 대상으로의 복원을 막는 데 쓰입니다.</summary>
        internal TargetId TargetId { get; }

        /// <summary>속성 Base 값들 — <see cref="AttributeSet.DeclaredAttributeIdAt"/> 선언 순서와 1:1입니다.</summary>
        internal BigNum[] AttributeBases { get; }

        /// <summary>활성 효과 인스턴스 상태들 — Id 오름차순입니다.</summary>
        internal InstanceRow[] Instances { get; }

        /// <summary>DR(체감 저항, 스펙 §15 G6) 계열별 적용 이력입니다. 이 이력이 지속시간 계산에
        /// 쓰이므로 결정론 재생을 위해 스냅샷·복원 양쪽에 포함됩니다.</summary>
        internal DrHistoryRow[] DrHistory { get; }

        /// <summary>스냅샷 인스턴스 하나의 필드와, 그 인스턴스가 부착했던 수정자 행들입니다.</summary>
        internal sealed class InstanceRow
        {
            internal InstanceRow(
                ulong id, int specId, TargetId source, int level, int stack,
                int remainingTicks, int periodCountdown, bool enabled, long createdTick,
                ModifierRow[] modifiers)
            {
                Id = id;
                SpecId = specId;
                Source = source;
                Level = level;
                Stack = stack;
                RemainingTicks = remainingTicks;
                PeriodCountdown = periodCountdown;
                Enabled = enabled;
                CreatedTick = createdTick;
                Modifiers = modifiers;
            }

            internal ulong Id { get; }
            internal int SpecId { get; }
            internal TargetId Source { get; }
            internal int Level { get; }
            internal int Stack { get; }
            internal int RemainingTicks { get; }
            internal int PeriodCountdown { get; }
            internal bool Enabled { get; }
            internal long CreatedTick { get; }
            internal ModifierRow[] Modifiers { get; }
        }

        /// <summary>인스턴스가 속성 슬롯에 부착했던 수정자 행 하나입니다. Magnitude는 적용 시점에
        /// 평가된 값 그대로 저장됩니다(복원 시 재평가하지 않음).</summary>
        internal readonly struct ModifierRow
        {
            internal ModifierRow(
                ushort attributeId, int rowIndex, AttributeModifierOp op, BigNum magnitude, bool scaleWithStack)
            {
                AttributeId = attributeId;
                RowIndex = rowIndex;
                Op = op;
                Magnitude = magnitude;
                ScaleWithStack = scaleWithStack;
            }

            internal ushort AttributeId { get; }
            internal int RowIndex { get; }
            internal AttributeModifierOp Op { get; }
            internal BigNum Magnitude { get; }
            internal bool ScaleWithStack { get; }
        }

        /// <summary>DR 계열 태그 하나의 스냅샷 적용 이력 행입니다.</summary>
        internal readonly struct DrHistoryRow
        {
            internal DrHistoryRow(ushort categoryTagIndex, int appliedCount, long lastAppliedTick)
            {
                CategoryTagIndex = categoryTagIndex;
                AppliedCount = appliedCount;
                LastAppliedTick = lastAppliedTick;
            }

            internal ushort CategoryTagIndex { get; }
            internal int AppliedCount { get; }
            internal long LastAppliedTick { get; }
        }
    }
}
