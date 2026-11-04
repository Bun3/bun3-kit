# Bun3 Unity Telemetry Sentry

Sentry sink for `com.bun3.unity.telemetry`. Same requirements as the core package (Unity 6000.5 or newer, ZString in the project manifest), plus `io.sentry.unity` in the project manifest.

- `builder.AddSentry(tagKeys, hook)` attaches a `SentrySink` when the environment has `SentrySinkOptions` with a DSN and at least one tier.
- Every record becomes a breadcrumb (`Analytics` at info level, the rest at debug level). `run_id`, `build` and `environment` are set as tags once; each key in `tagKeys` follows `TelemetryContext.Extra`; `distinct_id` becomes the Sentry user.
- A `SentryRecordHook` can handle a record itself and skip the default breadcrumb.
- Derive from `TelemetrySentryConfiguration` and assign it as the Sentry options configuration. It applies the fixed SDK options (no default PII, no sessions, no screenshots, no automatic log breadcrumbs) and enables the SDK when the DSN is set and the player consents (always while building).
- Events and breadcrumbs are dropped without consent. With consent, an error report carries the last 1 MiB of the local JSONL file as `telemetry.jsonl`, at most once per 60 seconds and never after consent was off during the run.
- Call `Telemetry.NotifyConsentChanged()` when the consent answer changes.
