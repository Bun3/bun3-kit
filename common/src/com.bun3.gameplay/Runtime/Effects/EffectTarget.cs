#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Effects
{
    /// <summary>
    /// 효과가 적용될 수 있는 대상 하나입니다. 속성 집합·보유 태그·활성 효과 목록을 소유합니다.
    /// </summary>
    public sealed class EffectTarget
    {
        private readonly List<EffectInstance> _activeEffects = new List<EffectInstance>();
        private EffectLifecycleEvent[] _events = new EffectLifecycleEvent[4];
        private int _eventCount;

        /// <summary>대상 식별자입니다.</summary>
        public TargetId Id { get; }

        /// <summary>이 대상의 속성 집합입니다.</summary>
        public AttributeSet Attributes { get; }

        /// <summary>이 대상이 보유한 태그와 누적 수입니다.</summary>
        public TagCountContainer Tags { get; }

        /// <summary>현재 활성 효과 인스턴스 수입니다.</summary>
        public int ActiveEffectCount => _activeEffects.Count;

        /// <summary>Id 오름차순으로 유지되는 활성 효과 인스턴스 목록입니다.</summary>
        internal List<EffectInstance> ActiveEffects => _activeEffects;

        /// <summary>아직 소비되지 않은 효과 생애주기 이벤트들입니다.</summary>
        public ReadOnlySpan<EffectLifecycleEvent> PendingEffectEvents => _events.AsSpan(0, _eventCount);

        /// <summary>효과 생애주기 이벤트 버퍼를 비웁니다.</summary>
        public void ClearEffectEvents() => _eventCount = 0;

        /// <summary>대상 식별자·속성 레지스트리·아키타입이 선언한 속성들·태그 카탈로그로 대상을 만듭니다.</summary>
        /// <param name="id">대상 식별자입니다.</param>
        /// <param name="registry">속성 정의를 담은 레지스트리입니다.</param>
        /// <param name="attributeIds">이 대상이 선언하는 속성 id들입니다.</param>
        /// <param name="tagCatalog">보유 태그 집계에 쓸 태그 카탈로그입니다.</param>
        public EffectTarget(TargetId id, AttributeRegistry registry, ReadOnlySpan<ushort> attributeIds, TagCatalog tagCatalog)
        {
            if (tagCatalog is null) throw new ArgumentNullException(nameof(tagCatalog));
            Id = id;
            Attributes = new AttributeSet(registry, attributeIds);
            Tags = tagCatalog.CreateCountContainer();
        }

        /// <summary>Id 오름차순 위치에 활성 효과 인스턴스를 삽입합니다.</summary>
        internal void InsertActive(EffectInstance instance)
        {
            var position = _activeEffects.Count;
            while (position > 0 && _activeEffects[position - 1].Id > instance.Id) position--;
            _activeEffects.Insert(position, instance);
        }

        /// <summary>활성 효과 목록에서 인스턴스를 제거합니다.</summary>
        internal void RemoveActive(EffectInstance instance) => _activeEffects.Remove(instance);

        /// <summary>효과 생애주기 이벤트를 버퍼에 적재합니다.</summary>
        internal void RaiseEffectEvent(EffectLifecycleEvent evt)
        {
            if (_eventCount == _events.Length) Array.Resize(ref _events, _events.Length * 2);
            _events[_eventCount++] = evt;
        }

        /// <summary>
        /// 이 대상의 결정론적 상태(속성 Base·활성 효과 인스턴스)를 깊은 복사한 스냅샷을 만듭니다.
        /// Current·보유 태그·대기 적용 큐·파이프라인 틱 카운터는 포함하지 않습니다 —
        /// <see cref="EffectTargetSnapshot"/> 문서 참고.
        /// </summary>
        public EffectTargetSnapshot CreateSnapshot()
        {
            var declaredCount = Attributes.DeclaredCount;
            var bases = new BigNum[declaredCount];
            for (var i = 0; i < declaredCount; i++) bases[i] = Attributes.DeclaredBaseAt(i);

            var instances = new EffectTargetSnapshot.InstanceRow[_activeEffects.Count];
            var scratch = new List<AttributeSet.ModifierSnapshotRow>(4);
            for (var i = 0; i < _activeEffects.Count; i++)
            {
                var instance = _activeEffects[i];
                scratch.Clear();
                Attributes.CollectModifiers(instance, scratch);
                var modifiers = new EffectTargetSnapshot.ModifierRow[scratch.Count];
                for (var m = 0; m < scratch.Count; m++)
                {
                    var row = scratch[m];
                    modifiers[m] = new EffectTargetSnapshot.ModifierRow(
                        row.AttributeId, row.RowIndex, row.Op, row.Magnitude, row.ScaleWithStack);
                }

                instances[i] = new EffectTargetSnapshot.InstanceRow(
                    instance.Id, instance.SpecId, instance.Source, instance.Level, instance.Stack,
                    instance.RemainingTicks, instance.PeriodCountdown, instance.Enabled, instance.CreatedTick,
                    modifiers);
            }

            return new EffectTargetSnapshot(Id, bases, instances);
        }

        /// <summary>
        /// 스냅샷으로 이 대상을 복원합니다. 현재 활성 인스턴스는 전부 이벤트 없이 분리·회수되고
        /// (복원은 관측 불가), 스냅샷의 인스턴스가 수정자 재부착까지 포함해 재구성됩니다. Base는
        /// 클램프 없이 raw로 쓰고, 마지막에 한 번 <see cref="AttributeSet.RebuildDirty"/>를 호출해
        /// Current를 재계산합니다 — 이 재계산 결정론이 비트 동일성을 보장합니다. 대기 적용 큐·파이프라인
        /// 틱 카운터·다음 발급 Id는 호출자가 별도로 복원해야 합니다.
        /// </summary>
        /// <param name="snapshot">복원할 스냅샷입니다.</param>
        /// <param name="catalog">GrantedTags 회수·재부여에 쓸 효과 카탈로그입니다(스냅샷 시점과 같은 카탈로그여야 합니다).</param>
        public void RestoreSnapshot(EffectTargetSnapshot snapshot, EffectCatalog catalog)
        {
            if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
            if (catalog is null) throw new ArgumentNullException(nameof(catalog));
            if (snapshot.TargetId != Id)
                throw new ArgumentException("다른 대상의 스냅샷은 복원할 수 없습니다.", nameof(snapshot));

            for (var i = 0; i < _activeEffects.Count; i++)
            {
                var instance = _activeEffects[i];
                Attributes.DetachModifiers(instance);
                var grantedTags = catalog.GetSpec(instance.SpecId).GrantedTags;
                for (var g = 0; g < grantedTags.Length; g++) Tags.Remove(grantedTags[g]);
                EffectInstance.Return(instance);
            }

            _activeEffects.Clear();

            var bases = snapshot.AttributeBases;
            for (var i = 0; i < bases.Length; i++) Attributes.RestoreDeclaredBase(i, bases[i]);

            var rows = snapshot.Instances;
            for (var i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                var instance = EffectInstance.Rent(
                    row.Id, row.SpecId, row.Source, row.Level, row.Stack,
                    row.RemainingTicks, row.PeriodCountdown, row.CreatedTick);
                instance.Enabled = row.Enabled;
                InsertActive(instance);

                var modifiers = row.Modifiers;
                for (var m = 0; m < modifiers.Length; m++)
                {
                    var modifier = modifiers[m];
                    Attributes.AttachModifier(
                        instance, modifier.RowIndex, modifier.AttributeId, modifier.Op,
                        modifier.Magnitude, modifier.ScaleWithStack);
                }

                var grantedTags = catalog.GetSpec(row.SpecId).GrantedTags;
                for (var g = 0; g < grantedTags.Length; g++) Tags.Add(grantedTags[g]);
            }

            Attributes.RebuildDirty();
        }
    }
}
