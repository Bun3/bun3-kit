using System;
using System.Collections.Generic;
using System.Threading;
using Bun3.Unity.Core.Utils;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.UI.Toasts
{
    /// <summary>
    /// 토스트 순차 표시 큐(레거시 Toast 정적 큐 대응). 한 번에 하나씩 표시하고,
    /// 대기 상한 초과분은 버리며, 선택적 중복 억제와 강제 표시(<c>force</c>)를 지원한다.
    /// 팝업 스택과 무관하게 흐른다 — back 키/딤/스택 정렬에 참여하지 않는다.
    /// </summary>
    /// <remarks>
    /// 뷰 인스턴스는 팩토리로 1회 생성해 재사용한다(SetActive 토글). 표시 흐름:
    /// <c>OnData → PlayShowAsync → WaitAsync(duration) → PlayHideAsync → 다음</c>.
    /// 저빈도 UI 경로라 대기열 항목 보관 등의 소량 할당을 허용한다.
    /// </remarks>
    public sealed class ToastQueue<TData> : IDisposable
    {
        /// <summary>토스트 뷰를 만든다(1회). 로딩 방식·부모 배치는 게임 몫.</summary>
        public delegate UniTask<ToastView<TData>> ViewFactory(CancellationToken cancellationToken);

        private readonly struct Entry
        {
            public readonly TData Data;
            public readonly float Duration;

            public Entry(TData data, float duration)
            {
                Data = data;
                Duration = duration;
            }
        }

        private readonly ViewFactory _factory;
        private readonly float _defaultDuration;
        private readonly int _capacity;
        private readonly IEqualityComparer<TData> _duplicateComparer;

        private readonly List<Entry> _pending = new();
        private ToastView<TData> _view;
        private CancellationTokenSource _lifetime = new();
        private UniTaskCompletionSource _skip;
        private TData _currentData;
        private bool _hasCurrent;
        private bool _draining;
        private bool _disposed;

        /// <param name="factory">뷰 생성(1회 호출 후 재사용).</param>
        /// <param name="defaultDuration">기본 표시 시간(초).</param>
        /// <param name="capacity">대기 상한 — 초과 요청은 버려진다(레거시 최대 개수 대응).</param>
        /// <param name="duplicateComparer">주면 표시 중·대기 중과 같은 데이터를 억제한다. null이면 억제 없음.</param>
        public ToastQueue(ViewFactory factory, float defaultDuration = 2f, int capacity = 10,
            IEqualityComparer<TData> duplicateComparer = null)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _defaultDuration = defaultDuration;
            _capacity = capacity;
            _duplicateComparer = duplicateComparer;
        }

        /// <summary>표시를 기다리는 항목 수.</summary>
        public int PendingCount => _pending.Count;

        /// <summary>지금 표시 중인지.</summary>
        public bool IsShowing => _hasCurrent;

        /// <summary>
        /// 토스트를 요청한다. 표시 가능하면 즉시, 아니면 순서를 기다린다.
        /// </summary>
        /// <param name="data">표시 데이터.</param>
        /// <param name="duration">표시 시간(초). 음수면 기본값.</param>
        /// <param name="force">true면 대기열 맨 앞에 끼어들고, 표시 중인 토스트는 즉시 숨김으로 넘어간다.</param>
        /// <returns>수용 여부 — 중복 억제·대기 상한으로 버려지면 false.</returns>
        public bool Show(TData data, float duration = -1f, bool force = false)
        {
            ThrowIfDisposed();

            if (duration < 0f)
                duration = _defaultDuration;

            if (_duplicateComparer != null && IsDuplicate(data))
                return false;

            if (_pending.Count >= _capacity)
            {
                if (!force)
                    return false;

                _pending.RemoveAt(_pending.Count - 1); // 끼어들기 — 가장 늦은 대기분을 버린다.
            }

            if (force)
            {
                _pending.Insert(0, new Entry(data, duration));
                _skip?.TrySetResult(); // 표시 중이면 유지 시간을 건너뛰고 숨김으로.
            }
            else
            {
                _pending.Add(new Entry(data, duration));
            }

            if (!_draining)
                DrainAsync().Forget();

            return true;
        }

        /// <summary>대기 항목을 전부 버린다. 표시 중인 토스트는 그대로 끝난다.</summary>
        public void Clear() => _pending.Clear();

        /// <summary>대기 정리 + 진행 취소 + 뷰 파괴. 이후 <see cref="Show"/>는 예외.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _pending.Clear();
            _lifetime.Cancel();
            _lifetime.Dispose();

            if (_view)
                _view.gameObject.SafeDestroy();
        }

        private bool IsDuplicate(TData data)
        {
            if (_hasCurrent && _duplicateComparer.Equals(_currentData, data))
                return true;

            for (int i = 0; i < _pending.Count; i++)
            {
                if (_duplicateComparer.Equals(_pending[i].Data, data))
                    return true;
            }

            return false;
        }

        private async UniTask DrainAsync()
        {
            _draining = true;
            try
            {
                var token = _lifetime.Token;

                while (_pending.Count > 0 && !token.IsCancellationRequested)
                {
                    var entry = _pending[0];
                    _pending.RemoveAt(0);

                    var view = await GetViewAsync(token);
                    if (view == null)
                        break; // 팩토리 실패/취소 — 대기분은 다음 Show에서 재시도된다.

                    _currentData = entry.Data;
                    _hasCurrent = true;
                    _skip = new UniTaskCompletionSource();

                    try
                    {
                        view.gameObject.SetActive(true);
                        view.OnData(entry.Data);
                        await view.PlayShowAsync(token);
                        await UniTask.WhenAny(view.WaitAsync(entry.Duration, token), _skip.Task);
                        await view.PlayHideAsync(token);
                    }
                    catch (OperationCanceledException)
                    {
                        break; // Dispose — 정리로 넘어간다.
                    }
                    finally
                    {
                        _hasCurrent = false;
                        _currentData = default;
                        _skip = null;

                        if (view)
                            view.gameObject.SetActive(false);
                    }
                }
            }
            finally
            {
                _draining = false;
            }
        }

        private async UniTask<ToastView<TData>> GetViewAsync(CancellationToken cancellationToken)
        {
            if (_view)
                return _view;

            try
            {
                _view = await _factory(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            if (_view)
                _view.gameObject.SetActive(false);

            return _view;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ToastQueue<TData>));
        }
    }
}
