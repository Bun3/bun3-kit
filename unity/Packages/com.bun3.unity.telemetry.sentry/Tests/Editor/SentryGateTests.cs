using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Sentry;
using UnityEngine;

namespace Bun3.Unity.Telemetry.Sentry.Tests
{
    public class SentryGateTests
    {
        bool savedConsent, savedEverOff;
        Func<long> savedClock;
        TelemetryHub savedHub;
        string dir;

        [SetUp] public void SetUp()
        {
            savedConsent = SentryGate.Consent;
            savedEverOff = SentryGate.ConsentEverOff;
            savedClock = SentryGate.Clock;
            savedHub = Telemetry.Hub;
            SentryGate.ConsentEverOff = false;
            SentryGate.Consent = true;
            SentryGate.ResetAttachmentTurn();
            dir = Path.Combine(Application.temporaryCachePath, "telemetry-tests", Guid.NewGuid().ToString("N"));
        }

        [TearDown] public void TearDown()
        {
            SentryGate.Consent = savedConsent;
            SentryGate.ConsentEverOff = savedEverOff;
            SentryGate.Clock = savedClock;
            SentryGate.ResetAttachmentTurn();
            Telemetry.Hub = savedHub;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        void UseLocalSink()
        {
            var hub = new TelemetryBuilder(null)
                .Add(new LocalJsonlSink(dir, "run", DateTime.UtcNow, 3, 100000), TelemetryTier.All)
                .Build();
            hub.Track(TelemetryCategory.Ui, "clicked");
            Telemetry.Hub = hub;
        }

        static SentryHint Send(out SentryEvent result)
        {
            var hint = new SentryHint();
            result = SentryGate.BeforeSend(new SentryEvent(), hint);
            return hint;
        }

        [Test] public void AttachesLocalTail()
        {
            UseLocalSink();
            var hint = Send(out var e);
            Assert.IsNotNull(e);
            var a = hint.Attachments.Single();
            Assert.AreEqual("telemetry.jsonl", a.FileName);
            using var s = a.Content.GetStream();
            StringAssert.Contains("\"clicked\"", new StreamReader(s).ReadToEnd());
        }

        [Test] public void NoHub_SendsWithoutAttachment()
        {
            Telemetry.Hub = null;
            var hint = Send(out var e);
            Assert.IsNotNull(e);
            Assert.AreEqual(0, hint.Attachments.Count);
        }

        [Test] public void NoLocalSink_SendsWithoutAttachment()
        {
            Telemetry.Hub = new TelemetryBuilder(null).Add(new HistorySink(), TelemetryTier.All).Build();
            var hint = Send(out var e);
            Assert.IsNotNull(e);
            Assert.AreEqual(0, hint.Attachments.Count);
        }

        [Test] public void ConsentOffOnce_NeverAttachesAgain()
        {
            UseLocalSink();
            SentryGate.Consent = false;
            SentryGate.Consent = true;
            var hint = Send(out var e);
            Assert.IsNotNull(e);
            Assert.AreEqual(0, hint.Attachments.Count);
        }

        [Test] public void AttachmentThrottled_60Seconds()
        {
            UseLocalSink();
            long now = 1000;
            SentryGate.Clock = () => now;
            var first = Send(out _);
            now += SentryGate.AttachmentInterval - 1;
            var second = Send(out var e);
            now += 1;
            var third = Send(out _);

            Assert.IsNotNull(e, "The event is still sent");
            Assert.AreEqual(1, first.Attachments.Count);
            Assert.AreEqual(0, second.Attachments.Count);
            Assert.AreEqual(1, third.Attachments.Count);
        }

        [Test] public void NoConsent_DropsEventAndBreadcrumb()
        {
            UseLocalSink();
            SentryGate.Consent = false;
            var hint = Send(out var e);
            Assert.IsNull(e);
            Assert.AreEqual(0, hint.Attachments.Count);
            Assert.IsNull(SentryGate.BeforeBreadcrumb(new Breadcrumb("m", "t"), new SentryHint()));
        }

        [Test] public void Consent_KeepsBreadcrumb()
        {
            var b = new Breadcrumb("m", "t");
            Assert.AreSame(b, SentryGate.BeforeBreadcrumb(b, new SentryHint()));
        }

        [Test] public void SubscribeLinksConsentChangesToTheGate()
        {
            var consent = true;
            SentryGate.Register(() => consent);
            try
            {
                SentryGate.Subscribe();
                Assert.IsTrue(SentryGate.Consent);
                consent = false;
                Telemetry.NotifyConsentChanged();
                Assert.IsFalse(SentryGate.Consent);
                Assert.IsTrue(SentryGate.ConsentEverOff);
            }
            finally
            {
                Telemetry.ConsentChanged -= SentryGate.Refresh;
                SentryGate.Register(null);
            }
        }

        [Test] public void ConsentReadFailure_DoesNotLatchOff()
        {
            SentryGate.RefreshFrom(() => throw new InvalidOperationException());
            Assert.IsFalse(SentryGate.Consent);
            Assert.IsFalse(SentryGate.ConsentEverOff);

            SentryGate.RefreshFrom(() => false);
            Assert.IsTrue(SentryGate.ConsentEverOff);
        }
    }
}
