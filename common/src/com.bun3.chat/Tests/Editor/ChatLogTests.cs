#nullable enable
using System;
using NUnit.Framework;

namespace Bun3.Chat.Tests
{
    public sealed class ChatLogTests
    {
        static ChatMessage Message(string text) => new ChatMessage(1, "p1", "Alice", text, 0, 0);

        static string[] Texts(ChatLog log)
        {
            var texts = new string[log.Count];
            for (int i = 0; i < texts.Length; i++) texts[i] = log[i].Text;
            return texts;
        }

        [Test]
        public void KeepsInsertionOrder()
        {
            var log = new ChatLog(3);
            log.Add(Message("a"));
            log.Add(Message("b"));
            Assert.That(log.Capacity, Is.EqualTo(3));
            Assert.That(Texts(log), Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void OverwritesOldestWhenFull()
        {
            var log = new ChatLog(3);
            foreach (var text in new[] { "a", "b", "c", "d", "e" }) log.Add(Message(text));
            Assert.That(log.Count, Is.EqualTo(3));
            Assert.That(Texts(log), Is.EqualTo(new[] { "c", "d", "e" }));
        }

        [Test]
        public void IndexOutsideCountThrows()
        {
            var log = new ChatLog(3);
            log.Add(Message("a"));
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = log[1]; });
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = log[-1]; });
        }

        [Test]
        public void UnreadIsCappedAtCapacityAndResetByMarkRead()
        {
            var log = new ChatLog(2);
            for (int i = 0; i < 5; i++) log.Add(Message("x"));
            Assert.That(log.Unread, Is.EqualTo(2));
            log.MarkRead();
            Assert.That(log.Unread, Is.EqualTo(0));
        }

        [Test]
        public void ChangedFiresOnAddOnEffectiveMarkReadAndOnClear()
        {
            var log = new ChatLog(4);
            int changes = 0;
            log.Changed += () => changes++;
            log.Add(Message("a"));
            Assert.That(changes, Is.EqualTo(1));
            log.MarkRead();
            Assert.That(changes, Is.EqualTo(2));
            log.MarkRead();
            Assert.That(changes, Is.EqualTo(2));
            log.Clear();
            Assert.That(changes, Is.EqualTo(3));
        }

        [Test]
        public void ClearEmptiesButKeepsTotalAdded()
        {
            var log = new ChatLog(3);
            for (int i = 0; i < 5; i++) log.Add(Message("x"));
            Assert.That(log.TotalAdded, Is.EqualTo(5));
            log.Clear();
            Assert.That(log.Count, Is.EqualTo(0));
            Assert.That(log.Unread, Is.EqualTo(0));
            Assert.That(log.TotalAdded, Is.EqualTo(5));
            log.Add(Message("new"));
            Assert.That(Texts(log), Is.EqualTo(new[] { "new" }));
            Assert.That(log.TotalAdded, Is.EqualTo(6));
        }

        [Test]
        public void NonPositiveCapacityThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChatLog(0));
        }
    }
}
