using NUnit.Framework;
using UnityEngine;

namespace Bun3.UI.Tests
{
    public class TestAssemblySmokeTests
    {
        [Test]
        public void RunsInPlayMode()
        {
            Assert.IsTrue(Application.isPlaying, "PlayMode 어셈블리가 아니다. asmdef의 includePlatforms를 비워야 한다.");
        }

        [Test]
        public void ReferencesRuntimeAssembly()
        {
            Assert.IsNotNull(typeof(Bun3.UI.Buttons.ButtonInteractableScope));
        }
    }
}
