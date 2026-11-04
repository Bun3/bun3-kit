using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Sentry.Unity;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Telemetry.Sentry.Tests
{
    public class SentrySettingsTests
    {
        static TelemetryEnvironmentOptions Env(string dsn = "dsn", string name = "release", bool debug = false) => new()
        {
            Name = name,
            Sinks = new List<TelemetrySinkOptions> { new SentrySinkOptions { Dsn = dsn, Debug = debug, Tiers = TelemetryTier.All } },
        };

        [Test] public void Building_IgnoresConsent() =>
            Assert.IsTrue(TelemetrySentryConfiguration.Settings(Env(), false, true, "0.1.0").Enabled);

        [Test] public void Building_StillNeedsDsn() =>
            Assert.IsFalse(TelemetrySentryConfiguration.Settings(Env(""), false, true, "0.1.0").Enabled);

        [TestCase("", true, false)]
        [TestCase("dsn", false, false)]
        [TestCase("dsn", true, true)]
        public void Runtime_RequiresConsentAndDsn(string dsn, bool consent, bool expected) =>
            Assert.AreEqual(expected, TelemetrySentryConfiguration.Settings(Env(dsn), consent, false, "0.1.0").Enabled);

        [Test] public void MissingOptions_Disabled()
        {
            Assert.IsFalse(TelemetrySentryConfiguration.Settings(null, true, true, "0.1.0").Enabled);
            Assert.IsFalse(TelemetrySentryConfiguration.Settings(new TelemetryEnvironmentOptions { Name = "x" }, true, true, "0.1.0").Enabled);
        }

        [Test] public void ValuesFromBundle()
        {
            var s = TelemetrySentryConfiguration.Settings(Env(name: "release", debug: true), true, false, "0.1.0");
            Assert.AreEqual("dsn", s.Dsn);
            Assert.AreEqual("release", s.Environment);
            Assert.AreEqual("0.1.0", s.Release);
            Assert.IsTrue(s.Debug);
            Assert.AreEqual(100, s.MaxBreadcrumbs);
        }

        [Test] public void Configure_SetsFixedOptions()
        {
            var configure = ScriptableObject.CreateInstance<TestSentryConfiguration>();
            configure.Env = Env();
            try
            {
                var o = new SentryUnityOptions { AutoSessionTracking = true, AttachScreenshot = true, AttachViewHierarchy = true };
                configure.Configure(o);
                Assert.IsTrue(o.Enabled);
                Assert.AreEqual("dsn", o.Dsn);
                Assert.IsFalse(o.SendDefaultPii);
                Assert.AreEqual(1f, o.SampleRate);
                Assert.AreEqual(0d, o.TracesSampleRate);
                Assert.IsTrue(o.CaptureLogErrorEvents);
                Assert.IsFalse(o.AutoSessionTracking);
                Assert.IsFalse(o.AttachScreenshot);
                Assert.IsFalse(o.AttachViewHierarchy);
                foreach (var t in new[] { LogType.Log, LogType.Warning, LogType.Assert, LogType.Error, LogType.Exception })
                    Assert.IsFalse(o.AddBreadcrumbsForLogType[t], t.ToString());
            }
            finally { Object.DestroyImmediate(configure); }
        }

        [Test] public void Configure_Throws_DisablesSdk()
        {
            var configure = ScriptableObject.CreateInstance<TestSentryConfiguration>();
            configure.Throw = true;
            try
            {
                var o = new SentryUnityOptions { Enabled = true };
                LogAssert.Expect(LogType.Warning, new Regex("boom"));
                configure.Configure(o);
                Assert.IsFalse(o.Enabled);
            }
            finally { Object.DestroyImmediate(configure); }
        }
    }
}
