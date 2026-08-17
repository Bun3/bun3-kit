using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// 도메인 무관 팝업/모달 스택. push/pop, 레이어 정렬, 중복 정책, 순차 대기열,
    /// back 키 라우팅을 담당한다. 팝업 인스턴스 생성/해제와 표현(부모 배치, 딤, 사운드)은
    /// <see cref="PopupFactory"/>/<see cref="PopupReleaser"/>/<see cref="Opened"/> 훅으로 게임이 채운다.
    /// </summary>
    /// <remarks>
    /// MonoBehaviour가 아니다 — 씬 구조를 강제하지 않고 게임이 생성해 보관한다.
    /// push/pop/back 경로는 클로저·LINQ·문자열 할당이 없다. 필요하면
    /// <see cref="PopupBackKeyRouter"/>를 붙여 back 키를 자동 라우팅한다.
    /// </remarks>
    public sealed class PopupStack : IDisposable
    {
        private readonly struct QueuedPopup
        {
            public readonly PopupKey Key;
            public readonly int Layer;

            public QueuedPopup(PopupKey key, int layer)
            {
                Key = key;
                Layer = layer;
            }
        }

        private readonly PopupFactory _factory;
        private readonly PopupReleaser _releaser;

        // 정렬 불변식: (Layer 오름차순, 삽입 순서). 끝 = 최상단.
        private readonly List<PopupBehaviour> _stack = new();
        private readonly Queue<QueuedPopup> _queue = new();
        private readonly List<PopupKey> _loading = new();

        private CancellationTokenSource _lifetime = new();
        private bool _disposed;

        /// <summary>팝업이 스택에 삽입된 직후(열림 연출 시작 전) 발화. z-order/딤 연출 연결 지점.</summary>
        public event Action<PopupBehaviour> Opened;

        /// <summary>팝업이 스택에서 제거된 직후(해제 직전) 발화.</summary>
        public event Action<PopupBehaviour> Closed;

        /// <summary>열려 있거나 전이 중인 팝업 수.</summary>
        public int Count => _stack.Count;

        /// <summary>최상단 팝업. 비어 있으면 null.</summary>
        public PopupBehaviour Top => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;

        /// <summary>순차 대기열에서 표시를 기다리는 항목 수.</summary>
        public int QueuedCount => _queue.Count;

        /// <param name="factory">키 → 팝업 인스턴스. 로딩 방식은 게임 몫.</param>
        /// <param name="releaser">닫힌 인스턴스 해제. 기본은 GameObject 파괴.</param>
        public PopupStack(PopupFactory factory, PopupReleaser releaser = null)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _releaser = releaser ?? DestroyPopup;
        }

        /// <summary>해당 키의 팝업이 열려 있는지(닫힘 연출 중 제외) 확인한다.</summary>
        public bool IsOpen(PopupKey key)
        {
            for (int i = 0; i < _stack.Count; i++)
            {
                if (_stack[i].Key == key && _stack[i].Phase != PopupPhase.Closing)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 팝업을 연다. 로딩/열림 연출 완료를 기다리지 않는 fire-and-forget 버전.
        /// 팩토리 예외는 UniTask 미관찰 예외 핸들러로 표면화된다.
        /// </summary>
        public void Push(PopupKey key, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore)
        {
            ThrowIfDisposed();
            PushAsync(key, layer, duplicate).Forget();
        }

        /// <summary>
        /// 팝업을 열고 열림 연출 완료까지 대기한다. 같은 키가 이미 열려 있거나 로딩 중이면
        /// <paramref name="duplicate"/> 정책을 따른다.
        /// </summary>
        public async UniTask PushAsync(PopupKey key, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore)
        {
            ThrowIfDisposed();

            if (IsOpen(key) || IsLoading(key))
            {
                switch (duplicate)
                {
                    case PopupDuplicatePolicy.Ignore:
                        return;

                    case PopupDuplicatePolicy.Queue:
                        Enqueue(key, layer);
                        return;

                    case PopupDuplicatePolicy.Replace:
                        // ponytail: 로딩 중인 같은 키 인스턴스는 건드리지 않는다(동시 로딩 허용).
                        // 필요해지면 로딩 취소로 확장.
                        CloseAllOf(key);
                        break;
                }
            }

            await OpenAsync(key, layer);
        }

        /// <summary>
        /// 순차 대기열에 넣는다. 스택이 완전히 비면(로딩 중 포함 없음) 머리부터 하나씩 표시되고,
        /// 닫히면 다음이 표시된다. 보상 연출처럼 겹치지 않고 차례로 보여야 하는 팝업용.
        /// </summary>
        public void Enqueue(PopupKey key, int layer = 0)
        {
            ThrowIfDisposed();
            _queue.Enqueue(new QueuedPopup(key, layer));
            TryDrainQueue();
        }

        /// <summary>
        /// back 키(ESC/Android back)를 최상단 팝업에 라우팅한다.
        /// </summary>
        /// <returns>
        /// 키를 소비했으면 true. 스택이 비어 있을 때만 false — 게임이 종료 확인 등
        /// 다음 처리를 이어간다. 최상단이 전이 중이면 아무것도 하지 않고 소비하며(연출 중 입력 무시),
        /// <see cref="PopupBehaviour.OnBackRequested"/>가 false를 돌려주면 닫지 않고 소비만 한다.
        /// </returns>
        public bool HandleBack()
        {
            var top = Top;
            if (top == null)
                return false;

            if (top.Phase != PopupPhase.Open)
                return true;

            if (!top.OnBackRequested())
                return true;

            Close(top);
            return true;
        }

        /// <summary>최상단의 닫히는 중이 아닌 팝업을 닫는다.</summary>
        public void Pop()
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                if (_stack[i].Phase != PopupPhase.Closing)
                {
                    Close(_stack[i]);
                    return;
                }
            }
        }

        /// <summary>팝업을 닫는다. 닫힘 연출 완료를 기다리지 않는 fire-and-forget 버전.</summary>
        public void Close(PopupBehaviour popup) => CloseAsync(popup).Forget();

        /// <summary>
        /// 팝업을 닫고 닫힘 연출·해제 완료까지 대기한다. 이 스택 소속이 아니거나 이미 닫히는
        /// 중이면 무시. 열림 연출 중이면 닫기를 예약만 하고 즉시 반환한다(열림 완료 후 닫힘) —
        /// 실제 닫힘까지 기다리려면 <see cref="PopupBehaviour.WaitUntilClosedAsync"/>를 쓸 것.
        /// </summary>
        public async UniTask CloseAsync(PopupBehaviour popup)
        {
            if (popup == null || popup.Stack != this || popup.Phase == PopupPhase.Closing)
                return;

            if (popup.Phase == PopupPhase.Opening)
            {
                popup.CloseRequested = true;
                return;
            }

            popup.SetPhase(PopupPhase.Closing);

            try
            {
                await popup.PlayCloseAsync(_lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                // Clear/Dispose가 해제를 맡는다.
            }

            if (popup.Stack != this || popup.Phase != PopupPhase.Closing)
                return;

            _stack.Remove(popup);
            popup.Detach();
            Closed?.Invoke(popup);
            _releaser(popup);

            TryDrainQueue();
        }

        /// <summary>
        /// 연출을 생략하고 전부 즉시 해제한다. 진행 중인 로딩/연출은 취소되고,
        /// 순차 대기열도 비운다. 씬 전환 등 강제 정리용.
        /// </summary>
        public void Clear()
        {
            var lifetime = _lifetime;
            _lifetime = new CancellationTokenSource();
            lifetime.Cancel();
            lifetime.Dispose();

            _queue.Clear();
            // _loading은 비우지 않는다 — 진행 중 로드의 finally가 자기 항목을 제거한다.
            // 토큰이 취소됐으므로 도착한 인스턴스는 스택에 들어오지 못하고 바로 해제된다.

            if (_stack.Count == 0)
                return;

            // 해제 콜백이 재-Push할 수 있어 스냅샷을 뜬다. (저빈도 경로 — 할당 허용)
            var popups = _stack.ToArray();
            _stack.Clear();

            for (int i = popups.Length - 1; i >= 0; i--)
            {
                var popup = popups[i];
                popup.Detach();
                Closed?.Invoke(popup);
                _releaser(popup);
            }
        }

        /// <summary><see cref="Clear"/> 후 스택을 더 쓸 수 없게 만든다.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            Clear();
            _disposed = true;
            _lifetime.Dispose();
        }

        private async UniTask OpenAsync(PopupKey key, int layer)
        {
            var token = _lifetime.Token;

            PopupBehaviour popup;
            _loading.Add(key);
            try
            {
                popup = await _factory(key, token);
            }
            finally
            {
                _loading.Remove(key);
            }

            // 팩토리는 로드 실패를 null로 알릴 수 있다.
            if (popup == null)
            {
                TryDrainQueue();
                return;
            }

            if (token.IsCancellationRequested)
            {
                // Clear/Dispose 이후 도착한 인스턴스 — 스택에 넣지 않고 바로 돌려보낸다.
                _releaser(popup);
                return;
            }

            InsertSorted(popup, layer);
            popup.Attach(this, key, layer);
            Opened?.Invoke(popup);

            try
            {
                await popup.PlayOpenAsync(token);
            }
            catch (OperationCanceledException)
            {
                // Clear/Dispose가 해제를 맡는다.
            }

            if (popup.Stack != this || popup.Phase != PopupPhase.Opening)
                return;

            popup.SetPhase(PopupPhase.Open);

            if (popup.CloseRequested)
                await CloseAsync(popup);
        }

        private void InsertSorted(PopupBehaviour popup, int layer)
        {
            int index = _stack.Count;
            while (index > 0 && _stack[index - 1].Layer > layer)
                index--;

            _stack.Insert(index, popup);
        }

        private void CloseAllOf(PopupKey key)
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                var popup = _stack[i];
                if (popup.Key == key && popup.Phase != PopupPhase.Closing)
                    Close(popup);
            }
        }

        private bool IsLoading(PopupKey key)
        {
            for (int i = 0; i < _loading.Count; i++)
            {
                if (_loading[i] == key)
                    return true;
            }

            return false;
        }

        private void TryDrainQueue()
        {
            if (_stack.Count > 0 || _loading.Count > 0 || _queue.Count == 0)
                return;

            var next = _queue.Dequeue();
            OpenAsync(next.Key, next.Layer).Forget();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PopupStack));
        }

        private static void DestroyPopup(PopupBehaviour popup)
        {
            if (!popup)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(popup.gameObject);
            else
                UnityEngine.Object.DestroyImmediate(popup.gameObject);
        }
    }
}
