using System;
using System.Reflection;
using System.Threading.Tasks;
using global::Dissonance.VAD;
using NUnit.Framework;

namespace Bun3.Unity.Audio.Dissonance.Tests
{
    public class DissonanceVoiceActivityScopeTests
    {
        private sealed class Scope : IDisposable
        {
            private readonly IDisposable _instance;
            private readonly MethodInfo _poll;
            internal IVoiceActivationListener Listener;
            internal int UnsubscribeCount;

            internal Scope(bool startDuringSubscribe = false, bool startDuringUnsubscribe = false)
            {
                var type = Type.GetType("Bun3.Unity.Audio.Dissonance.DissonanceVoiceActivityScope, Bun3.Unity.Audio.Dissonance");
                Assert.That(type, Is.Not.Null, "The VAD scope must publish worker callbacks and preserve short speech activations.");
                Action<IVoiceActivationListener> subscribe = listener =>
                {
                    Listener = listener;
                    if (startDuringSubscribe) listener.VoiceActivationStart();
                };
                Action<IVoiceActivationListener> unsubscribe = listener =>
                {
                    UnsubscribeCount++;
                    Assert.That(listener, Is.SameAs(Listener));
                    if (startDuringUnsubscribe) listener.VoiceActivationStart();
                    listener.VoiceActivationStop();
                };
                _instance = (IDisposable)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new object[] { subscribe, unsubscribe }, null);
                _poll = type.GetMethod("Poll");
            }

            internal (bool Active, long Count) Poll()
            {
                var snapshot = _poll.Invoke(_instance, null);
                var type = snapshot.GetType();
                return ((bool)type.GetProperty("IsSpeechActive").GetValue(snapshot),
                    (long)type.GetProperty("ActivationCount").GetValue(snapshot));
            }

            public void Dispose() => _instance.Dispose();
        }

        [Test]
        public void SynchronousSubscriptionCallbackIsVisibleOnFirstPoll()
        {
            using var scope = new Scope(startDuringSubscribe: true);
            Assert.That(scope.Poll(), Is.EqualTo((true, 1L)));
            scope.Listener.VoiceActivationStop();
            Assert.That(scope.Poll(), Is.EqualTo((false, 1L)));
        }

        [Test]
        public void ShortSpeechAndDuplicateStartPreserveActivationCountBetweenPolls()
        {
            using var scope = new Scope();
            Assert.That(scope.Poll(), Is.EqualTo((false, 0L)));
            scope.Listener.VoiceActivationStart();
            scope.Listener.VoiceActivationStart();
            scope.Listener.VoiceActivationStop();
            scope.Listener.VoiceActivationStop();
            Assert.That(scope.Poll(), Is.EqualTo((false, 1L)));
            scope.Listener.VoiceActivationStart();
            scope.Listener.VoiceActivationStop();
            Assert.That(scope.Poll(), Is.EqualTo((false, 2L)));
        }

        [Test]
        public void WorkerCallbacksPublishAllActivationsToOwner()
        {
            using var scope = new Scope();
            Task.Run(() =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    scope.Listener.VoiceActivationStart();
                    scope.Listener.VoiceActivationStop();
                }
                scope.Listener.VoiceActivationStart();
            }).GetAwaiter().GetResult();
            Assert.That(scope.Poll(), Is.EqualTo((true, 1001L)));
        }

        [Test]
        public void DisposeUnsubscribesOnceAndSynchronousOrLateCallbacksCannotRevive()
        {
            using var scope = new Scope(startDuringUnsubscribe: true);
            scope.Listener.VoiceActivationStart();
            scope.Listener.VoiceActivationStop();
            scope.Dispose();
            scope.Dispose();
            scope.Listener.VoiceActivationStart();
            scope.Listener.VoiceActivationStop();
            scope.Listener.VoiceActivationStart();
            Assert.That(scope.Poll(), Is.EqualTo((false, 1L)));
            Assert.That(scope.UnsubscribeCount, Is.EqualTo(1));
        }

        [Test]
        public void WarmCallbacksDoNotAllocateManagedMemory()
        {
            using var scope = new Scope();
            for (int i = 0; i < 32; i++)
            {
                scope.Listener.VoiceActivationStart();
                scope.Listener.VoiceActivationStop();
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1024; i++)
            {
                scope.Listener.VoiceActivationStart();
                scope.Listener.VoiceActivationStop();
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }
    }
}
