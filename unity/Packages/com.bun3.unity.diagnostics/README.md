# Bun3 Unity Diagnostics

Editor-only Frame Debugger dump and batching analysis.

## Usage

1. Enter play mode, pause at the frame you care about (pause is automated when you forget).
2. Enable the Frame Debugger (auto-enabled when possible).
3. Menu `Tools/Bun3/Frame Debugger Dump`, or call `Bun3.Unity.Diagnostics.FrameDebuggerDumper.DumpAsync()`.
4. Reports land in `<project>/FrameDebuggerDump/FrameDebuggerDump_<timestamp>.md` and `.json`.

With `com.unity.pipeline` installed (Unity 6), the CLI commands `framedebugger_dump` /
`framedebugger_dump_status` are also registered for terminal-driven capture.

## Install (git URL)

`https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.diagnostics`

Minimum Unity: 2022.3. No dependencies.
