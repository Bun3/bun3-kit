using System.Collections.Generic;
using System.Threading;
using Bun3.Unity.UI.Popups;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.UI.Editor.Tests
{
    /// <summary>
    /// Common base for EditMode popup stack tests. Transitions are driven by manually completed
    /// <see cref="UniTaskCompletionSource"/>s, so assertions run synchronously without pumping the player loop.
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

            protected override UniTask PlayShowAsync(CancellationToken cancellationToken)
                => OpenSource?.Task ?? UniTask.CompletedTask;

            protected override UniTask PlayHideAsync(CancellationToken cancellationToken)
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

        /// <summary>When true, factory-created popups get a manually completed open source.</summary>
        protected bool PendingOpen;

        /// <summary>When true, factory-created popups get a manually completed close source.</summary>
        protected bool PendingClose;

        /// <summary>When true, factory-created popups get a dim child object.</summary>
        protected bool WithDim;

        /// <summary>When true, also enables dim-click close (<see cref="Popup.CloseOnDimClick"/>).</summary>
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
