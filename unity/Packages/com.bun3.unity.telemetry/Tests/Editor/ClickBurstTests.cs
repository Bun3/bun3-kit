using NUnit.Framework;

namespace Bun3.Unity.Telemetry.Tests
{
    public class ClickBurstTests
    {
        [Test]
        public void FirstClickEmitsImmediately()
        {
            Assert.That(new ClickBurst().Register("A", 0, out _, out _), Is.True);
        }

        [Test]
        public void BurstEndsWithOneSummary()
        {
            var b = new ClickBurst();
            Assert.That(b.Register("A", 0, out _, out _), Is.True);
            Assert.That(b.Register("A", 0.3, out _, out _), Is.False);
            Assert.That(b.Register("A", 0.6, out _, out _), Is.False);
            Assert.That(b.Register("A", 0.9, out _, out _), Is.False);
            Assert.That(b.Register("A", 1.2, out _, out _), Is.False);
            Assert.That(b.Tick(2.3, out var path, out var count), Is.True);
            Assert.That(path, Is.EqualTo("A"));
            Assert.That(count, Is.EqualTo(5));
            Assert.That(b.Tick(5, out _, out _), Is.False);
        }

        [Test]
        public void DifferentPathEndsBurst()
        {
            var b = new ClickBurst();
            b.Register("A", 0, out _, out _);
            b.Register("A", 0.2, out _, out _);
            Assert.That(b.Register("B", 0.4, out var path, out var count), Is.True);
            Assert.That(path, Is.EqualTo("A"));
            Assert.That(count, Is.EqualTo(2));
        }

        [Test]
        public void SpacedClicksEmitEach()
        {
            var b = new ClickBurst();
            Assert.That(b.Register("A", 0, out _, out _), Is.True);
            Assert.That(b.Register("A", 1.2, out var path, out var count), Is.True);
            Assert.That(path, Is.Null);
            Assert.That(count, Is.EqualTo(1));
        }
    }
}
