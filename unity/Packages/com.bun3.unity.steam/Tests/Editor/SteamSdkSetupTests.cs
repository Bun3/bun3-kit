using NUnit.Framework;

namespace Bun3.Unity.Steam.Editor.Tests
{
    public class SteamSdkSetupTests
    {
        [Test]
        public void EnablingAppendsTheSymbolAndKeepsOthers() =>
            Assert.That(SteamSdkSetup.WithSymbol(new[] { "A", "B" }, true), Is.EqualTo(new[] { "A", "B", SteamSdkSetup.Symbol }));

        [Test]
        public void EnablingTwiceKeepsTheExistingOrder()
        {
            var current = new[] { SteamSdkSetup.Symbol, "A" };
            Assert.That(SteamSdkSetup.WithSymbol(current, true), Is.SameAs(current));
        }

        [Test]
        public void DisablingRemovesOnlyTheSymbol() =>
            Assert.That(SteamSdkSetup.WithSymbol(new[] { "A", SteamSdkSetup.Symbol, "B" }, false), Is.EqualTo(new[] { "A", "B" }));

        [Test]
        public void DisablingWhenAbsentChangesNothing()
        {
            var current = new[] { "A" };
            Assert.That(SteamSdkSetup.WithSymbol(current, false), Is.SameAs(current));
        }
    }
}
