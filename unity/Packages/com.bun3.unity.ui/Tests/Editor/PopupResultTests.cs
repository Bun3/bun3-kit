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

            _resultStack.Pop(); // Like back/cancel — closed without SetResult.

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
            // where TPopup : Popup<TResult> — a wrong result-type pairing is a compile error.
            var task = _resultStack.PushForResultAsync<ChoicePopup, int>(defaultResult: -1);

            Assert.IsTrue(_resultStack.IsOpen<ChoicePopup>(), "A type key's default name is the class name.");
            Assert.IsTrue(_resultStack.IsOpen("ChoicePopup"), "It equals the data-path string key.");

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
            _resultStack.Push<ChoicePopup>("ChoiceVariant"); // Same class, different prefab.

            Assert.AreEqual(2, _resultStack.Count, "A variant name must identify a separate popup.");
            Assert.IsTrue(_resultStack.IsOpen<ChoicePopup>("ChoiceVariant"));
            Assert.IsFalse(_resultStack.IsOpen("SomethingElse"));
        }

        [Test]
        public void PushForResultAsync_WrongPopupType_LogsErrorAndReturnsDefault()
        {
            LogAssert.Expect(LogType.Error, new Regex("Popup<Int32>"));

            // The fixture's default Stack creates TestPopup, which is not a Popup<int>.
            var task = Stack.PushForResultAsync<int>("p1", defaultResult: -1);

            Assert.AreEqual(-1, task.GetAwaiter().GetResult());
        }

        [Test]
        public void PoolReuse_ResetsResultPerSession()
        {
            _pool.MarkPooled("p1");

            _resultStack.Push("p1");
            var popup = (ChoicePopup)_resultStack.Top;
            popup.Choose(5); // Closes with a result → returned to the pool.

            _resultStack.Push("p1"); // Same instance reused.
            Assert.AreSame(popup, _resultStack.Top);
            var waiting = popup.WaitForResultAsync(defaultResult: 0);

            _resultStack.Pop(); // This session closes without a result.

            Assert.AreEqual(0, waiting.GetAwaiter().GetResult(),
                "The previous session's result (5) must not leak into the new session.");
        }
    }
}
