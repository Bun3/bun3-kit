using System.Collections.Generic;
using Bun3.Unity.Window.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bun3.Unity.Window.Editor.Tests
{
    public class OverlaySettingsValidatorTests
    {
        private static OverlaySettingsValidator.Snapshot GoodSnapshot(
            FullScreenMode fullScreenMode = FullScreenMode.Windowed,
            bool runInBackground = true,
            bool useFlipModelSwapchain = false,
            bool allowHdrDisplaySupport = false,
            bool resizableWindow = false,
            GraphicsDeviceType firstGraphicsApi = GraphicsDeviceType.Direct3D11)
        {
            return new OverlaySettingsValidator.Snapshot(
                fullScreenMode,
                runInBackground,
                useFlipModelSwapchain,
                allowHdrDisplaySupport,
                resizableWindow,
                firstGraphicsApi);
        }

        [Test]
        public void CollectWarnings_WithOverlayFriendlySettings_IsEmpty()
        {
            Assert.That(OverlaySettingsValidator.CollectWarnings(GoodSnapshot()), Is.Empty);
        }

        [Test]
        public void CollectWarnings_FlagsEachMisconfigurationIndependently()
        {
            AssertSingleWarning(GoodSnapshot(fullScreenMode: FullScreenMode.FullScreenWindow), "Fullscreen Mode");
            AssertSingleWarning(GoodSnapshot(runInBackground: false), "Run In Background");
            AssertSingleWarning(GoodSnapshot(useFlipModelSwapchain: true), "Flip Model");
            AssertSingleWarning(GoodSnapshot(allowHdrDisplaySupport: true), "HDR");
            AssertSingleWarning(GoodSnapshot(resizableWindow: true), "Resizable Window");
            AssertSingleWarning(GoodSnapshot(firstGraphicsApi: GraphicsDeviceType.Direct3D12), "graphics API");
        }

        [Test]
        public void CollectWarnings_AccumulatesAllProblems()
        {
            var snapshot = GoodSnapshot(
                fullScreenMode: FullScreenMode.ExclusiveFullScreen,
                runInBackground: false,
                useFlipModelSwapchain: true,
                allowHdrDisplaySupport: true,
                resizableWindow: true,
                firstGraphicsApi: GraphicsDeviceType.Vulkan);
            Assert.That(OverlaySettingsValidator.CollectWarnings(snapshot), Has.Count.EqualTo(6));
        }

        private static void AssertSingleWarning(in OverlaySettingsValidator.Snapshot snapshot, string expectedSubstring)
        {
            List<string> warnings = OverlaySettingsValidator.CollectWarnings(snapshot);
            Assert.That(warnings, Has.Count.EqualTo(1), $"expected exactly one warning about '{expectedSubstring}'");
            StringAssert.Contains(expectedSubstring, warnings[0]);
        }
    }
}
