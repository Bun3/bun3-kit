using System;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Window.Tests
{
    public class ClickThroughTests
    {
        private sealed class FakeHitTest : IPointerHitTest
        {
            public bool Result;
            public Vector2 LastQueriedPosition;

            public bool IsHit(Vector2 screenPosition)
            {
                LastQueriedPosition = screenPosition;
                return Result;
            }
        }

        private IPointerHitTest _originalHitTest;
        private Func<Vector2> _originalPointerSource;

        [SetUp]
        public void SetUp()
        {
            _originalHitTest = ClickThrough.HitTest;
            _originalPointerSource = ClickThrough.PointerPositionSource;
        }

        [TearDown]
        public void TearDown()
        {
            ClickThrough.ForceClickThrough = false;
            ClickThrough.AutoByPointer = false;
            ClickThrough.HitTest = _originalHitTest;
            ClickThrough.PointerPositionSource = _originalPointerSource;
            ClickThrough.SetEnabled(false);
        }

#if UNITY_EDITOR
        [Test]
        public void IsSupported_IsFalseInEditor()
        {
            // A click-through editor window could never be clicked again.
            Assert.That(ClickThrough.IsSupported, Is.False);
        }
#endif

        [Test]
        public void SetEnabled_TogglesIsEnabled()
        {
            ClickThrough.SetEnabled(true);
            Assert.That(ClickThrough.IsEnabled, Is.True);

            ClickThrough.SetEnabled(false);
            Assert.That(ClickThrough.IsEnabled, Is.False);
        }

        [Test]
        public void SetEnabled_RaisesEnabledChanged_OncePerTransition()
        {
            var count = 0;
            var last = false;
            Action<bool> handler = value =>
            {
                count++;
                last = value;
            };
            ClickThrough.EnabledChanged += handler;
            try
            {
                ClickThrough.SetEnabled(true);
                ClickThrough.SetEnabled(true);
                Assert.That(count, Is.EqualTo(1));
                Assert.That(last, Is.True);

                ClickThrough.SetEnabled(false);
                Assert.That(count, Is.EqualTo(2));
                Assert.That(last, Is.False);
            }
            finally
            {
                ClickThrough.EnabledChanged -= handler;
            }
        }

        [Test]
        public void ComputePolicy_TruthTable()
        {
            var fake = new FakeHitTest();
            ClickThrough.HitTest = fake;
            ClickThrough.PointerPositionSource = () => new Vector2(12f, 34f);

            // Force wins over everything.
            ClickThrough.ForceClickThrough = true;
            ClickThrough.AutoByPointer = false;
            Assert.That(ClickThrough.ComputePolicy(), Is.True);

            // No force, no auto → interactive window.
            ClickThrough.ForceClickThrough = false;
            Assert.That(ClickThrough.ComputePolicy(), Is.False);

            // Auto: pointer over interactive content → interactive window.
            ClickThrough.AutoByPointer = true;
            fake.Result = true;
            Assert.That(ClickThrough.ComputePolicy(), Is.False);
            Assert.That(fake.LastQueriedPosition, Is.EqualTo(new Vector2(12f, 34f)));

            // Auto: pointer over empty space → click-through.
            fake.Result = false;
            Assert.That(ClickThrough.ComputePolicy(), Is.True);

            // Auto with no hit test → nothing is ever "hit" → click-through.
            ClickThrough.HitTest = null;
            Assert.That(ClickThrough.ComputePolicy(), Is.True);
        }

        [Test]
        public void TickPolicy_WithBothSwitchesOff_LeavesManualStateAlone()
        {
            ClickThrough.ForceClickThrough = false;
            ClickThrough.AutoByPointer = false;

            ClickThrough.SetEnabled(true); // manual control
            ClickThrough.TickPolicy();
            Assert.That(ClickThrough.IsEnabled, Is.True);

            ClickThrough.SetEnabled(false);
            ClickThrough.TickPolicy();
            Assert.That(ClickThrough.IsEnabled, Is.False);
        }

        [Test]
        public void TickPolicy_WithPolicyActive_PushesComputedState()
        {
            ClickThrough.HitTest = new FakeHitTest { Result = false };
            ClickThrough.PointerPositionSource = () => Vector2.zero;
            ClickThrough.AutoByPointer = true;

            ClickThrough.TickPolicy();
            Assert.That(ClickThrough.IsEnabled, Is.True);

            ClickThrough.HitTest = new FakeHitTest { Result = true };
            ClickThrough.TickPolicy();
            Assert.That(ClickThrough.IsEnabled, Is.False);
        }
    }
}
