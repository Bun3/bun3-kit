#nullable enable
using System;
using NUnit.Framework;

namespace Bun3.Chat.Tests
{
    public sealed class ChatRateLimiterTests
    {
        [Test]
        public void AllowsBurstThenRejects()
        {
            var limiter = new ChatRateLimiter(3, 1000);
            Assert.That(limiter.TryAcquire(1, 0), Is.True);
            Assert.That(limiter.TryAcquire(1, 1), Is.True);
            Assert.That(limiter.TryAcquire(1, 2), Is.True);
            Assert.That(limiter.TryAcquire(1, 3), Is.False);
        }

        [Test]
        public void RecoversOnceTheOldestLeavesTheWindow()
        {
            var limiter = new ChatRateLimiter(3, 1000);
            for (long t = 0; t < 3; t++) limiter.TryAcquire(1, t);
            Assert.That(limiter.TryAcquire(1, 999), Is.False);
            Assert.That(limiter.TryAcquire(1, 1000), Is.True);
        }

        [Test]
        public void RejectedAttemptsDoNotConsumeBudget()
        {
            var limiter = new ChatRateLimiter(3, 1000);
            for (long t = 0; t < 3; t++) limiter.TryAcquire(1, t);
            for (int i = 0; i < 10; i++) Assert.That(limiter.TryAcquire(1, 500), Is.False);
            Assert.That(limiter.TryAcquire(1, 1000), Is.True);
            Assert.That(limiter.TryAcquire(1, 1001), Is.True);
            Assert.That(limiter.TryAcquire(1, 1002), Is.True);
            Assert.That(limiter.TryAcquire(1, 1003), Is.False);
        }

        [Test]
        public void SendersAreIndependent()
        {
            var limiter = new ChatRateLimiter(1, 1000);
            Assert.That(limiter.TryAcquire(1, 0), Is.True);
            Assert.That(limiter.TryAcquire(2, 0), Is.True);
            Assert.That(limiter.TryAcquire(1, 1), Is.False);
        }

        [Test]
        public void ForgetAndClearResetSenders()
        {
            var limiter = new ChatRateLimiter(1, 1000);
            limiter.TryAcquire(1, 0);
            limiter.TryAcquire(2, 0);
            limiter.Forget(1);
            Assert.That(limiter.TryAcquire(1, 1), Is.True);
            Assert.That(limiter.TryAcquire(2, 1), Is.False);
            limiter.Clear();
            Assert.That(limiter.TryAcquire(2, 2), Is.True);
        }

        [Test]
        public void ClockGoingBackwardsDoesNotLockSenderOut()
        {
            var limiter = new ChatRateLimiter(2, 1000);
            limiter.TryAcquire(1, 10_000);
            limiter.TryAcquire(1, 10_001);
            Assert.That(limiter.TryAcquire(1, 5_000), Is.True);
        }

        [Test]
        public void InvalidArgumentsThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChatRateLimiter(0, 1000));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChatRateLimiter(1, 0));
        }
    }
}
