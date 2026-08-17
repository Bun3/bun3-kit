using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>대기열 항목에 실린 초기 데이터. 저빈도 경로라 박싱/할당을 허용한다.</summary>
    internal interface IQueuedPopupArg
    {
        void Deliver(Popup popup);
    }

    internal sealed class QueuedPopupArg<TArg> : IQueuedPopupArg
    {
        private readonly TArg _arg;

        public QueuedPopupArg(TArg arg) => _arg = arg;

        public void Deliver(Popup popup) => PopupStack.DeliverArg(popup, _arg);
    }

    /// <summary>
    /// 채널형 순차 표시 큐. <see cref="PopupStack.Enqueue"/>(스택이 완전히 비어야 표시)와 달리,
    /// <b>이 큐가 연 팝업이 닫혔는가</b>만 기준으로 하나씩 표시한다 — 다른 팝업(우편함 등) 위에도
    /// 뜬다. 여러 소스가 밀어 넣는 획득 아이템/승급 알림처럼 "그룹 내 1개씩" 규칙이 필요한 곳에 쓴다.
    /// </summary>
    /// <remarks>
    /// <paramref name="priority"/>가 높은 항목이 먼저 표시되고, 같은 우선순위끼리는 삽입 순서를
    /// 지킨다(예: 승급 2 &gt; 특별 아이템 1 &gt; 일반 묶음 0). 채널이 여러 개 필요하면 인스턴스를
    /// 여러 개 만들면 된다. 스택 <c>Clear()</c> 후에도 남은 항목은 계속 표시된다 — 씬 전환 시
    /// 대기 항목까지 버리려면 <see cref="Clear"/>를 함께 부를 것.
    /// </remarks>
    public sealed class PopupQueue
    {
        private readonly struct Entry
        {
            public readonly PopupKey Key;
            public readonly int Layer;
            public readonly int Priority;
            public readonly IQueuedPopupArg Arg;

            public Entry(PopupKey key, int layer, int priority, IQueuedPopupArg arg)
            {
                Key = key;
                Layer = layer;
                Priority = priority;
                Arg = arg;
            }
        }

        private readonly PopupStack _stack;

        // 정렬 불변식: (Priority 내림차순, 삽입 순서). 머리 = 다음 표시 대상.
        private readonly List<Entry> _entries = new();

        private Popup _current;
        private bool _draining;

        public PopupQueue(PopupStack stack)
            => _stack = stack ?? throw new ArgumentNullException(nameof(stack));

        /// <summary>표시를 기다리는 항목 수.</summary>
        public int Count => _entries.Count;

        /// <summary>이 큐가 열어 현재 표시 중인 팝업. 없으면 null.</summary>
        public Popup Current => _current;

        /// <summary>대기열에 넣는다. 표시 가능하면 즉시 열린다.</summary>
        public void Enqueue(PopupKey key, int layer = 0, int priority = 0)
            => EnqueueCore(key, layer, priority, null);

        /// <summary>초기 데이터를 실어 대기열에 넣는다. 팝업은 <see cref="IPopupArg{TArg}"/>를 구현해야 한다.</summary>
        public void EnqueueWithArg<TArg>(PopupKey key, TArg arg, int layer = 0, int priority = 0)
            => EnqueueCore(key, layer, priority, new QueuedPopupArg<TArg>(arg));

        /// <summary>대기 항목을 전부 버린다. 이미 표시 중인 팝업은 건드리지 않는다.</summary>
        public void Clear() => _entries.Clear();

        private void EnqueueCore(PopupKey key, int layer, int priority, IQueuedPopupArg arg)
        {
            int index = _entries.Count;
            while (index > 0 && _entries[index - 1].Priority < priority)
                index--;

            _entries.Insert(index, new Entry(key, layer, priority, arg));

            if (!_draining)
                DrainAsync().Forget();
        }

        private async UniTask DrainAsync()
        {
            _draining = true;
            try
            {
                while (_entries.Count > 0)
                {
                    var entry = _entries[0];
                    _entries.RemoveAt(0);

                    var popup = await _stack.OpenQueuedAsync(entry.Key, entry.Layer, entry.Arg);
                    if (popup == null)
                        continue; // 로드 실패/취소 → 다음 항목으로.

                    _current = popup;

                    if (popup.Phase != PopupPhase.None)
                        await popup.WaitUntilClosedAsync();
                }
            }
            finally
            {
                _draining = false;
                _current = null;
            }
        }
    }
}
