using System;
using System.Collections.Generic;
using System.Threading;
using Bun3.Unity.Core.Utils;
using UnityEngine;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// 도메인 무관 팝업/모달 스택. push/pop, 레이어 정렬, 중복 정책, 순차 대기열,
    /// back 키 라우팅, 초기 데이터 전달을 담당한다. 팝업 인스턴스 생성/해제와 표현(부모 배치,
    /// 딤, 사운드)은 <see cref="PopupFactory"/>/<see cref="PopupReleaser"/>/<see cref="Opened"/>
    /// 훅으로 게임이 채운다.
    /// </summary>
    /// <remarks>
    /// MonoBehaviour가 아니다 — 씬 구조를 강제하지 않고 게임이 생성해 보관한다.
    /// push/pop/back 경로는 클로저·LINQ·문자열 할당이 없다. 필요하면
    /// <see cref="PopupBackKeyRouter"/>를 붙여 back 키를 자동 라우팅한다.
    /// <br/>
    /// partial 구성: 이 파일(상태·수명·정렬) / Push(열기·중복 정책) / Close(닫기·back) /
    /// Result(결과 대기) / Queue(순차 대기열).
    /// </summary>
    public sealed partial class PopupStack : IDisposable
    {
        private readonly PopupFactory _factory;
        private readonly PopupReleaser _releaser;

        // 정렬 불변식: (Layer 오름차순, 삽입 순서). 끝 = 최상단.
        private readonly List<Popup> _stack = new();
        private readonly List<PopupKey> _loading = new();

        private CancellationTokenSource _lifetime = new();
        private bool _disposed;

        /// <summary>팝업이 스택에 삽입된 직후(열림 연출 시작 전) 발화. z-order/딤 연출 연결 지점.</summary>
        public event Action<Popup> Opened;

        /// <summary>팝업이 스택에서 제거된 직후(해제 직전) 발화.</summary>
        public event Action<Popup> Closed;

        /// <summary><see cref="PopupDuplicatePolicy.Focus"/>로 기존 인스턴스가 최상단에 재사용될 때 발화.</summary>
        public event Action<Popup> Focused;

        /// <summary>열려 있거나 전이 중인 팝업 수.</summary>
        public int Count => _stack.Count;

        /// <summary>
        /// 열린 팝업들의 읽기 전용 뷰. 순서 = 아래→위(끝이 최상단). 라이브 뷰이므로
        /// 열거 중 Push/Close를 부르지 말 것 — 스냅샷이 필요하면 복사해서 쓴다.
        /// </summary>
        public IReadOnlyList<Popup> Popups => _stack;

        /// <summary>최상단 팝업. 비어 있으면 null.</summary>
        public Popup Top => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;

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

        /// <summary>타입 키로 열려 있는지 확인한다.</summary>
        public bool IsOpen<TPopup>(string popupName = null) where TPopup : Popup
            => IsOpen(PopupKey.Of<TPopup>(popupName));

        /// <summary>
        /// 연출을 생략하고 전부 즉시 해제한다. 진행 중인 로딩/연출은 취소되고, 순차 대기열도
        /// 비우며, 닫기 잠금도 무시한다. 씬 전환 등 강제 정리용.
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

        /// <summary>
        /// 구조 변화(열림/닫힘/Focus) 후 전체 팝업에 순서를 통지하고 딤을 갱신한다.
        /// 딤 규칙: <see cref="Popup.BackgroundDim"/>을 가진 팝업 중 최상단만 켠다 —
        /// 딤 없는 팝업이 맨 위여도 그 아래 딤 보유 팝업의 딤이 유지된다.
        /// </summary>
        private void NotifyStackOrderChanged()
        {
            Popup dimOwner = null;
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                if (_stack[i].BackgroundDim)
                {
                    dimOwner = _stack[i];
                    break;
                }
            }

            var top = Top;
            for (int i = 0; i < _stack.Count; i++)
            {
                var popup = _stack[i];

                if (popup.BackgroundDim)
                    popup.BackgroundDim.SetActive(ReferenceEquals(popup, dimOwner));

                popup.OnStackOrderChanged(i, ReferenceEquals(popup, top));
            }
        }

        private void InsertSorted(Popup popup, int layer)
        {
            int index = _stack.Count;
            while (index > 0 && _stack[index - 1].Layer > layer)
                index--;

            _stack.Insert(index, popup);
        }

        private Popup FindTopmostOpen(PopupKey key)
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                if (_stack[i].Key == key && _stack[i].Phase != PopupPhase.Closing)
                    return _stack[i];
            }

            return null;
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

        internal static void DeliverArg<TArg>(Popup popup, TArg arg)
        {
            if (popup is IPopupArg<TArg> receiver)
                receiver.OnPopupArg(arg);
            else
                // 게임 코드 결선 오류 — 저빈도 경로라 문자열 할당 허용.
                Debug.LogError(
                    $"팝업 {popup.GetType().Name}이(가) IPopupArg<{typeof(TArg).Name}>를 구현하지 않아 초기 데이터를 버린다.",
                    popup);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PopupStack));
        }

        private static void DestroyPopup(Popup popup)
        {
            if (popup)
                popup.gameObject.SafeDestroy();
        }
    }
}
