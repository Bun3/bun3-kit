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
        // 레거시 Popup_Alert 스타일: fluent 세터 + 결과 await.
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
            var popup = _stack.PushAsync<AlertPopup>(p => p.SetTitle("확인").SetDesc("내용"))
                .GetAwaiter().GetResult();

            Assert.AreEqual("확인", popup.Title);
            Assert.AreEqual("내용", popup.Desc);
            Assert.AreEqual(PopupPhase.None, popup.PhaseAtConfigure, "구성은 스택 삽입 전에 실행돼야 한다.");
            Assert.AreEqual(PopupPhase.Open, popup.Phase);
        }

        [Test]
        public void ConfigureWithResult_LegacyAlertFlow()
        {
            // 레거시 Popup_Alert.Show().SetDesc(...).WaitResultAsync() 대응 흐름.
            var task = _stack.PushForResultAsync<AlertPopup, bool>(
                p => p.SetTitle("보상 선택").SetDesc("정말?"));

            _created[0].ClickOk();

            Assert.IsTrue(task.GetAwaiter().GetResult());
        }

        [Test]
        public void ConfigureWithResult_ClosedWithoutChoice_IsDefault()
        {
            var task = _stack.PushForResultAsync<AlertPopup, bool>(p => p.SetTitle("t"));

            _stack.Pop(); // back/딤 격

            Assert.IsFalse(task.GetAwaiter().GetResult());
        }

        [Test]
        public void Configure_Focus_ReappliesToExisting()
        {
            _stack.Push<AlertPopup>(p => p.SetTitle("첫 구성"));

            var focused = _stack.PushAsync<AlertPopup>(p => p.SetTitle("재구성"),
                duplicate: PopupDuplicatePolicy.Focus).GetAwaiter().GetResult();

            Assert.AreEqual(1, _created.Count, "새로 만들지 않고 재사용해야 한다.");
            Assert.AreSame(_created[0], focused);
            Assert.AreEqual("재구성", focused.Title, "레거시 GetOrShow().Set체인처럼 재적용돼야 한다.");
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

            Assert.AreEqual("queued", _created[1].Title, "대기열 표시 시점에 구성이 적용돼야 한다.");
        }
    }
}
