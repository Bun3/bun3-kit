#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Bun3.Chat.Tests
{
    public sealed class ChatRelayTests
    {
        FakeChatPolicy policy = null!;

        [SetUp]
        public void SetUp()
        {
            policy = new FakeChatPolicy();
            policy.Senders[1] = ("p1", "Alice");
            policy.Channels[1] = 7;
        }

        static ChatRelayOptions History(int capacity, int replay) => new ChatRelayOptions { HistoryCapacity = capacity, HistoryReplayCount = replay };

        static string[] Texts(IReadOnlyList<ChatMessage> messages)
        {
            var texts = new string[messages.Count];
            for (int i = 0; i < texts.Length; i++) texts[i] = messages[i].Text;
            return texts;
        }

        static string[] Texts(ChatLog log)
        {
            var texts = new string[log.Count];
            for (int i = 0; i < texts.Length; i++) texts[i] = log[i].Text;
            return texts;
        }

        [Test]
        public void InvalidOptionsThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChatRelay(policy, new ChatRelayOptions { MaxLength = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChatRelay(policy, new ChatRelayOptions { Burst = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChatRelay(policy, new ChatRelayOptions { BurstWindowMs = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChatRelay(policy, History(-1, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChatRelay(policy, History(5, -1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChatRelay(policy, History(5, 6)));
            Assert.Throws<ArgumentNullException>(() => new ChatRelay(null!));
        }

        [Test]
        public void TextFilterRewritesNormalizedTextBeforeStampingAndHistory()
        {
            string? seen = null;
            var options = History(5, 5);
            options.TextFilter = text => { seen = text; return text.Replace("bad", "***"); };
            var relay = new ChatRelay(policy, options);
            Assert.That(relay.Submit(1, "  a bad word\n", 1, out var message), Is.EqualTo(ChatRejectReason.None));
            Assert.That(seen, Is.EqualTo("a bad word"));
            Assert.That(message.Text, Is.EqualTo("a *** word"));
            Assert.That(Texts(relay.History!), Is.EqualTo(new[] { "a *** word" }));
        }

        [Test]
        public void TextFilterResultThatFailsNormalizationRejectsWithoutSpendingBurst()
        {
            var relay = new ChatRelay(policy, new ChatRelayOptions { Burst = 1, HistoryCapacity = 5, TextFilter = text => text == "x" ? " " : text });
            Assert.That(relay.Submit(1, "x", 1, out var message), Is.EqualTo(ChatRejectReason.InvalidText));
            Assert.That(message.Text, Is.Null);
            Assert.That(relay.History!.Count, Is.Zero);
            Assert.That(relay.Submit(1, "ok", 2, out message), Is.EqualTo(ChatRejectReason.None));
        }

        [Test]
        public void SuccessStampsPolicyIdentityChannelAndTime()
        {
            var relay = new ChatRelay(policy);
            Assert.That(relay.Submit(1, "  hi\n", 1234, out var message), Is.EqualTo(ChatRejectReason.None));
            Assert.That(message.SenderId, Is.EqualTo(1UL));
            Assert.That(message.SenderPlayerId, Is.EqualTo("p1"));
            Assert.That(message.SenderName, Is.EqualTo("Alice"));
            Assert.That(message.Text, Is.EqualTo("hi"));
            Assert.That(message.Channel, Is.EqualTo(7));
            Assert.That(message.TimestampMs, Is.EqualTo(1234));
        }

        [Test]
        public void PipelineReportsTheFirstFailingStage()
        {
            var relay = new ChatRelay(policy, new ChatRelayOptions { Burst = 1 });
            Assert.That(relay.Submit(9, "", 0, out _), Is.EqualTo(ChatRejectReason.UnknownSender));
            Assert.That(relay.Submit(1, " ", 0, out _), Is.EqualTo(ChatRejectReason.InvalidText));
            policy.Senders[2] = ("p2", "Bob");
            Assert.That(relay.Submit(2, "hi", 0, out _), Is.EqualTo(ChatRejectReason.NotAllowed));
            Assert.That(relay.Submit(1, "one", 0, out _), Is.EqualTo(ChatRejectReason.None));
            Assert.That(relay.Submit(1, "two", 1, out var rejected), Is.EqualTo(ChatRejectReason.RateLimited));
            Assert.That(rejected.Text, Is.Null);
        }

        [Test]
        public void RejectedMessagesDoNotConsumeRateBudget()
        {
            var relay = new ChatRelay(policy, new ChatRelayOptions { Burst = 2 });
            for (int i = 0; i < 5; i++) relay.Submit(1, "", 0, out _);
            policy.Channels.Remove(1);
            for (int i = 0; i < 5; i++) relay.Submit(1, "hi", 0, out _);
            policy.Channels[1] = 7;
            Assert.That(relay.Submit(1, "a", 0, out _), Is.EqualTo(ChatRejectReason.None));
            Assert.That(relay.Submit(1, "b", 0, out _), Is.EqualTo(ChatRejectReason.None));
        }

        [Test]
        public void SenderWithoutUsableIdentityIsUnknown()
        {
            var relay = new ChatRelay(policy);
            policy.Senders[1] = ("", "Alice");
            Assert.That(relay.Submit(1, "hi", 0, out _), Is.EqualTo(ChatRejectReason.UnknownSender));
            policy.Senders[1] = ("p1", new string('n', ChatMessage.MaxIdentityBytes - 1));
            Assert.That(relay.Submit(1, "hi", 0, out _), Is.EqualTo(ChatRejectReason.UnknownSender));
            policy.Senders[1] = ("p1", new string('n', ChatMessage.MaxIdentityBytes - 2));
            Assert.That(relay.Submit(1, "hi", 0, out _), Is.EqualTo(ChatRejectReason.None));
        }

        [Test]
        public void CollectRecipientsFiltersByPolicyAndReusesTheList()
        {
            var relay = new ChatRelay(policy);
            relay.Submit(1, "hi", 0, out var message);
            policy.Blocked.Add((3, 7));
            var result = new List<ulong> { 99 };
            relay.CollectRecipients(message, new ulong[] { 1, 2, 3 }, result);
            Assert.That(result, Is.EqualTo(new ulong[] { 1, 2 }));
        }

        [Test]
        public void HistoryIsOffByDefaultAndStoresOnlyAcceptedMessages()
        {
            Assert.That(new ChatRelay(policy).History, Is.Null);
            var relay = new ChatRelay(policy, History(2, 0));
            relay.Submit(1, "a", 0, out _);
            relay.Submit(1, " ", 0, out _);
            relay.Submit(1, "b", 0, out _);
            relay.Submit(1, "c", 0, out _);
            Assert.That(Texts(relay.History!), Is.EqualTo(new[] { "b", "c" }));
        }

        [Test]
        public void CollectReplayReturnsNewestReceivableOldestFirst()
        {
            var relay = new ChatRelay(policy, History(10, 2));
            relay.Submit(1, "a", 0, out _);
            relay.Submit(1, "b", 0, out _);
            policy.Channels[1] = 8;
            relay.Submit(1, "secret", 0, out _);
            policy.Channels[1] = 7;
            relay.Submit(1, "c", 0, out _);
            policy.Blocked.Add((5, 8));
            var result = new List<ChatMessage> { new ChatMessage(0, "x", "x", "junk", 0, 0) };
            relay.CollectReplay(5, result);
            Assert.That(Texts(result), Is.EqualTo(new[] { "b", "c" }));
        }

        [Test]
        public void CollectReplayIsEmptyWhenReplayIsOff()
        {
            var relay = new ChatRelay(policy, History(10, 0));
            relay.Submit(1, "a", 0, out _);
            var result = new List<ChatMessage>();
            relay.CollectReplay(5, result);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void SeedHistoryCopiesInOrderAndKeepsNewest()
        {
            var source = new ChatLog(10);
            foreach (var text in new[] { "a", "b", "c", "d" }) source.Add(new ChatMessage(1, "p1", "Alice", text, 7, 0));
            var relay = new ChatRelay(policy, History(3, 3));
            relay.SeedHistory(source);
            Assert.That(Texts(relay.History!), Is.EqualTo(new[] { "b", "c", "d" }));
            relay.SeedHistory(relay.History!);
            Assert.That(Texts(relay.History!), Is.EqualTo(new[] { "b", "c", "d" }));
            var off = new ChatRelay(policy);
            Assert.DoesNotThrow(() => off.SeedHistory(source));
            Assert.That(off.History, Is.Null);
        }

        [Test]
        public void ClearEmptiesHistoryAndResetsRateLimit()
        {
            var relay = new ChatRelay(policy, new ChatRelayOptions { Burst = 1, HistoryCapacity = 5 });
            relay.Submit(1, "a", 0, out _);
            Assert.That(relay.Submit(1, "b", 1, out _), Is.EqualTo(ChatRejectReason.RateLimited));
            relay.Clear();
            Assert.That(relay.History!.Count, Is.EqualTo(0));
            Assert.That(relay.Submit(1, "c", 2, out _), Is.EqualTo(ChatRejectReason.None));
        }
    }
}
