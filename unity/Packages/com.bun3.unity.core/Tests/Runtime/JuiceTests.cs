using System.Collections;
using Bun3.Unity.Core.Motion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Core.Tests
{
    public class JuiceTests
    {
        private GameObject _target;

        [SetUp]
        public void SetUp() => _target = new GameObject("Juice target");

        [TearDown]
        public void TearDown()
        {
            if (_target != null)
                Object.Destroy(_target);
        }

        [UnityTest]
        public IEnumerator Punch_Retriggered_GrowsThenRestoresExactScale()
        {
            var start = new Vector3(2f, 2f, 1f);
            _target.transform.localScale = start;
            Juice.Punch(_target.transform, 1.5f, 0.1f);

            var peak = 0f;
            var retriggered = false;
            var until = Time.realtimeSinceStartup + 0.3f;
            while (Time.realtimeSinceStartup < until)
            {
                peak = Mathf.Max(peak, _target.transform.localScale.x);
                if (!retriggered && peak > start.x)
                {
                    Juice.Punch(_target.transform, 1.5f, 0.1f);
                    retriggered = true;
                }
                yield return null;
            }

            Assert.That(peak, Is.GreaterThan(start.x));
            Assert.That(_target.transform.localScale, Is.EqualTo(start));
        }

        [UnityTest]
        public IEnumerator Shake_MovesThenRestoresExactPosition()
        {
            var start = new Vector3(1f, 2f, 3f);
            _target.transform.localPosition = start;
            Juice.Shake(_target.transform, 0.5f, 0.1f);

            var moved = false;
            var until = Time.realtimeSinceStartup + 0.3f;
            while (Time.realtimeSinceStartup < until)
            {
                moved |= _target.transform.localPosition != start;
                yield return null;
            }

            Assert.That(moved, Is.True);
            Assert.That(_target.transform.localPosition, Is.EqualTo(start));
        }

        [UnityTest]
        public IEnumerator Flash_RestoresOwnColor_AndDestroyedTargetIsDropped()
        {
            var sprite = _target.AddComponent<SpriteRenderer>();
            sprite.color = Color.green;
            Juice.Flash(sprite, Color.red, 0.1f);
            var other = new GameObject("Destroyed target");
            Juice.Punch(other.transform, 1.5f, 0.1f);
            Object.Destroy(other);

            yield return new WaitForSecondsRealtime(0.3f);

            Assert.That(sprite.color, Is.EqualTo(Color.green));
        }
    }
}
