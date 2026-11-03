using System;
using System.Threading;
using Sentry;
using UnityEngine;

namespace Bun3.Unity.Telemetry.Sentry
{
    // BeforeSend and BeforeBreadcrumb run on other threads, so they read only the values the main thread stored here.
    internal static class SentryGate
    {
        internal const int AttachmentBytes = 1024 * 1024;
        internal static readonly long AttachmentInterval = 60 * System.Diagnostics.Stopwatch.Frequency;
        const long NeverAttached = long.MinValue;

        static volatile bool consent;
        static volatile bool consentEverOff;
        static Func<bool> reader;
        static long lastAttached = NeverAttached;

        internal static Func<long> Clock = System.Diagnostics.Stopwatch.GetTimestamp;

        internal static bool Consent { get => consent; set => SetConsent(value); }

        // Once consent was off, the local file holds lines from that period; it is not attached again this run.
        internal static bool ConsentEverOff { get => consentEverOff; set => consentEverOff = value; }

        static void SetConsent(bool value)
        {
            if (!value) consentEverOff = true;
            consent = value;
        }

        internal static void ResetAttachmentTurn() => Interlocked.Exchange(ref lastAttached, NeverAttached);

        static bool TakeAttachmentTurn()
        {
            long now = Clock(), last = Interlocked.Read(ref lastAttached);
            if (last != NeverAttached && now - last < AttachmentInterval) return false;
            return Interlocked.CompareExchange(ref lastAttached, now, last) == last;
        }

        internal static void Register(Func<bool> read)
        {
            reader = read;
            Refresh();
        }

        // Main thread only.
        internal static void Refresh()
        {
            if (reader != null) RefreshFrom(reader);
        }

        // A failed read is not a refusal: nothing is sent this time, but the opt-out latch stays untouched.
        internal static void RefreshFrom(Func<bool> read)
        {
            bool value;
            try { value = read(); }
            catch (Exception) { consent = false; return; }
            SetConsent(value);
        }

        // The registered reader and the consent value survive: SDK initialization may already have run.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Clock = System.Diagnostics.Stopwatch.GetTimestamp;
            ResetAttachmentTurn();
            consentEverOff = false;
            Refresh();
        }

        // The core clears Telemetry.ConsentChanged at SubsystemRegistration, so subscribe after it.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void Subscribe()
        {
            Telemetry.ConsentChanged -= Refresh;
            Telemetry.ConsentChanged += Refresh;
            Refresh();
        }

        internal static SentryEvent BeforeSend(SentryEvent e, SentryHint h)
        {
            if (!consent) return null;
            try
            {
                var sink = Telemetry.Hub?.Sink<LocalJsonlSink>();
                var bytes = sink != null && !consentEverOff && TakeAttachmentTurn() ? sink.ReadTail(AttachmentBytes) : null;
                if (bytes != null) h.AddAttachment(bytes, "telemetry.jsonl", AttachmentType.Default, "application/x-ndjson");
            }
            catch (Exception) { }
            return e;
        }

        internal static Breadcrumb BeforeBreadcrumb(Breadcrumb b, SentryHint h) => consent ? b : null;
    }
}
