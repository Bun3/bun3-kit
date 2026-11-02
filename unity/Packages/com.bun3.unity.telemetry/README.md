# Bun3 Unity Telemetry

Game-agnostic telemetry for Unity.

- `Telemetry.Track(category, name, props)` is the single entry point; it is a no-op until a hub is installed.
- `TelemetryBuilder` assembles a `TelemetryHub`: sinks are attached one line at a time with the tiers they receive, remote sinks are gated by a consent callback, and keys the game lists are dropped from every record.
- The kit fills `run_id`, `build`, `environment` and `scene`; the game fills the rest through a `TelemetryContextFill` callback.
- `TelemetryEnvironmentOptions` holds per-environment sink options that the game authors in its own settings asset.
- `ConsoleSink` prints each record as one JSON line.
- `Telemetry.Install(builder)` installs the builder's captures: engine log lines, scene changes, UI interactions (a path and text length, never input text) and any `ITelemetryCapture` added with `TelemetryBuilder.Capture`.
- `LocalJsonlSink` writes records to JSON lines files and `HistorySink` keeps the recent records of the current run.
- Optional popup recording lives in the `Bun3.Unity.Telemetry.Popups` assembly, which compiles only when `com.bun3.unity.ui` is installed: `builder.CapturePopups(stack)` records `popup_opened` and `popup_closed` (with `seconds_open`) for that `PopupStack`.
