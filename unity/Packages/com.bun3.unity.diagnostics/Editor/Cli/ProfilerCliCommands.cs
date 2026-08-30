using System;
using Unity.Pipeline.Commands;

namespace Bun3.Unity.Diagnostics
{
    // Terminal entry points for AI-driven profiling: bounded recording, then a synchronous dump.
    // Compiled only when com.unity.pipeline is installed (BUN3_UNITY_PIPELINE constraint on this
    // assembly).
    static class ProfilerCliCommands
    {
        [Serializable]
        public sealed class RecordResponse
        {
            public bool running;
            public bool started;
            public int targetFrames;
            public string message = "";
        }

        [Serializable]
        public sealed class RecordStatusResponse
        {
            public bool running;
            public int bufferedFrames;
            public string message = "";
        }

        [CliCommand(
            "profiler_record",
            "Clear the profiler buffer and record N frames, auto-stopping; poll profiler_record_status, then run profiler_dump",
            MainThreadRequired = true)]
        public static RecordResponse Record(
            [CliArg("frames", "Frames to record before auto-stop")] int frames = 300)
        {
            if (ProfilerDumper.IsRecording)
                return new RecordResponse { running = true, message = "A recording is already running." };

            _ = ProfilerDumper.RecordAsync(frames);
            return new RecordResponse
            {
                running = true,
                started = true,
                targetFrames = frames,
                message = "Recording. Poll profiler_record_status, then run profiler_dump.",
            };
        }

        [CliCommand(
            "profiler_record_status",
            "Report bounded profiler recording progress and buffered frame count",
            MainThreadRequired = true)]
        public static RecordStatusResponse RecordStatus()
        {
            bool running = ProfilerDumper.IsRecording;
            return new RecordStatusResponse
            {
                running = running,
                bufferedFrames = ProfilerDumper.BufferedFrameCount,
                message = running ? "Recording in progress." : "Not recording.",
            };
        }

        [CliCommand(
            "profiler_dump",
            "Dump the profiler buffer (or a saved capture file) to Markdown/JSON reports with spike and marker analysis",
            MainThreadRequired = true)]
        public static ProfilerDumpResult Dump(
            [CliArg("file", "Optional saved capture (.data/.raw) to load instead of the live buffer")] string file = "",
            [CliArg("budget_ms", "CPU frame budget in ms for spike reporting")] float budgetMs = ProfilerDumper.DefaultBudgetMs)
        {
            return string.IsNullOrEmpty(file)
                ? ProfilerDumper.Dump(budgetMs)
                : ProfilerDumper.LoadAndDump(file, budgetMs);
        }
    }
}
