# Bun3 Unity Telemetry

Game-agnostic telemetry for Unity.

Requires Unity 6000.5 or newer: ZString needs the public `System.Runtime.CompilerServices.Unsafe` that 6000.5 ships, and every telemetry assembly is constrained to `UNITY_6000_5_OR_NEWER`, so older editors skip them instead of failing to compile. UPM ignores git dependencies inside `package.json`, so the project manifest must list `com.cysharp.unitask` and `com.cysharp.zstring` itself.

- `Telemetry.Track(category, name, props)` is the single entry point; it is a no-op until a hub is installed.
- `TelemetryBuilder` assembles a `TelemetryHub`: sinks are attached one line at a time with the tiers they receive, remote sinks are gated by a consent callback, and keys the game lists are dropped from every record.
- The kit fills `run_id`, `build`, `environment` and `scene`; the game fills the rest through a `TelemetryContextFill` callback.
- `TelemetryEnvironmentOptions` holds per-environment sink options that the game authors in its own settings asset.
- `ConsoleSink` prints each record as one JSON line.
- `Telemetry.Install(builder)` installs the builder's captures: engine log lines, scene changes, UI interactions (a path and text length, never input text) and any `ITelemetryCapture` added with `TelemetryBuilder.Capture`.
- `LocalJsonlSink` writes records to JSON lines files and `HistorySink` keeps the recent records of the current run.
- Optional popup recording lives in the `Bun3.Unity.Telemetry.Popups` assembly, which compiles only when `com.bun3.unity.ui` is installed: `builder.Capture(new PopupCapture(stack))` records `popup_opened` and `popup_closed` (with `seconds_open`) for that `PopupStack`.

## Minimal setup

```csharp
var runId = Guid.NewGuid().ToString("N");
var builder = new TelemetryBuilder(environment)       // environment: the TelemetryEnvironmentOptions bundle for this build
    .RunId(runId)                                     // the same value goes to LocalJsonlSink so its file name matches the records' run_id
    .Context((ref TelemetryContext c) => c.DistinctId = userId)
    .Consent(() => userAllowsReporting)
    .Add(new LocalJsonlSink(Path.Combine(Application.persistentDataPath, "logs"), runId, DateTime.UtcNow, keepRuns: 20, maxFileBytes: 8 * 1024 * 1024), TelemetryTier.All)
    .AddConsole()                                     // attaches only when the bundle has console options
    .AddPostHog()                                     // attaches only when the bundle has PostHog options with a key
    .AddSentry();                                     // attaches only when the bundle has Sentry options with a DSN
Telemetry.Install(builder);
Telemetry.Track(TelemetryCategory.Analytics, "level_started", ("level", 3));
```

`LocalJsonlSink` takes the run id at construction, so pass the same value to the builder's `RunId`. `AddPostHog` and `AddSentry` come from the optional PostHog and Sentry packages and read their options from the environment bundle, so a bundle without a key or DSN attaches nothing.
