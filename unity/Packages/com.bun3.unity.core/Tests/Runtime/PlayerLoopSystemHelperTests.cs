using System.Collections;
using Bun3.Unity.Core.PlayerLoop;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Bun3.Unity.Core.Tests
{
    public class PlayerLoopSystemHelperTests
    {
        private static class TestTickMarker
        {
        }

        [TearDown]
        public void TearDown()
        {
            PlayerLoopSystemHelper.TryRemoveSystem(typeof(TestTickMarker));
        }

        [UnityTest]
        public IEnumerator InsertedSystem_TicksEveryFrame_UntilRemoved()
        {
            var ticks = 0;
            PlayerLoopSystemHelper.InsertSystemBefore(
                typeof(TestTickMarker),
                () => ticks++,
                typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate));

            Assert.That(PlayerLoopSystemHelper.IsInserted(typeof(TestTickMarker)), Is.True);

            yield return null;
            yield return null;
            Assert.That(ticks, Is.GreaterThanOrEqualTo(2));

            Assert.That(PlayerLoopSystemHelper.TryRemoveSystem(typeof(TestTickMarker)), Is.True);
            Assert.That(PlayerLoopSystemHelper.IsInserted(typeof(TestTickMarker)), Is.False);

            var ticksAfterRemoval = ticks;
            yield return null;
            yield return null;
            Assert.That(ticks, Is.EqualTo(ticksAfterRemoval));
        }

        [Test]
        public void TryRemoveSystem_WhenNotInserted_ReturnsFalse()
        {
            Assert.That(PlayerLoopSystemHelper.TryRemoveSystem(typeof(TestTickMarker)), Is.False);
        }
    }
}
