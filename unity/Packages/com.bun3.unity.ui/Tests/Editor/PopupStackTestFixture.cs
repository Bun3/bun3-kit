using System.Collections.Generic;
using System.Threading;
using Bun3.Unity.UI.Popups;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.UI.Editor.Tests
{
    /// <summary>
    /// EditMode 팝업 스택 테스트 공통 기반. 전이는 수동 완료
    /// <see cref="UniTaskCompletionSource"/>로 제어해 플레이어 루프 펌핑 없이 동기 검증한다.
    /// </summary>
    public abstract class PopupStackTestFixture
    {
        protected sealed class TestPopup : Popup, IPopupArg<int>, IPopupArg<string>
        {
            public UniTaskCompletionSource OpenSource;
            public UniTaskCompletionSource CloseSource;
            public bool RejectBack;
            public int BackRequests;

            public int ReceivedArg;
            public string ReceivedToken;
            public PopupPhase PhaseAtArg = (PopupPhase)(-1);

            public int BlockedChanges;
            public bool LastBlocked;

            protected override UniTask PlayOpenAsync(CancellationToken cancellationToken)
                => OpenSource?.Task ?? UniTask.CompletedTask;

            protected override UniTask PlayCloseAsync(CancellationToken cancellationToken)
                => CloseSource?.Task ?? UniTask.CompletedTask;

            protected override bool OnBackRequested()
            {
                BackRequests++;
                return !RejectBack;
            }

            public void OnPopupArg(int arg)
            {
                ReceivedArg = arg;
                PhaseAtArg = Phase;
            }

            public void OnPopupArg(string arg) => ReceivedToken = arg;

            public int LastOrderIndex = -1;
            public bool LastIsTopmost;

            protected override void OnStackOrderChanged(int stackIndex, bool isTopmost)
            {
                LastOrderIndex = stackIndex;
                LastIsTopmost = isTopmost;
            }

            public int TopmostGained;
            public int TopmostLost;

            protected override void OnBecameTopmost() => TopmostGained++;

            protected override void OnCovered() => TopmostLost++;

            protected override void OnCloseBlockedChanged(bool blocked)
            {
                BlockedChanges++;
                LastBlocked = blocked;
            }
        }

        protected PopupStack Stack;
        protected List<TestPopup> Created;
        protected List<Popup> Released;

        /// <summary>true면 팩토리가 만드는 팝업에 수동 완료 열림 소스를 단다.</summary>
        protected bool PendingOpen;

        /// <summary>true면 팩토리가 만드는 팝업에 수동 완료 닫힘 소스를 단다.</summary>
        protected bool PendingClose;

        /// <summary>true면 팩토리가 만드는 팝업에 딤 자식 오브젝트를 달아 준다.</summary>
        protected bool WithDim;

        /// <summary>true면 딤 클릭 닫기(<see cref="Popup.CloseOnDimClick"/>)도 켠다.</summary>
        protected bool WithDimClick;

        [SetUp]
        public void SetUp()
        {
            Created = new List<TestPopup>();
            Released = new List<Popup>();
            PendingOpen = false;
            PendingClose = false;
            Stack = new PopupStack(CreatePopup, ReleasePopup);
        }

        [TearDown]
        public void TearDown()
        {
            Stack.Dispose();

            foreach (var popup in Created)
            {
                if (popup)
                    Object.DestroyImmediate(popup.gameObject);
            }
        }

        protected UniTask<Popup> CreatePopup(PopupKey key, CancellationToken cancellationToken)
        {
            var popup = new GameObject($"popup-{key.Name}").AddComponent<TestPopup>();

            if (PendingOpen)
                popup.OpenSource = new UniTaskCompletionSource();
            if (PendingClose)
                popup.CloseSource = new UniTaskCompletionSource();

            if (WithDim)
            {
                var dim = new GameObject("dim");
                dim.transform.SetParent(popup.transform, false);
                popup.BackgroundDim = dim;
                popup.CloseOnDimClick = WithDimClick;
            }

            Created.Add(popup);
            return UniTask.FromResult<Popup>(popup);
        }

        protected void ReleasePopup(Popup popup)
        {
            Released.Add(popup);
            if (popup)
                Object.DestroyImmediate(popup.gameObject);
        }
    }
}
