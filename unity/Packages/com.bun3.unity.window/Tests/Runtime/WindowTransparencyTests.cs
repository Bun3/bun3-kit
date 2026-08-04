using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Window.Tests
{
    public class WindowTransparencyTests
    {
#if UNITY_EDITOR
        [Test]
        public void IsSupported_IsFalseInEditor()
        {
            // Unlike AlwaysOnTop, transparency must never touch the editor window.
            Assert.That(WindowTransparency.IsSupported, Is.False);
        }
#endif

        [Test]
        public void Apply_WhenUnsupported_IsANoOpThatLeavesCameraUntouched()
        {
            if (WindowTransparency.IsSupported)
            {
                Assert.Ignore("Only meaningful where transparency cannot reach the OS.");
            }

            var go = new GameObject(nameof(Apply_WhenUnsupported_IsANoOpThatLeavesCameraUntouched));
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.Skybox;
                var originalBackground = camera.backgroundColor;

                var raised = false;
                void Handler(TransparencyMethod _) => raised = true;
                WindowTransparency.Applied += Handler;
                try
                {
                    var result = WindowTransparency.Apply(
                        camera, TransparencyPreference.Auto, new Color(1f, 0f, 1f, 1f));

                    Assert.That(result, Is.EqualTo(TransparencyMethod.None));
                    Assert.That(WindowTransparency.ActiveMethod, Is.EqualTo(TransparencyMethod.None));
                    Assert.That(camera.clearFlags, Is.EqualTo(CameraClearFlags.Skybox));
                    Assert.That(camera.backgroundColor, Is.EqualTo(originalBackground));
                    Assert.That(raised, Is.False);
                }
                finally
                {
                    WindowTransparency.Applied -= Handler;
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Settings_Defaults_MatchDocumentedBehavior()
        {
            var settings = ScriptableObject.CreateInstance<WindowOverlaySettings>();
            try
            {
                Assert.That(settings.AlwaysOnTopEnabled, Is.True);
                Assert.That(settings.EnforceInterval, Is.EqualTo(0.25f));
                Assert.That(settings.TransparencyEnabled, Is.True);
                Assert.That(settings.PreferredMethod, Is.EqualTo(TransparencyPreference.Auto));
                Assert.That(settings.ColorKey, Is.EqualTo(new Color(1f, 0f, 1f, 1f)));
                Assert.That(settings.AutoClickThrough, Is.True);
                Assert.That(settings.HitTest, Is.InstanceOf<EventSystemHitTest>());
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }
    }
}
