using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// 팝업 인스턴스 풀 + 프리로드. 실제 로딩만 게임이 델리게이트로 주면,
    /// <see cref="RentAsync"/>/<see cref="Return"/>이 <see cref="PopupFactory"/>/<see cref="PopupReleaser"/>
    /// 시그니처와 맞아 <see cref="PopupStack"/>에 그대로 꽂힌다.
    /// </summary>
    /// <example><code>
    /// var pool = new PopupPool(LoadPopupAsync);
    /// var stack = new PopupStack(pool.RentAsync, pool.Return);
    /// await pool.PreloadAsync(PopupId.Shop);       // 로비 진입 시 미리 로드 + 풀 대상 등록
    /// </code></example>
    /// <remarks>
    /// 풀 대상은 옵트인이다: <see cref="PreloadAsync"/> 또는 <see cref="MarkPooled"/>로 등록된
    /// 키만 <see cref="Return"/> 시 풀에 반납(비활성화)되고, 나머지는 파괴된다.
    /// 반납은 비활성화만 한다 — 트랜스폼/상태 원복이 필요하면 게임의
    /// <see cref="Popup"/> 쪽에서 처리한다(매 오픈마다 <see cref="IPopupArg{TArg}"/>가
    /// 다시 전달되므로 재초기화 지점은 그쪽이 자연스럽다).
    /// </remarks>
    public sealed class PopupPool : IDisposable
    {
        private readonly PopupFactory _loader;
        private readonly Dictionary<PopupKey, Queue<Popup>> _pooled = new();
        private readonly Dictionary<Popup, PopupKey> _rented = new();
        private bool _disposed;

        /// <param name="loader">키 → 새 인스턴스. 로딩 방식(Resources/Addressables)은 게임 몫.</param>
        public PopupPool(PopupFactory loader)
            => _loader = loader ?? throw new ArgumentNullException(nameof(loader));

        /// <summary>풀 대상 키로 등록한다(인스턴스 프리로드 없이). 등록 안 된 키는 반납 시 파괴된다.</summary>
        public void MarkPooled(PopupKey key)
        {
            if (!_pooled.ContainsKey(key))
                _pooled[key] = new Queue<Popup>();
        }

        /// <summary>키를 풀 대상으로 등록하고 인스턴스를 미리 만들어 비활성 상태로 쌓아 둔다.</summary>
        public async UniTask PreloadAsync(PopupKey key, int count = 1,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            MarkPooled(key);

            for (int i = 0; i < count; i++)
            {
                var popup = await _loader(key, cancellationToken);
                if (popup == null)
                    return;

                popup.gameObject.SetActive(false);
                _pooled[key].Enqueue(popup);
            }
        }

        /// <summary>
        /// 풀에서 꺼내거나(활성화해서) 없으면 로더로 새로 만든다.
        /// <see cref="PopupFactory"/>로 스택에 꽂는 용도.
        /// </summary>
        public async UniTask<Popup> RentAsync(PopupKey key, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            if (_pooled.TryGetValue(key, out var queue))
            {
                while (queue.Count > 0)
                {
                    var pooled = queue.Dequeue();
                    if (!pooled)
                        continue; // 외부에서 파괴된 항목은 건너뛰며 정리.

                    pooled.gameObject.SetActive(true);
                    _rented[pooled] = key;
                    return pooled;
                }
            }

            var popup = await _loader(key, cancellationToken);
            if (popup != null)
                _rented[popup] = key;

            return popup;
        }

        /// <summary>
        /// 인스턴스를 되돌려받는다: 풀 대상 키면 비활성화 후 보관, 아니면 파괴.
        /// <see cref="PopupReleaser"/>로 스택에 꽂는 용도.
        /// </summary>
        public void Return(Popup popup)
        {
            if (popup == null)
                return;

            bool wasRented = _rented.Remove(popup, out var key);

            if (!popup)
                return; // Unity 쪽에서 이미 파괴됨.

            if (!_disposed && wasRented && _pooled.TryGetValue(key, out var queue))
            {
                popup.gameObject.SetActive(false);
                queue.Enqueue(popup);
                return;
            }

            Destroy(popup);
        }

        /// <summary>풀에 보관 중인 인스턴스를 전부 파괴한다. 대여 중인 인스턴스는 건드리지 않는다.</summary>
        public void Clear()
        {
            foreach (var queue in _pooled.Values)
            {
                while (queue.Count > 0)
                    Destroy(queue.Dequeue());
            }
        }

        /// <summary><see cref="Clear"/> 후 풀을 더 쓸 수 없게 만든다. 이후 반납되는 인스턴스는 파괴된다.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            Clear();
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PopupPool));
        }

        private static void Destroy(Popup popup)
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
