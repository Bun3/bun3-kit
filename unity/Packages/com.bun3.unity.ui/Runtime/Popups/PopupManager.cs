using System;
using UnityEngine;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// 팝업 조각들(<see cref="PopupStack"/> + 선택적 풀/back 키 라우터/sibling 정렬)의
    /// 조립 결과. <see cref="PopupManagerBuilder"/>로 만들며, <see cref="Dispose"/>가
    /// 해제 순서를 보장한다. 각 조각은 프로퍼티로 그대로 노출된다 — 래핑 API는 없다.
    /// </summary>
    public sealed class PopupManager : IDisposable
    {
        /// <summary>
        /// 전역 접근 슬롯(선택). 게임 부트스트랩에서
        /// <c>PopupManager.Instance = new PopupManagerBuilder(...).Build();</c>처럼 대입해 두면
        /// 어디서든 <c>PopupManager.Instance.Stack.Push(...)</c>로 쓴다(레거시
        /// <c>GameManager.Get().ShowPopup</c> 대응). <see cref="PopupManagerBuilder.Build"/>가 자동 대입하지 않는
        /// 이유: 씬별/테스트용 다중 매니저를 막지 않기 위해 — 전역으로 쓸지는 게임이 정한다.
        /// 대입된 인스턴스가 <see cref="Dispose"/>되면 슬롯은 자동으로 비워진다.
        /// </summary>
        public static PopupManager Instance { get; set; }

        // Enter Play Mode Options로 도메인 리로드를 껐을 때, 이전 플레이 세션의 인스턴스
        // (이미 파괴된 오브젝트를 가리킬 수 있다)가 다음 세션까지 살아남는 것을 막는다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance() => Instance = null;

        /// <summary>팝업 스택. 항상 존재한다.</summary>
        public PopupStack Stack { get; }

        /// <summary>인스턴스 풀. <see cref="PopupManagerBuilder.UsePool"/>을 썼을 때만. 아니면 null.</summary>
        public PopupPool Pool { get; }

        /// <summary>back 키 라우터. <see cref="PopupManagerBuilder.UseBackKey"/>를 썼을 때만. 아니면 null.</summary>
        public PopupBackKeyRouter BackKeyRouter { get; }

        /// <summary>sibling 정렬 도우미. <see cref="PopupManagerBuilder.UseSiblingArranger"/>를 썼을 때만. 아니면 null.</summary>
        public PopupSiblingArranger Arranger { get; }

        internal PopupManager(PopupStack stack, PopupPool pool,
            PopupBackKeyRouter backKeyRouter, PopupSiblingArranger arranger)
        {
            Stack = stack;
            Pool = pool;
            BackKeyRouter = backKeyRouter;
            Arranger = arranger;
        }

        /// <summary>
        /// 라우터 제거 → 정렬 해지 → 스택 정리 → 풀 파괴 순으로 전부 해제한다.
        /// 이 인스턴스가 <see cref="Instance"/>였다면 슬롯도 비운다.
        /// </summary>
        public void Dispose()
        {
            if (ReferenceEquals(Instance, this))
                Instance = null;

            EditorSafeDestroy.Destroy(BackKeyRouter);

            Arranger?.Dispose();
            Stack.Dispose();
            Pool?.Dispose();
        }
    }

    /// <summary>
    /// <see cref="PopupManager"/> 조립기. 모든 게임이 반복하는 배선(풀→스택→라우터→정렬)을
    /// 한 곳에 모은다. DI 컨테이너를 쓰는 게임은 이 빌더 없이 각 조각을 직접 등록해도 된다 —
    /// 전부 생성자 주입 POCO다.
    /// </summary>
    /// <example><code>
    /// _popups = new PopupManagerBuilder(LoadPopupAsync)
    ///     .UsePool()
    ///     .UseBackKey(gameObject, onUnhandled: ShowQuitDialog)
    ///     .UseSiblingArranger()
    ///     .Build();
    /// _popups.Stack.Push((int)PopupId.Shop);
    /// </code></example>
    public sealed class PopupManagerBuilder
    {
        private readonly PopupFactory _factory;
        private PopupReleaser _releaser;
        private bool _usePool;
        private GameObject _backKeyHost;
        private Action _backUnhandled;
        private bool _useArranger;

        /// <param name="factory">키 → 팝업 인스턴스 로더. 풀을 쓰면 풀의 로더가 된다.</param>
        public PopupManagerBuilder(PopupFactory factory)
            => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

        /// <summary>팩토리를 <see cref="PopupPool"/>로 감싼다. 풀 대상 키는 빌드 후 Pool에 등록.</summary>
        public PopupManagerBuilder UsePool()
        {
            _usePool = true;
            return this;
        }

        /// <summary>커스텀 릴리저. <see cref="UsePool"/>과 함께 쓸 수 없다(풀이 해제를 소유).</summary>
        public PopupManagerBuilder WithReleaser(PopupReleaser releaser)
        {
            _releaser = releaser;
            return this;
        }

        /// <summary>
        /// <paramref name="host"/>에 <see cref="PopupBackKeyRouter"/>를 붙여 ESC/Android back을
        /// 자동 라우팅한다. <paramref name="onUnhandled"/>는 스택이 비어 키를 소비하지 못했을 때
        /// (종료 확인 다이얼로그 등).
        /// </summary>
        public PopupManagerBuilder UseBackKey(GameObject host, Action onUnhandled = null)
        {
            _backKeyHost = host ? host : throw new ArgumentNullException(nameof(host));
            _backUnhandled = onUnhandled;
            return this;
        }

        /// <summary>스택 순서에 맞춰 sibling index를 자동 정렬한다(팝업 전용 부모 전제).</summary>
        public PopupManagerBuilder UseSiblingArranger()
        {
            _useArranger = true;
            return this;
        }

        /// <summary>설정대로 조각들을 만들고 배선해 <see cref="PopupManager"/>를 돌려준다.</summary>
        public PopupManager Build()
        {
            if (_usePool && _releaser != null)
                throw new InvalidOperationException(
                    "UsePool과 WithReleaser는 함께 쓸 수 없다 — 풀이 인스턴스 해제를 소유한다.");

            PopupPool pool = null;
            PopupStack stack;

            if (_usePool)
            {
                pool = new PopupPool(_factory);
                stack = new PopupStack(pool.RentAsync, pool.Return);
            }
            else
            {
                stack = new PopupStack(_factory, _releaser);
            }

            var arranger = _useArranger ? new PopupSiblingArranger(stack) : null;

            PopupBackKeyRouter router = null;
            if (_backKeyHost)
            {
                router = _backKeyHost.AddComponent<PopupBackKeyRouter>();
                router.Stack = stack;
                if (_backUnhandled != null)
                    router.BackUnhandled += _backUnhandled;
            }

            return new PopupManager(stack, pool, router, arranger);
        }
    }
}
