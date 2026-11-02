# Bun3 Unity Telemetry

Game-agnostic telemetry for Unity.

- `Telemetry.Track(category, name, props)` is the single entry point; it is a no-op until a hub is installed.
- `TelemetryBuilder` assembles a `TelemetryHub`: sinks are attached one line at a time with the tiers they receive, remote sinks are gated by a consent callback, and keys the game lists are dropped from every record.
- The kit fills `run_id`, `build`, `environment` and `scene`; the game fills the rest through a `TelemetryContextFill` callback.
- `TelemetryEnvironmentOptions` holds per-environment sink options that the game authors in its own settings asset.
- `ConsoleSink` prints each record as one JSON line.
