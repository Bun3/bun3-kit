# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.4.2] - 2026-08-29

### Fixed

- Adoption placeholders (pid -1) expire after the deep-quiet threshold when no hook
  event ever replaces them - adopting a session that turned out to be dead no longer
  leaves an immortal sleeping zombie. Consumers should adopt with pid -1.

## [0.4.1] - 2026-08-29

### Fixed

- The Claude heartbeat script sweeps its session chick files on Stop/StopFailure/
  SessionEnd - a subagent killed without SubagentStop no longer lingers as an orphan
  duckling (chick pids point at the living parent, so pid pruning cannot catch them).

## [0.4.0] - 2026-08-29

### Changed

- The default provider manifests (Claude, Gemini, Cursor, Codex, ChatGPT) now ship
  inside the package and are SEEDED into the add-on directory
  (`%LOCALAPPDATA%/bun3-agents/providers/`) on first run - the defaults are add-ons,
  sitting exactly where a community manifest goes, as live examples users can copy or
  edit. An existing file of the same name is never overwritten (edits win; delete to
  restore the bundled version). Consumers no longer need their own
  StreamingAssets/providers, though that layer still works as an app-level override.

## [0.3.0] - 2026-08-29

### Changed

- Protocol namespace is `%LOCALAPPDATA%/bun3-agents/` with `bun3-agent-*.ps1` scripts
  and the `bun3-agent` install marker. All pre-rename (`ai-office`) migration shims
  were removed - nothing had shipped, so there is nothing to migrate.

## [0.1.0] - 2026-08-28

### Added

- Initial extraction from ai-office: heartbeat protocol + store (dead-pid pruning),
  provider manifest registry with community add-on support, hook auto-install and
  clean uninstall for Claude / Gemini / Cursor / Codex (three settings schemas,
  scripts bundled as Resources TextAssets), transcript-based session discovery,
  desktop-app process watcher.
