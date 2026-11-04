# Bun3 Unity Telemetry PostHog

PostHog sink for `com.bun3.unity.telemetry`. Same requirements as the core package (Unity 6000.5 or newer, ZString in the project manifest).

- `builder.AddPostHog()` attaches a `PostHogSink` when the environment has `PostHogSinkOptions` with a key and at least one tier.
- Records are batched (20 records or 10 seconds, at most 500 queued) and posted to `<Host>/batch/` with one request in flight.
- Engine log records are never sent. `TelemetryContext.Extra` entries are sent as properties; null values are skipped.
- A quota or key error stops sending for the rest of the run.
