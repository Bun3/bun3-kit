# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.1] - 2026-08-29

### Fixed

- Migrated ids left a stale copy in the legacy heartbeat directory; the fallback read
  fed both to consumers and the stale one dragged every fresh worker into Sleeping.
  A legacy file whose id already exists in the new directory is now deleted on read.

## [0.2.0] - 2026-08-29

### Changed

- **Protocol namespace decoupled from the first consumer app**: heartbeats, hook
  scripts, and add-on manifests now live under `%LOCALAPPDATA%/bun3-agents/`
  (was `ai-office/`), scripts are `bun3-agent-*.ps1`, and the install marker is
  `bun3-agent`. Migration is automatic: installing strips legacy `ai-office`
  entries from CLI settings (a legacy Codex notify line is recognized as ours and
  replaced without being chained), the captured Codex original is carried over,
  and the store keeps reading the legacy heartbeat directory so sessions hooked
  before the rename stay visible until they restart. Uninstall removes both
  marker generations.

## [0.1.0] - 2026-08-28

### Added

- Initial extraction from ai-office: heartbeat protocol + store (dead-pid pruning),
  provider manifest registry with community add-on support, hook auto-install and
  clean uninstall for Claude / Gemini / Cursor / Codex (three settings schemas,
  scripts bundled as Resources TextAssets), transcript-based session discovery,
  desktop-app process watcher.
