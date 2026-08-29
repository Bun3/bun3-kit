using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Bun3.Unity.Diagnostics
{
    /// <summary>Outcome of one profiler dump.</summary>
    [Serializable]
    public sealed class ProfilerDumpResult
    {
        /// <summary>True when the dump completed and reports were written.</summary>
        public bool success;

        /// <summary>Failure description; empty on success.</summary>
        public string error = "";

        /// <summary>Data source: "live" or "file:&lt;path&gt;".</summary>
        public string source = "";

        /// <summary>Number of analyzed frames.</summary>
        public int frameCount;

        /// <summary>Worst main-thread CPU frame time, in milliseconds.</summary>
        public float worstCpuMs;

        /// <summary>Heaviest marker by summed self time, formatted for display.</summary>
        public string topMarker = "";

        /// <summary>Absolute path of the Markdown report; empty when none was written.</summary>
        public string markdownPath = "";

        /// <summary>Absolute path of the JSON report; empty when none was written.</summary>
        public string jsonPath = "";
    }

    /// <summary>
    /// Dumps the profiler frame buffer to Markdown + JSON with spike and marker analysis.
    /// Dumping is synchronous; <see cref="RecordAsync"/> is the only asynchronous piece — it
    /// records a bounded number of frames on EditorApplication.update, then stops the profiler.
    /// </summary>
    public static class ProfilerDumper
    {
        /// <summary>Default per-frame CPU budget for spike reporting (30 FPS).</summary>
        public const float DefaultBudgetMs = 33.3f;

        const string OutputDirName = "ProfilerDump";
        const int RecordStallTicksLimit = 1000;

        static TaskCompletionSource<int> s_RecordTcs;
        static int s_RecordTargetFrames;
        static int s_RecordStallTicks;
        static int s_RecordLastSeenFrame;

        /// <summary>True while a bounded recording is in progress.</summary>
        public static bool IsRecording => s_RecordTcs != null && !s_RecordTcs.Task.IsCompleted;

        /// <summary>Frames currently held by the profiler buffer.</summary>
        public static int BufferedFrameCount => ProfilerCapture.FrameCount;

        /// <summary>Result of the most recent dump; null before the first one.</summary>
        public static ProfilerDumpResult LastResult { get; private set; }

        [MenuItem("Tools/Bun3/Profiler Dump")]
        static void DumpMenu() => Dump();

        /// <summary>Analyzes the current profiler buffer and writes the reports.</summary>
        public static ProfilerDumpResult Dump(float budgetMs = DefaultBudgetMs) =>
            DumpInternal("live", budgetMs);

        /// <summary>Loads a saved profiler capture into the buffer (replacing it), then dumps.</summary>
        public static ProfilerDumpResult LoadAndDump(string path, float budgetMs = DefaultBudgetMs)
        {
            if (!ProfilerDriver.LoadProfile(path, false))
                return Fail($"Failed to load profiler capture: {path}", $"file:{path}");
            return DumpInternal($"file:{path}", budgetMs);
        }

        /// <summary>
        /// Clears the buffer, records until <paramref name="frameCount"/> frames exist (or the
        /// editor stops producing frames), then disables the profiler. Returns the captured count.
        /// </summary>
        public static Task<int> RecordAsync(int frameCount)
        {
            if (IsRecording)
                return s_RecordTcs.Task;
            s_RecordTcs = new TaskCompletionSource<int>();
            ProfilerDriver.ClearAllFrames();
            ProfilerDriver.enabled = true;
            s_RecordTargetFrames = Math.Max(1, frameCount);
            s_RecordStallTicks = 0;
            s_RecordLastSeenFrame = ProfilerDriver.lastFrameIndex;
            EditorApplication.update -= RecordTick;
            EditorApplication.update += RecordTick;
            return s_RecordTcs.Task;
        }

        static void RecordTick()
        {
            if (ProfilerDriver.lastFrameIndex != s_RecordLastSeenFrame)
            {
                s_RecordLastSeenFrame = ProfilerDriver.lastFrameIndex;
                s_RecordStallTicks = 0;
            }

            int captured = ProfilerCapture.FrameCount;
            if (captured >= s_RecordTargetFrames || ++s_RecordStallTicks > RecordStallTicksLimit)
            {
                EditorApplication.update -= RecordTick;
                ProfilerDriver.enabled = false;
                s_RecordTcs.TrySetResult(captured);
            }
        }

        static ProfilerDumpResult DumpInternal(string source, float budgetMs)
        {
            try
            {
                if (!ProfilerCapture.HasFrames)
                    return Fail("Profiler buffer is empty. Record first (Profiler window, RecordAsync, or profiler_record) or load a capture.", source);

                var samples = ProfilerCapture.ReadAll();
                if (samples.Count == 0)
                    return Fail("Profiler buffer contained no readable frames.", source);

                var analysis = ProfilerDumpAnalyzer.Analyze(samples, budgetMs);
                var frames = samples.ConvertAll(s => s.stat);
                var now = DateTime.Now;
                var dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, OutputDirName);
                Directory.CreateDirectory(dir);
                var stamp = now.ToString("yyyyMMdd_HHmmss");
                var mdPath = Path.Combine(dir, $"ProfilerDump_{stamp}.md");
                var jsonPath = Path.Combine(dir, $"ProfilerDump_{stamp}.json");
                var timestamp = now.ToString("yyyy-MM-dd HH:mm:ss");
                File.WriteAllText(mdPath, ProfilerDumpReportWriter.ToMarkdown(timestamp, source, frames, analysis), Encoding.UTF8);
                File.WriteAllText(jsonPath, ProfilerDumpReportWriter.ToJson(timestamp, source, frames, analysis), Encoding.UTF8);

                var top = analysis.topMarkersBySelfTime.Count > 0 ? analysis.topMarkersBySelfTime[0] : null;
                var result = new ProfilerDumpResult
                {
                    success = true,
                    source = source,
                    frameCount = samples.Count,
                    worstCpuMs = analysis.worstCpuMs,
                    topMarker = top == null ? "" : $"{top.name} ({top.totalSelfMs:F1} ms total)",
                    markdownPath = mdPath,
                    jsonPath = jsonPath,
                };
                Debug.Log(
                    $"Profiler dump: {result.frameCount} frames, worst {result.worstCpuMs:F1} ms\n" +
                    $"Top marker: {result.topMarker}\n" +
                    $"Report: {mdPath}");
                LastResult = result;
                return result;
            }
            catch (Exception e)
            {
                return Fail($"Profiler dump failed: {e.GetBaseException().Message}", source);
            }
        }

        static ProfilerDumpResult Fail(string error, string source)
        {
            var result = new ProfilerDumpResult { success = false, error = error, source = source };
            LastResult = result;
            return result;
        }
    }
}
