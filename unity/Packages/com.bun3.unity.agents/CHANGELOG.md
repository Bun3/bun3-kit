# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
