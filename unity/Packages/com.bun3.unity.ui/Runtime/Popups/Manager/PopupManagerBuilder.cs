using System;
using UnityEngine;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// <see cref="PopupManager"/> 조립기. 모든 게임이 반복하는 배선(풀→스택→라우터→정렬)을
    /// 한 곳에 모은다. DI 컨테이너를 쓰는 게임은 이 빌더 없이 각 조각을 직접 등록해도 된다 —
    /// 전부 생성자 주입 POCO다.
    /// </summary>
    /// <example><code>
    /// PopupManager.Instance = new PopupManagerBuilder(LoadPopupAsync)
    ///     .UsePool()
    ///     .UseBackKey(gameObject, onUnhandled: ShowQuitDialog)
    ///     .UseSiblingArranger()
    ///     .Build();
    /// PopupManager.Instance.Push&lt;ShopPopup&gt;();
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
