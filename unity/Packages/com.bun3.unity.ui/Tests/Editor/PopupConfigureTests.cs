using System.Collections.Generic;
using System.Threading;
using Bun3.Unity.UI.Popups;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.UI.Editor.Tests
{
    public class PopupConfigureTests
    {
        // Fluent-setter alert popup + result await.
        private sealed class AlertPopup : Popup<bool>
        {
            public string Title;
            public string Desc;
            public int ConfigureCount;
            public PopupPhase PhaseAtConfigure = (PopupPhase)(-1);

            public AlertPopup SetTitle(string title)
            {
                Title = title;
                ConfigureCount++;
                PhaseAtConfigure = Phase;
                return this;
            }

            public AlertPopup SetDesc(string desc)
            {
                Desc = desc;
                return this;
            }

            public void ClickOk()
            {
                SetResult(true);
                Close();
            }
        }

        private readonly List<AlertPopup> _created = new();
        private PopupStack _stack;

        private UniTask<Popup> CreateAlert(PopupKey key, CancellationToken cancellationToken)
        {
            var popup = new GameObject("alert").AddComponent<AlertPopup>();
            _created.Add(popup);
            return UniTask.FromResult<Popup>(popup);
        }

        [SetUp]
        public void SetUp()
        {
            _created.Clear();
            _stack = new PopupStack(CreateAlert);
        }

        [TearDown]
        public void TearDown()
        {
            _stack.Dispose();
            foreach (var popup in _created)
            {
                if (popup)
                    Object.DestroyImmediate(popup.gameObject);
            }
        }

        [Test]
        public void Configure_RunsAfterLoad_BeforeOpen()
        {
            var popup = _stack.PushAsync<AlertPopup>(p => p.SetTitle("confirm").SetDesc("body"))
                .GetAwaiter().GetResult();

            Assert.AreEqual("confirm", popup.Title);
            Assert.AreEqual("body", popup.Desc);
            Assert.AreEqual(PopupPhase.None, popup.PhaseAtConfigure, "Configure must run before stack insertion.");
            Assert.AreEqual(PopupPhase.Open, popup.Phase);
        }

        [Test]
        public void ConfigureWithResult_AlertFlow()
        {
            // Show().SetDesc(...).WaitResultAsync() style alert flow.
            var task = _stack.PushForResultAsync<AlertPopup, bool>(
                p => p.SetTitle("choose reward").SetDesc("are you sure?"));

            _created[0].ClickOk();

            Assert.IsTrue(task.GetAwaiter().GetResult());
        }

        [Test]
        public void ConfigureWithResult_ClosedWithoutChoice_IsDefault()
        {
            var task = _stack.PushForResultAsync<AlertPopup, bool>(p => p.SetTitle("t"));

            _stack.Pop(); // Like back/dim.

            Assert.IsFalse(task.GetAwaiter().GetResult());
        }

        [Test]
        public void Configure_Focus_ReappliesToExisting()
        {
            _stack.Push<AlertPopup>(p => p.SetTitle("first-config"));

            var focused = _stack.PushAsync<AlertPopup>(p => p.SetTitle("re-config"),
                duplicate: PopupDuplicatePolicy.Focus).GetAwaiter().GetResult();

            Assert.AreEqual(1, _created.Count, "Must reuse, not create anew.");
            Assert.AreSame(_created[0], focused);
            Assert.AreEqual("re-config", focused.Title, "Focus must re-apply the configure chain to the existing instance.");
            Assert.AreEqual(2, focused.ConfigureCount);
        }

        [Test]
        public void Configure_Queue_AppliedWhenShown()
        {
            _stack.Push<AlertPopup>(p => p.SetTitle("first"));

            _stack.Push<AlertPopup>(p => p.SetTitle("queued"),
                duplicate: PopupDuplicatePolicy.Queue);

            Assert.AreEqual(1, _stack.Count);

            _stack.Pop();

            Assert.AreEqual("queued", _created[1].Title, "Configure must apply at queue display time.");
        }
    }
}
