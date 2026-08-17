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
        protected sealed class TestPopup : PopupBehaviour
        {
            public UniTaskCompletionSource OpenSource;
            public UniTaskCompletionSource CloseSource;
            public bool RejectBack;
            public int BackRequests;

            protected override UniTask PlayOpenAsync(CancellationToken cancellationToken)
                => OpenSource?.Task ?? UniTask.CompletedTask;

            protected override UniTask PlayCloseAsync(CancellationToken cancellationToken)
                => CloseSource?.Task ?? UniTask.CompletedTask;

            protected override bool OnBackRequested()
            {
                BackRequests++;
                return !RejectBack;
            }
        }

        protected PopupStack Stack;
        protected List<TestPopup> Created;
        protected List<PopupBehaviour> Released;

        /// <summary>true면 팩토리가 만드는 팝업에 수동 완료 열림 소스를 단다.</summary>
        protected bool PendingOpen;

        /// <summary>true면 팩토리가 만드는 팝업에 수동 완료 닫힘 소스를 단다.</summary>
        protected bool PendingClose;

        [SetUp]
        public void SetUp()
        {
            Created = new List<TestPopup>();
            Released = new List<PopupBehaviour>();
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

        protected UniTask<PopupBehaviour> CreatePopup(PopupKey key, CancellationToken cancellationToken)
        {
            var popup = new GameObject($"popup-{key.Value}").AddComponent<TestPopup>();

            if (PendingOpen)
                popup.OpenSource = new UniTaskCompletionSource();
            if (PendingClose)
                popup.CloseSource = new UniTaskCompletionSource();

            Created.Add(popup);
            return UniTask.FromResult<PopupBehaviour>(popup);
        }

        protected void ReleasePopup(PopupBehaviour popup)
        {
            Released.Add(popup);
            if (popup)
                Object.DestroyImmediate(popup.gameObject);
        }
    }
}
