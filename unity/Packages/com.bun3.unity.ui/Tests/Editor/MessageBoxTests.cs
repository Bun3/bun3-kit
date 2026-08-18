using System.Collections.Generic;
using System.Threading;
using Bun3.Unity.UI.Popups;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.UI.Editor.Tests
{
    public class MessageBoxTests
    {
        private sealed class TestMessageBox : MessageBoxPopup
        {
            public MessageBoxRequest LastRequest;

            public void Click(int buttonIndex) => Choose(buttonIndex);

            protected override void OnRequest(in MessageBoxRequest request) => LastRequest = request;
        }

        private readonly List<TestMessageBox> _created = new();
        private PopupStack _stack;

        private UniTask<Popup> CreateMessageBox(PopupKey key, CancellationToken cancellationToken)
        {
            var popup = new GameObject("msgbox").AddComponent<TestMessageBox>();
            _created.Add(popup);
            return UniTask.FromResult<Popup>(popup);
        }

        [SetUp]
        public void SetUp()
        {
            _created.Clear();
            _stack = new PopupStack(CreateMessageBox);
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
        public void ShowMessageBox_BindsRequest_ReturnsClickedIndex()
        {
            var task = _stack.ShowMessageBoxAsync<TestMessageBox>("제목", "본문", "예", "아니오", "나중에");
            var box = _created[0];

            Assert.AreEqual("제목", box.LastRequest.Title);
            Assert.AreEqual(3, box.LastRequest.Buttons.Length);

            box.Click(2);

            Assert.AreEqual(2, task.GetAwaiter().GetResult());
        }

        [Test]
        public void ClosedWithoutButton_ReturnsMinusOne()
        {
            var task = _stack.ShowMessageBoxAsync<TestMessageBox>("t", "m", "확인");

            _stack.Pop(); // back/딤 격 — 버튼 없이 닫힘

            Assert.AreEqual(-1, task.GetAwaiter().GetResult());
        }

        [Test]
        public void Confirm_FirstButtonOnly_IsTrue()
        {
            var confirm = _stack.ConfirmAsync<TestMessageBox>("t", "m", "확인", "취소");
            _created[0].Click(0);
            Assert.IsTrue(confirm.GetAwaiter().GetResult());

            var cancel = _stack.ConfirmAsync<TestMessageBox>("t", "m", "확인", "취소");
            _created[1].Click(1);
            Assert.IsFalse(cancel.GetAwaiter().GetResult());
        }
    }
}
