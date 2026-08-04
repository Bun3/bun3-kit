using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Bun3.Unity.Window.Tests
{
    public class EventSystemHitTestTests
    {
        private const int IgnoreRaycastLayer = 2;

        private GameObject _eventSystemGo;
        private GameObject _canvasGo;
        private GameObject _imageGo;

        [SetUp]
        public void SetUp()
        {
            _eventSystemGo = new GameObject("EventSystem", typeof(EventSystem));

            _canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(GraphicRaycaster));
            _canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            _imageGo = new GameObject("Image", typeof(Image));
            var rect = _imageGo.GetComponent<RectTransform>();
            rect.SetParent(_canvasGo.transform, worldPositionStays: false);
            rect.sizeDelta = new Vector2(100f, 100f); // anchored at canvas center by default
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_imageGo);
            Object.DestroyImmediate(_canvasGo);
            Object.DestroyImmediate(_eventSystemGo);
        }

        private static Vector2 ScreenCenter => new(Screen.width / 2f, Screen.height / 2f);

        [UnityTest]
        public IEnumerator IsHit_OverUiGraphic_ReturnsTrue()
        {
            yield return null; // canvas layout pass

            var hitTest = new EventSystemHitTest();
            Assert.That(hitTest.IsHit(ScreenCenter), Is.True);
        }

        [UnityTest]
        public IEnumerator IsHit_OverEmptySpace_ReturnsFalse()
        {
            yield return null;

            var hitTest = new EventSystemHitTest();
            Assert.That(hitTest.IsHit(new Vector2(-1000f, -1000f)), Is.False);
        }

        [UnityTest]
        public IEnumerator IsHit_OnIgnoredLayer_ReturnsFalse()
        {
            _imageGo.layer = IgnoreRaycastLayer;
            yield return null;

            var hitTest = new EventSystemHitTest();
            Assert.That(hitTest.IsHit(ScreenCenter), Is.False);
        }

        [Test]
        public void IsHit_WithoutEventSystem_ReturnsFalse()
        {
            Object.DestroyImmediate(_eventSystemGo);
            _eventSystemGo = null;
            _eventSystemGo = new GameObject("EventSystemPlaceholder"); // keep TearDown simple

            var hitTest = new EventSystemHitTest();
            Assert.That(hitTest.IsHit(ScreenCenter), Is.False);
        }
    }
}
