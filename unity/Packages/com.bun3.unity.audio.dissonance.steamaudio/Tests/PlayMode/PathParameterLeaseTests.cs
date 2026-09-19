using System;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using Bun3.Unity.Audio.SteamAudio;
using NUnit.Framework;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.Dissonance.SteamAudio.Tests
{
    public sealed class PathParameterLeaseTests
    {
        static PathRenderSettings Settings => new(new SA.CoordinateSpace3
        {
            right = new SA.Vector3 { x = 1 }, up = new SA.Vector3 { y = 1 }, ahead = new SA.Vector3 { z = -1 }
        });

        [Test]
        public void ReusedStorageRejectsTheOldImmutablePublisherToken()
        {
            var storage = new PathParameterMailbox(4);
            var old = storage.BeginGeneration(1);
            var coefficients = new float[4];
            Assert.That(old.IsValid, Is.True);
            Assert.That(old.TryPublish(coefficients, Settings), Is.True);
            Assert.That(old.TrySetBlocked(false), Is.True);
            storage.Retire(1);
            var current = storage.BeginGeneration(2);
            Assert.That(old.Generation, Is.EqualTo(1));
            Assert.That(old.IsValid, Is.False);
            Assert.That(old.IsBlocked, Is.True);
            Assert.That(old.TryPublish(coefficients, Settings), Is.False);
            Assert.That(old.TrySetBlocked(false), Is.False);
            Assert.That(current.IsValid, Is.True);
            Assert.That(current.IsBlocked, Is.True);
            Assert.That(storage.TryRead(1, coefficients, out _), Is.False);
            Assert.That(storage.TryRead(2, coefficients, out _), Is.False, "A new generation must not inherit old parameters.");
            Assert.That(current.TryPublish(coefficients, Settings), Is.True);
            storage.Retire(1);
            Assert.That(current.IsValid, Is.True, "A delayed retirement token cannot close a replacement generation.");
        }

        [Test]
        public void ReuseRequiresRetirementAndStrictlyIncreasingGeneration()
        {
            var storage = new PathParameterMailbox(4);
            var lease = storage.BeginGeneration(1);
            Assert.That(lease.IsValid, Is.True);
            Assert.Throws<InvalidOperationException>(() => storage.BeginGeneration(2));
            storage.Retire(1);
            Assert.Throws<ArgumentOutOfRangeException>(() => storage.BeginGeneration(1));
        }

        [Test]
        public void WarmGenerationReuseAndPublicationAllocateNothing()
        {
            var storage = new PathParameterMailbox(4);
            var input = new float[4];
            var output = new float[4];
            var settings = Settings;
            var lease = storage.BeginGeneration(1);
            Assert.That(lease.TryPublish(input, settings), Is.True);
            Assert.That(storage.TryRead(1, output, out _), Is.True);
            storage.Retire(1);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 2; i < 10002; i++)
            {
                lease = storage.BeginGeneration(i);
                lease.TryPublish(input, settings);
                storage.TryRead(i, output, out _);
                storage.Retire(i);
            }
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
        }

        [Test]
        public void ConcurrentRetirementReuseCannotGiveADelayedReaderAnotherGenerationsSnapshot()
        {
            var storage = new PathParameterMailbox(4);
            var input = new[] { 1f, 1f, 1f, 1f };
            var lease = storage.BeginGeneration(1);
            lease.TryPublish(input, Settings);
            long current = 1;
            int stop = 0;
            using var ready = new ManualResetEventSlim();
            var reader = Task.Run(() =>
            {
                var copy = new float[4];
                ready.Set();
                while (Volatile.Read(ref stop) == 0)
                {
                    long requested = Interlocked.Read(ref current);
                    if (storage.TryRead(requested, copy, out _) && copy[0] != requested)
                        throw new InvalidOperationException("A delayed reader consumed another generation's snapshot.");
                }
            });
            try
            {
                Assert.That(ready.Wait(TimeSpan.FromSeconds(5)), Is.True);
                for (int generation = 2; generation < 50002; generation++)
                {
                    storage.Retire(generation - 1);
                    bool begun = false;
                    for (int attempt = 0; attempt < 1000 && !begun; attempt++)
                    {
                        try { lease = storage.BeginGeneration(generation); begun = true; }
                        catch (InvalidOperationException) { Thread.Yield(); }
                    }
                    Assert.That(begun, Is.True);
                    Array.Fill(input, (float)generation);
                    lease.TryPublish(input, Settings);
                    Interlocked.Exchange(ref current, generation);
                }
            }
            finally { Volatile.Write(ref stop, 1); }
            Assert.That(reader.Wait(TimeSpan.FromSeconds(5)), Is.True);
        }

#if UNITY_EDITOR
        [Test]
        public void ReaderDelayedBeforeClaimCannotEnterAReplacementGeneration()
        {
            var storage = new PathParameterMailbox(4);
            var first = storage.BeginGeneration(1);
            first.TryPublish(new[] { 1f, 1f, 1f, 1f }, Settings);
            using var entered = new ManualResetEventSlim();
            using var proceed = new ManualResetEventSlim();
            Action beforeClaim = () =>
            {
                entered.Set();
                if (!proceed.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Test did not release the delayed reader.");
            };
            typeof(PathParameterMailbox).GetField("BeforeReaderClaimForTests", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(storage, beforeClaim);
            var copy = new float[4];
            var reader = Task.Run(() => storage.TryRead(1, copy, out _));
            try
            {
                Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                storage.Retire(1);
                var replacement = storage.BeginGeneration(2);
                Assert.That(replacement.TryPublish(new[] { 2f, 2f, 2f, 2f }, Settings), Is.True);
            }
            finally { proceed.Set(); }
            Assert.That(reader.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(reader.Result, Is.False, "A reader that passed its old precheck cannot enter a replacement generation after a delayed claim.");
        }
#endif
    }
}
