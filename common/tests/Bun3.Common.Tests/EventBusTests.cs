using System;
using System.Collections.Generic;
using Bun3.Common.Events;
using NUnit.Framework;

namespace Bun3.Common.Tests
{
    [TestFixture]
    public class EventBusTests
    {
        private readonly struct Ping
        {
            public readonly int Value;
            public Ping(int value) { Value = value; }
        }

        private readonly struct Mode : IStateEvent
        {
            public readonly int Value;
            public Mode(int value) { Value = value; }
        }

        [Test]
        public void Publish_DeliversToSubscriber_UntilUnsubscribed()
        {
            using var bus = new EventBus();
            var received = new List<int>();
            EventBusHandler<Ping> handler = (in Ping e) => received.Add(e.Value);

            bus.Subscribe(handler);
            bus.Publish(new Ping(1));
            bus.Unsubscribe(handler);
            bus.Publish(new Ping(2));

            Assert.That(received, Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void StateEvent_LateSubscriberReceivesLastValueImmediately()
        {
            using var bus = new EventBus();
            bus.Publish(new Mode(3));
            bus.Publish(new Mode(7));
            var received = new List<int>();

            bus.Subscribe((in Mode e) => received.Add(e.Value));

            Assert.That(received, Is.EqualTo(new[] { 7 }));
            Assert.That(bus.TryGetLast(out Mode last), Is.True);
            Assert.That(last.Value, Is.EqualTo(7));
        }

        [Test]
        public void MomentEvent_IsNotRetainedOrReplayed()
        {
            using var bus = new EventBus();
            bus.Publish(new Ping(5));
            var calls = 0;

            bus.Subscribe((in Ping e) => calls++);

            Assert.That(calls, Is.Zero);
            Assert.That(bus.TryGetLast(out Ping _), Is.False);
        }

        [Test]
        public void UnsubscribeDuringPublish_SkipsRemovedHandlerInSamePublish()
        {
            using var bus = new EventBus();
            var calls = new List<string>();
            EventBusHandler<Ping> second = (in Ping e) => calls.Add("second");
            bus.Subscribe((in Ping e) => { calls.Add("first"); bus.Unsubscribe(second); });
            bus.Subscribe(second);

            bus.Publish(new Ping(0));
            bus.Publish(new Ping(0));

            Assert.That(calls, Is.EqualTo(new[] { "first", "first" }));
        }

        [Test]
        public void SubscribeDuringPublish_ReceivesFromNextPublish()
        {
            using var bus = new EventBus();
            var late = new List<int>();
            var added = false;
            bus.Subscribe((in Ping e) =>
            {
                if (added) return;
                added = true;
                bus.Subscribe((in Ping x) => late.Add(x.Value));
            });

            bus.Publish(new Ping(1));
            bus.Publish(new Ping(2));

            Assert.That(late, Is.EqualTo(new[] { 2 }));
        }

        [Test]
        public void DuplicateSubscribe_IsIgnored()
        {
            using var bus = new EventBus();
            var calls = 0;
            EventBusHandler<Ping> handler = (in Ping e) => calls++;

            bus.Subscribe(handler);
            bus.Subscribe(handler);
            bus.Publish(new Ping(0));

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void RecursivePublish_BeyondDepthLimit_Throws()
        {
            using var bus = new EventBus();
            bus.Subscribe((in Ping e) => bus.Publish(new Ping(e.Value + 1)));

            Assert.Catch<InvalidOperationException>(() => bus.Publish(new Ping(0)));
        }

        [Test]
        public void HandlerException_WithCallback_ReportsAndContinues()
        {
            var reported = new List<Exception>();
            using var bus = new EventBus(reported.Add);
            var reached = false;
            bus.Subscribe((in Ping e) => throw new InvalidOperationException("boom"));
            bus.Subscribe((in Ping e) => reached = true);

            Assert.DoesNotThrow(() => bus.Publish(new Ping(0)));

            Assert.That(reached, Is.True);
            Assert.That(reported, Has.Count.EqualTo(1));
        }

        [Test]
        public void HandlerException_WithoutCallback_CallsAllThenRethrowsFirst()
        {
            using var bus = new EventBus();
            var reached = false;
            bus.Subscribe((in Ping e) => throw new InvalidOperationException("first"));
            bus.Subscribe((in Ping e) => reached = true);

            var thrown = Assert.Throws<InvalidOperationException>(() => bus.Publish(new Ping(0)));

            Assert.That(thrown.Message, Is.EqualTo("first"));
            Assert.That(reached, Is.True);
        }

        [Test]
        public void Dispose_ClearsStateAndRejectsPublish()
        {
            var bus = new EventBus();
            bus.Publish(new Mode(1));

            bus.Dispose();

            Assert.That(bus.TryGetLast(out Mode _), Is.False);
            Assert.DoesNotThrow(() => bus.Unsubscribe((in Mode e) => { }));
            Assert.Throws<ObjectDisposedException>(() => bus.Publish(new Mode(2)));
            Assert.Throws<ObjectDisposedException>(() => bus.Subscribe((in Mode e) => { }));
        }

        [Test]
        public void Publish_AfterWarmUp_DoesNotAllocate()
        {
            using var bus = new EventBus();
            var sum = 0;
            bus.Subscribe((in Ping e) => sum += e.Value);
            bus.Subscribe((in Mode e) => sum += e.Value);
            bus.Publish(new Ping(1));
            bus.Publish(new Mode(1));

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++)
            {
                bus.Publish(new Ping(i));
                bus.Publish(new Mode(i));
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
            Assert.That(sum, Is.Not.Zero);
        }
    }
}
