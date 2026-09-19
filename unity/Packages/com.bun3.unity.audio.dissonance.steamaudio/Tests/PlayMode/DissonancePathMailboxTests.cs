using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.Dissonance.SteamAudio.Tests
{
    public sealed class DissonancePathMailboxTests
    {
        static PathPlaybackSettings Settings(float value) => new(new SA.CoordinateSpace3
        {
            origin = new SA.Vector3 { x = value }, right = new SA.Vector3 { x = 1 },
            up = new SA.Vector3 { y = 1 }, ahead = new SA.Vector3 { z = -1 }
        }, value, value + 1, value + 2, value + 3, ((int)value & 1) == 0);

        [Test]
        public void PublicationCopiesAllFieldsAndRejectsStaleOrRetiredOwners()
        {
            var mailbox = new DissonancePathMailbox(7, 4);
            var input = new[] { 4f, 4f, 4f, 4f };
            var copy = new float[4];
            Assert.That(mailbox.TryPublish(7, input, Settings(4)), Is.True);
            input[0] = 99;
            Assert.That(mailbox.TryRead(copy, out var settings), Is.True);
            Assert.That(copy, Is.All.EqualTo(4));
            Assert.That(settings.Listener.origin.x, Is.EqualTo(4));
            Assert.That(settings.EqLow, Is.EqualTo(4));
            Assert.That(settings.EqMid, Is.EqualTo(5));
            Assert.That(settings.EqHigh, Is.EqualTo(6));
            Assert.That(settings.Gain, Is.EqualTo(7));
            Assert.That(settings.NormalizeEq, Is.True);
            Assert.That(mailbox.TryPublish(8, input, Settings(9)), Is.False);
            Assert.That(mailbox.TrySetBlocked(8, false), Is.False);
            Assert.That(mailbox.TrySetBlocked(7, false), Is.True);
            Assert.That(mailbox.IsBlocked, Is.False);
            mailbox.Retire();
            Assert.That(mailbox.IsBlocked, Is.True);
            Assert.That(mailbox.TrySetBlocked(7, false), Is.False);
            Assert.That(mailbox.TryPublish(7, input, Settings(9)), Is.False);
            Assert.That(mailbox.TryRead(copy, out _), Is.False);
        }

        [Test]
        public void ConcurrentPublicationNeverTearsMovingListenerCoefficientsOrEq()
        {
            var mailbox = new DissonancePathMailbox(1, 4);
            var input = new float[4];
            Assert.That(mailbox.TryPublish(1, input, Settings(0)), Is.True);
            using var begin = new ManualResetEventSlim();
            var reader = Task.Run(() =>
            {
                var copy = new float[4];
                begin.Set();
                int reads = 0;
                for (int i = 0; i < 50000; i++)
                {
                    if (!mailbox.TryRead(copy, out var settings)) continue;
                    float value = copy[0];
                    if (copy[1] != value || copy[2] != value || copy[3] != value || settings.Listener.origin.x != value ||
                        settings.EqLow != value || settings.EqMid != value + 1 || settings.EqHigh != value + 2 ||
                        settings.Gain != value + 3 || settings.NormalizeEq != (((int)value & 1) == 0))
                        throw new InvalidOperationException("A reader observed a torn parameter snapshot.");
                    reads++;
                }
                return reads;
            });
            Assert.That(begin.Wait(TimeSpan.FromSeconds(5)), Is.True);
            for (int i = 1; i <= 50000; i++)
            {
                Array.Fill(input, (float)i);
                mailbox.TryPublish(1, input, Settings(i));
            }
            Assert.That(reader.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(reader.Result, Is.GreaterThan(0));
        }

        [Test]
        public void WarmPublicationAndReadAllocateNoManagedMemory()
        {
            var mailbox = new DissonancePathMailbox(1, 4);
            var input = new float[4];
            var output = new float[4];
            var settings = Settings(1);
            Assert.That(mailbox.TryPublish(1, input, settings), Is.True);
            Assert.That(mailbox.TryRead(output, out _), Is.True);
            for (int i = 0; i < 100; i++) { mailbox.TryPublish(1, input, settings); mailbox.TryRead(output, out _); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) { mailbox.TryPublish(1, input, settings); mailbox.TryRead(output, out _); }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void InvalidPublicationCannotReplaceLastGoodSnapshot()
        {
            var mailbox = new DissonancePathMailbox(1, 4);
            var input = new float[4];
            Assert.That(mailbox.TryPublish(1, input, Settings(1)), Is.True);
            input[2] = float.NaN;
            Assert.Throws<ArgumentOutOfRangeException>(() => mailbox.TryPublish(1, input, Settings(2)));
            input[2] = 0;
            Assert.Throws<ArgumentOutOfRangeException>(() => mailbox.TryPublish(1, input, new PathPlaybackSettings(Settings(1).Listener, gain: -1)));
            Assert.Throws<ArgumentException>(() => mailbox.TryPublish(1, input, new PathPlaybackSettings(default)));
            Assert.That(mailbox.TryRead(input, out var last), Is.True);
            Assert.That(last.Gain, Is.EqualTo(4));
        }
    }
}
