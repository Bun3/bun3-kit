# Changelog

## [0.1.0] - 2026-11-03

Requires Unity 6000.5 or newer; the assemblies are skipped on older editors.

### Added

- Record types, categories and tiers, `TelemetryHub`, `TelemetryBuilder`, `TelemetryEnvironmentOptions`, JSON line formatting, `ConsoleSink` and the `Telemetry` entry point.
- `Telemetry.Install` and `Uninstall` with automatic engine log, scene change and UI interaction records, `ITelemetryCapture`, `TelemetryBuilder.EngineLogPrefixes` and `Capture`.
- `LocalJsonlSink` and `HistorySink`.
- Optional `Bun3.Unity.Telemetry.Popups` assembly (requires `com.bun3.unity.ui`) with `PopupCapture`, attached through `TelemetryBuilder.Capture`.
