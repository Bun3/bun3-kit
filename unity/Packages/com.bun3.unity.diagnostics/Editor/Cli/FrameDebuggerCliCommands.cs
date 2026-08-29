using System;
using Unity.Pipeline.Commands;

namespace Bun3.Unity.Diagnostics
{
    // Terminal entry points for AI-driven capture: start the dump, then poll status until the
    // report paths come back. Compiled only when com.unity.pipeline is installed (see the
    // BUN3_UNITY_PIPELINE define constraint on this assembly).
    static class FrameDebuggerCliCommands
    {
        [Serializable]
        public sealed class DumpStatusResponse
        {
            public bool running;
            public bool started;
            public string message = "";
            public FrameDumpResult result;
        }

        [CliCommand(
            "framedebugger_dump",
            "Start dumping the current Frame Debugger capture to Markdown/JSON reports with batching analysis; poll framedebugger_dump_status for the result",
            MainThreadRequired = true)]
        public static DumpStatusResponse StartDump()
        {
            if (FrameDebuggerDumper.IsRunning)
                return new DumpStatusResponse { running = true, message = "A dump is already running." };

            _ = FrameDebuggerDumper.DumpAsync();
            bool running = FrameDebuggerDumper.IsRunning;
            return new DumpStatusResponse
            {
                running = running,
                started = true,
                message = running ? "Dump started. Poll framedebugger_dump_status." : Describe(FrameDebuggerDumper.LastResult),
                result = running ? null : FrameDebuggerDumper.LastResult,
            };
        }

        [CliCommand(
            "framedebugger_dump_status",
            "Report the running/last Frame Debugger dump state and report paths",
            MainThreadRequired = true)]
        public static DumpStatusResponse Status()
        {
            bool running = FrameDebuggerDumper.IsRunning;
            return new DumpStatusResponse
            {
                running = running,
                message = running ? "Dump in progress." : Describe(FrameDebuggerDumper.LastResult),
                result = running ? null : FrameDebuggerDumper.LastResult,
            };
        }

        static string Describe(FrameDumpResult result) =>
            result == null ? "No dump has run yet."
            : result.success ? $"Dump complete: {result.markdownPath}"
            : $"Dump failed: {result.error}";
    }
}
