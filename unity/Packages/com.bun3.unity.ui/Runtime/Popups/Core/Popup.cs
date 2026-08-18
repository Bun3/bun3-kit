using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// 게임 팝업 프리팹의 베이스 컴포넌트. <see cref="PopupStack"/>이 생명주기를 소유하며,
    /// 게임은 열림/닫힘 연출과 back 키 반응만 가상 메서드로 구현한다.
    /// </summary>
    /// <remarks>
    /// <see cref="Key"/>/<see cref="Layer"/>/<see cref="Phase"/>는 스택이 설정한다 — 게임은 읽기만.
    /// 인스턴스 생성(<see cref="PopupFactory"/>)과 해제(<see cref="PopupReleaser"/>)는 게임 몫이므로,
    /// 이 클래스는 트랜스폼/캔버스 배치를 일절 건드리지 않는다.
    /// <br/>
    /// partial 구성: 이 파일(생명주기·훅) / CloseScope(닫기 잠금).
    /// </remarks>
    public abstract partial class Popup : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("최상단 팝업일 때만 켤 딤 배경. 딤이 없는 팝업은 비워 둔다.")]
        private GameObject _backgroundDim;

        private UniTaskCompletionSource _closedSource;

        /// <summary>
        /// 딤 배경(레거시 backgroundDimTransform 대응). null이면 딤 없는 팝업.
        /// 스택이 순서 변화 때마다 <b>딤을 가진 팝업 중 최상단</b>의 딤만 켠다 —
        /// 딤 없는 팝업이 그 위에 떠도 이 팝업의 딤이 유지된다.
        /// </summary>
        public GameObject BackgroundDim
        {
            get => _backgroundDim;
            set => _backgroundDim = value;
        }

        /// <summary>이 인스턴스를 연 키. 스택에 속해 있지 않으면 마지막 값이 남는다.</summary>
        public PopupKey Key { get; private set; }

        /// <summary>정렬 레이어. 높을수록 위. 같은 레이어 안에서는 나중 push가 위.</summary>
        public int Layer { get; private set; }

        /// <summary>현재 생명주기 단계.</summary>
        public PopupPhase Phase { get; private set; }

        /// <summary>이 팝업을 소유한 스택. 스택에 속해 있지 않으면 null.</summary>
        public PopupStack Stack { get; private set; }

        /// <summary>열림 연출 중 닫기 요청이 들어와 열림 완료 후 닫아야 함을 표시.</summary>
        internal bool CloseRequested;

        /// <summary>자신을 소유 스택에서 닫는다. 스택에 속해 있지 않으면 무시.</summary>
        public void Close() => Stack?.Close(this);

        /// <summary>
        /// 이 팝업이 닫혀 스택에서 제거될 때까지 대기한다. 이미 닫혀 있으면 즉시 완료.
        /// 확인 다이얼로그 응답 대기, 보상 연출 체인 등에 쓴다.
        /// </summary>
        public UniTask WaitUntilClosedAsync()
        {
            if (Phase == PopupPhase.None)
                return UniTask.CompletedTask;

            _closedSource ??= new UniTaskCompletionSource();
            return _closedSource.Task;
        }

        /// <summary>
        /// 열림 연출 대기 지점. 스택은 이 태스크가 끝나야 <see cref="PopupPhase.Open"/>으로 전이한다.
        /// 기본은 즉시 완료(연출 없음).
        /// </summary>
        protected internal virtual UniTask PlayOpenAsync(CancellationToken cancellationToken)
            => UniTask.CompletedTask;

        /// <summary>
        /// 닫힘 연출 대기 지점. 스택은 이 태스크가 끝나야 인스턴스를 해제한다.
        /// 기본은 즉시 완료(연출 없음).
        /// </summary>
        protected internal virtual UniTask PlayCloseAsync(CancellationToken cancellationToken)
            => UniTask.CompletedTask;

        /// <summary>
        /// back 키(ESC/Android back)가 이 팝업에 라우팅됐을 때. <c>true</c>(기본)면 닫기가
        /// 진행되고, <c>false</c>면 닫기를 거부한다(키 입력 자체는 소비됨).
        /// </summary>
        protected internal virtual bool OnBackRequested() => true;

        /// <summary>
        /// 스택 순서가 바뀔 때마다(열림/닫힘/Focus) 스택이 모든 팝업에 통지하는 지점.
        /// 딤 토글(<see cref="BackgroundDim"/>)은 스택이 이 훅과 별개로 처리하므로,
        /// 여기서는 추가 표현(사운드, 포커스 이동 등)만 얹으면 된다. 기본은 아무것도 안 한다.
        /// </summary>
        /// <param name="stackIndex">스택 내 인덱스(0 = 최하단).</param>
        /// <param name="isTopmost">전체 스택의 최상단인지.</param>
        protected internal virtual void OnStackOrderChanged(int stackIndex, bool isTopmost) { }

        internal void Attach(PopupStack stack, PopupKey key, int layer)
        {
            Stack = stack;
            Key = key;
            Layer = layer;
            Phase = PopupPhase.Opening;
            CloseRequested = false;
            _closeScopeCount = 0; // 풀 재사용 시 이전 세션 잠금이 새 세션을 오염시키지 않게

            OnAttached();
        }

        /// <summary>새 세션 시작(스택 삽입) 시 어셈블리 내부 파생형이 상태를 리셋하는 지점.</summary>
        private protected virtual void OnAttached() { }

        internal void SetPhase(PopupPhase phase) => Phase = phase;

        internal void Detach()
        {
            // 잠금 중 강제 해제(Clear)여도 표현(스피너/레이캐스트 차단)이 켜진 채 남지 않게 통지하고,
            // 세대를 올려 살아남은 이전 세션 스코프의 늦은 Dispose를 무효화한다.
            if (_closeScopeCount > 0)
            {
                _closeScopeCount = 0;
                OnCloseBlockedChanged(false);
            }

            _closeScopeVersion++;

            Phase = PopupPhase.None;
            Stack = null;

            var source = _closedSource;
            _closedSource = null;
            source?.TrySetResult();
        }
    }
}
