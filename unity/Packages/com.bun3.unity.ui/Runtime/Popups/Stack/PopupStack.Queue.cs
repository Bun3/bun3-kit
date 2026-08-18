using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.UI.Popups
{
    // 순차 대기열 경로: 스택이 완전히 비면 하나씩 표시. (채널형은 PopupQueue 참고)
    public sealed partial class PopupStack
    {
        private readonly struct QueuedPopup
        {
            public readonly PopupKey Key;
            public readonly int Layer;
            public readonly IQueuedPopupArg Arg;

            public QueuedPopup(PopupKey key, int layer, IQueuedPopupArg arg)
            {
                Key = key;
                Layer = layer;
                Arg = arg;
            }
        }

        private readonly Queue<QueuedPopup> _queue = new();

        /// <summary>순차 대기열에서 표시를 기다리는 항목 수.</summary>
        public int QueuedCount => _queue.Count;

        /// <summary>
        /// 순차 대기열에 넣는다. 스택이 완전히 비면(로딩 중 포함 없음) 머리부터 하나씩 표시되고,
        /// 닫히면 다음이 표시된다. 보상 연출처럼 겹치지 않고 차례로 보여야 하는 팝업용.
        /// </summary>
        public void Enqueue(PopupKey key, int layer = 0)
        {
            ThrowIfDisposed();
            EnqueueCore(key, layer, null);
        }

        /// <summary>초기 데이터를 실어 순차 대기열에 넣는다. (저빈도 경로 — 데이터 보관 할당 허용)</summary>
        public void EnqueueWithArg<TArg>(PopupKey key, TArg arg, int layer = 0)
        {
            ThrowIfDisposed();
            EnqueueCore(key, layer, new QueuedPopupArg<TArg>(arg));
        }

        /// <summary><see cref="PopupQueue"/> 전용 진입점 — 중복 정책을 거치지 않고 바로 연다.</summary>
        internal UniTask<Popup> OpenQueuedAsync(PopupKey key, int layer, IQueuedPopupArg arg)
            => arg == null
                ? OpenAsync(key, layer, (byte)0, ArgMode.None)
                : OpenAsync(key, layer, arg, ArgMode.Queued);

        private void EnqueueCore(PopupKey key, int layer, IQueuedPopupArg arg)
        {
            _queue.Enqueue(new QueuedPopup(key, layer, arg));
            TryDrainQueue();
        }

        private void TryDrainQueue()
        {
            if (_stack.Count > 0 || _loading.Count > 0 || _queue.Count == 0)
                return;

            var next = _queue.Dequeue();
            OpenQueuedAsync(next.Key, next.Layer, next.Arg).Forget();
        }
    }
}
