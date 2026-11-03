using System;
using Sentry.Unity;
using UnityEngine;

namespace Bun3.Unity.Telemetry.Sentry
{
    internal struct SentrySettings
    {
        internal const int MaxBreadcrumbCount = 100;

        public bool Enabled, Debug;
        public string Dsn, Environment, Release;
        public int MaxBreadcrumbs;
    }

    /// <summary>
    /// Sentry options configuration for the telemetry pipeline. The SDK calls it at build time and at game start.
    /// It never throws; a failure disables the SDK.
    /// </summary>
    public abstract class TelemetrySentryConfiguration : SentryOptionsConfiguration
    {
        /// <inheritdoc />
        public sealed override void Configure(SentryUnityOptions options)
        {
            try
            {
                var building = Application.isEditor && !Application.isPlaying;
                if (!building) SentryGate.Register(Consent);
                var s = Settings(Environment(building), SentryGate.Consent, building, Application.version);
                options.Enabled = s.Enabled;
                options.Dsn = s.Dsn;
                options.Environment = s.Environment;
                options.Release = s.Release;
                options.Debug = s.Debug;
                options.MaxBreadcrumbs = s.MaxBreadcrumbs;

                options.SendDefaultPii = false;
                options.SampleRate = 1f;
                options.TracesSampleRate = 0d;
                options.Il2CppLineNumberSupportEnabled = true;
                options.WindowsNativeSupportEnabled = true;
                options.AutoSessionTracking = false;
                options.AttachScreenshot = false;
                options.AttachViewHierarchy = false;
                options.AddBreadcrumbsForLogType[LogType.Log] = false;
                options.AddBreadcrumbsForLogType[LogType.Warning] = false;
                options.AddBreadcrumbsForLogType[LogType.Assert] = false;
                options.AddBreadcrumbsForLogType[LogType.Error] = false;
                options.AddBreadcrumbsForLogType[LogType.Exception] = false;
                options.SetBeforeSend(SentryGate.BeforeSend);
                options.SetBeforeBreadcrumb(SentryGate.BeforeBreadcrumb);
            }
            catch (Exception e)
            {
                options.Enabled = false;
                Debug.LogWarning(e);
            }
        }

        /// <summary>The environment options to read the Sentry options from.</summary>
        /// <param name="building">True in the editor while not playing, which is the build post-processing run.</param>
        protected abstract TelemetryEnvironmentOptions Environment(bool building);

        /// <summary>
        /// Whether the player consents to remote reporting. Called on the main thread during SDK initialization, before any scene loads,
        /// and again at subsystem registration. If it reads false then, the SDK stays off for the whole run, so the consent source must be readable that early.
        /// </summary>
        protected abstract bool Consent();

        internal static SentrySettings Settings(TelemetryEnvironmentOptions env, bool consent, bool building, string version)
        {
            var o = env?.Get<SentrySinkOptions>();
            return new SentrySettings
            {
                Enabled = o != null && !string.IsNullOrEmpty(o.Dsn) && (building || consent),
                Debug = o != null && o.Debug,
                Dsn = o?.Dsn,
                Environment = env?.Name,
                Release = version,
                MaxBreadcrumbs = SentrySettings.MaxBreadcrumbCount,
            };
        }
    }
}
