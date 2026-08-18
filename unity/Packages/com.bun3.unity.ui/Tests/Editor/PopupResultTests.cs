using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Bun3.Unity.UI.Popups;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.UI.Editor.Tests
{
    public class PopupResultTests : PopupStackTestFixture
    {
        private sealed class ChoicePopup : Popup<int>
        {
            public void Choose(int value)
            {
                SetResult(value);
                Close();
            }
        }

        private readonly List<ChoicePopup> _choices = new();
        private PopupStack _resultStack;
        private PopupPool _pool;

        private UniTask<Popup> CreateChoicePopup(PopupKey key, CancellationToken cancellationToken)
        {
            var popup = new GameObject($"choice-{key.Name}").AddComponent<ChoicePopup>();
            _choices.Add(popup);
            return UniTask.FromResult<Popup>(popup);
        }

        [SetUp]
        public void SetUpResultStack()
        {
            _choices.Clear();
            _pool = new PopupPool(CreateChoicePopup);
            _resultStack = new PopupStack(_pool.RentAsync, _pool.Return);
        }

        [TearDown]
        public void TearDownResultStack()
        {
            _resultStack.Dispose();
            _pool.Dispose();

            foreach (var popup in _choices)
            {
                if (popup)
                    Object.DestroyImmediate(popup.gameObject);
            }
        }

        [Test]
        public void SetResultThenClose_DeliversValue()
        {
            _resultStack.Push("p1");
            var popup = (ChoicePopup)_resultStack.Top;
            var waiting = popup.WaitForResultAsync(defaultResult: -1);

            popup.Choose(7);

            Assert.AreEqual(7, waiting.GetAwaiter().GetResult());
        }

        [Test]
        public void ClosedWithoutResult_ReturnsDefault()
        {
            _resultStack.Push("p1");
            var popup = (ChoicePopup)_resultStack.Top;
            var waiting = popup.WaitForResultAsync(defaultResult: -1);

            _resultStack.Pop(); // back/취소 격 — SetResult 없이 닫힘

            Assert.AreEqual(-1, waiting.GetAwaiter().GetResult());
        }

        [Test]
        public void PushForResultAsync_EndToEnd()
        {
            var task = _resultStack.PushForResultAsync<int>("p1", defaultResult: -1);

            ((ChoicePopup)_resultStack.Top).Choose(42);

            Assert.AreEqual(42, task.GetAwaiter().GetResult());
        }

        [Test]
        public void TypeAsKey_ConstraintChecksResultType_AtCompileTime()
        {
            // where TPopup : Popup<TResult> — 잘못된 결과 타입 조합은 컴파일 에러가 된다.
            var task = _resultStack.PushForResultAsync<ChoicePopup, int>(defaultResult: -1);

            Assert.IsTrue(_resultStack.IsOpen<ChoicePopup>(), "타입 키의 기본 이름은 클래스 이름이다.");
            Assert.IsTrue(_resultStack.IsOpen("ChoicePopup"), "데이터 경로 문자열과 같은 키로 판정된다.");

            ((ChoicePopup)_resultStack.Top).Choose(9);

            Assert.AreEqual(9, task.GetAwaiter().GetResult());
        }

        [Test]
        public void TypeAsKey_PushAsyncReturnsTypedInstance()
        {
            var popup = _resultStack.PushAsync<ChoicePopup>().GetAwaiter().GetResult();

            Assert.IsNotNull(popup);
            Assert.AreSame(_resultStack.Top, popup);
            Assert.AreEqual("ChoicePopup", popup.Key.Name);
        }

        [Test]
        public void TypeAsKey_VariantName_IsDistinctPopup()
        {
            _resultStack.Push<ChoicePopup>();
            _resultStack.Push<ChoicePopup>("ChoiceVariant"); // 같은 클래스, 다른 프리팹 격

            Assert.AreEqual(2, _resultStack.Count, "변형 이름은 별도 팝업으로 식별돼야 한다.");
            Assert.IsTrue(_resultStack.IsOpen<ChoicePopup>("ChoiceVariant"));
            Assert.IsFalse(_resultStack.IsOpen("SomethingElse"));
        }

        [Test]
        public void PushForResultAsync_WrongPopupType_LogsErrorAndReturnsDefault()
        {
            LogAssert.Expect(LogType.Error, new Regex("Popup<Int32>"));

            // 픽스처 기본 Stack의 TestPopup은 Popup<int>가 아니다.
            var task = Stack.PushForResultAsync<int>("p1", defaultResult: -1);

            Assert.AreEqual(-1, task.GetAwaiter().GetResult());
        }

        [Test]
        public void PoolReuse_ResetsResultPerSession()
        {
            _pool.MarkPooled("p1");

            _resultStack.Push("p1");
            var popup = (ChoicePopup)_resultStack.Top;
            popup.Choose(5); // 결과 남기고 닫힘 → 풀 반납

            _resultStack.Push("p1"); // 같은 인스턴스 재사용
            Assert.AreSame(popup, _resultStack.Top);
            var waiting = popup.WaitForResultAsync(defaultResult: 0);

            _resultStack.Pop(); // 이번 세션은 결과 없이 닫힘

            Assert.AreEqual(0, waiting.GetAwaiter().GetResult(),
                "이전 세션의 결과(5)가 새 세션으로 새면 안 된다.");
        }
    }
}
