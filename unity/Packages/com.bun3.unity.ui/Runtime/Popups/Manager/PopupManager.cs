using System;
using Bun3.Unity.Core.Utils;
using UnityEngine;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// 팝업 조각들(<see cref="PopupStack"/> + 선택적 풀/back 키 라우터/sibling 정렬)의
    /// 조립 결과. <see cref="PopupManagerBuilder"/>로 만들며, <see cref="Dispose"/>가
    /// 해제 순서를 보장한다.
    /// </summary>
    /// <remarks>partial 구성: 이 파일(조립·수명·전역 슬롯) / Facade(스택 동작 위임).</remarks>
    public sealed partial class PopupManager : IDisposable
    {
        /// <summary>
        /// 전역 접근 슬롯(선택). 게임 부트스트랩에서
        /// <c>PopupManager.Instance = new PopupManagerBuilder(...).Build();</c>처럼 대입해 두면
        /// 어디서든 <c>PopupManager.Instance.Push(...)</c>로 쓴다(레거시
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

            BackKeyRouter.SafeDestroy();

            Arranger?.Dispose();
            Stack.Dispose();
            Pool?.Dispose();
        }
    }
}
