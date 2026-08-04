using System;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Window.Tests
{
    public class AlwaysOnTopTests
    {
        [TearDown]
        public void TearDown()
        {
            AlwaysOnTop.SetEnabled(false);
            AlwaysOnTop.EnforceIntervalSeconds = 0.25f;
        }

#if UNITY_EDITOR
        [Test]
        public void IsSupported_InEditor_TrueOnlyOnWindows()
        {
            Assert.That(
                AlwaysOnTop.IsSupported,
                Is.EqualTo(Application.platform == RuntimePlatform.WindowsEditor));
        }
#endif

        [Test]
        public void SetEnabled_WhenSupported_PinsAndUnpinsActualWindow()
        {
            if (!AlwaysOnTop.IsSupported)
            {
                Assert.Ignore("Topmost is only applied on Windows.");
            }

            AlwaysOnTop.SetEnabled(true);
            Assert.That(AlwaysOnTop.IsEffectivelyTopMost(), Is.True);

            AlwaysOnTop.SetEnabled(false);
            Assert.That(AlwaysOnTop.IsEffectivelyTopMost(), Is.False);
        }

        [Test]
        public void WhenUnsupported_NativeQueriesAreSafeNoOps()
        {
            if (AlwaysOnTop.IsSupported)
            {
                Assert.Ignore("Only meaningful where the pin cannot reach the OS.");
            }

            AlwaysOnTop.SetEnabled(true);
            Assert.That(AlwaysOnTop.IsEffectivelyTopMost(), Is.False);
            Assert.That(AlwaysOnTop.EnforceOnce(), Is.False);
        }

        [Test]
        public void SetEnabled_TogglesIsEnabled()
        {
            AlwaysOnTop.SetEnabled(true);
            Assert.That(AlwaysOnTop.IsEnabled, Is.True);

            AlwaysOnTop.SetEnabled(false);
            Assert.That(AlwaysOnTop.IsEnabled, Is.False);
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
            AlwaysOnTop.EnabledChanged += handler;
            try
            {
                AlwaysOnTop.SetEnabled(true);
                AlwaysOnTop.SetEnabled(true);
                Assert.That(count, Is.EqualTo(1));
                Assert.That(last, Is.True);

                AlwaysOnTop.SetEnabled(false);
                Assert.That(count, Is.EqualTo(2));
                Assert.That(last, Is.False);
            }
            finally
            {
                AlwaysOnTop.EnabledChanged -= handler;
            }
        }

        [Test]
        public void EnforceOnce_WhenDisabled_ReturnsFalse()
        {
            AlwaysOnTop.SetEnabled(false);
            Assert.That(AlwaysOnTop.EnforceOnce(), Is.False);
        }
    }
}
