#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
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
    }
}
