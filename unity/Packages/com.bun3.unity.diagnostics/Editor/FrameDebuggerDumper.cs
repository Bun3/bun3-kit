using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Bun3.Unity.Diagnostics
{
    /// <summary>Outcome of one Frame Debugger dump run.</summary>
    [Serializable]
    public sealed class FrameDumpResult
    {
        /// <summary>True when the dump completed and reports were written.</summary>
        public bool success;

        /// <summary>Failure description; empty on success.</summary>
        public string error = "";

        /// <summary>Event count reported by the Frame Debugger when the dump started.</summary>
        public int eventCount;

        /// <summary>Events actually captured (can be lower when the capture shrank mid-dump).</summary>
        public int capturedCount;

        /// <summary>Absolute path of the Markdown report; empty when none was written.</summary>
        public string markdownPath = "";

        /// <summary>Absolute path of the JSON report; empty when none was written.</summary>
        public string jsonPath = "";

        /// <summary>Most frequent batch-break cause, formatted "cause (count)".</summary>
        public string topBreakCause = "";
    }

    /// <summary>
    /// Dumps the current Frame Debugger capture to Markdown + JSON reports with batching analysis.
    /// The Frame Debugger only prepares detail data for the event its limit points at, so the dump
    /// walks the limit one event at a time on EditorApplication.update, polling until the replay
    /// catches up, with a timeout skip per event. If the game advances during the dump (live
    /// server traffic), the event count can drift; a shrinking capture ends the dump early with
    /// the events gathered so far.
    /// </summary>
    public static class FrameDebuggerDumper
    {
        const int MaxAttemptsPerStep = 300;
        const string OutputDirName = "FrameDebuggerDump";

        static TaskCompletionSource<FrameDumpResult> s_Tcs;
        static List<FrameEvent> s_Events;
        static string[] s_EventTypes;
        static string[] s_BreakCauses;
        static int s_Index;
        static int s_Count;
        static int s_Attempts;
        static bool s_WaitingForCapture;

        /// <summary>True while a dump traversal is in progress.</summary>
        public static bool IsRunning => s_Tcs != null && !s_Tcs.Task.IsCompleted;

        /// <summary>Result of the most recent completed dump; null before the first one.</summary>
        public static FrameDumpResult LastResult { get; private set; }

        [MenuItem("Tools/Bun3/Frame Debugger Dump")]
        static void DumpMenu() => DumpAsync();

        /// <summary>
        /// Starts dumping the current Frame Debugger capture. Pauses play mode and tries to enable
        /// the Frame Debugger when there is no capture yet. Returns the in-flight task when a dump
        /// is already running.
        /// </summary>
        public static Task<FrameDumpResult> DumpAsync()
        {
            if (IsRunning)
                return s_Tcs.Task;
            s_Tcs = new TaskCompletionSource<FrameDumpResult>();

            try
            {
                if (EditorApplication.isPlaying && !EditorApplication.isPaused)
                    EditorApplication.isPaused = true;

                var missing = FrameDebuggerReflection.Bind();
                if (!FrameDebuggerReflection.IsBound)
                {
                    Complete(Fail($"Frame Debugger reflection bindings missing: {string.Join(", ", missing)}"));
                    return s_Tcs.Task;
                }

                s_Attempts = 0;
                s_Count = FrameDebuggerReflection.GetCount();
                if (s_Count <= 0)
                {
                    if (!FrameDebuggerReflection.TryEnable())
                    {
                        Complete(Fail("No Frame Debugger capture and auto-enable is unavailable. Enable the Frame Debugger window and retry."));
                        return s_Tcs.Task;
                    }

                    s_WaitingForCapture = true;
                }
                else
                {
                    BeginTraversal();
                }

                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
            }
            catch (Exception e)
            {
                EditorApplication.update -= Tick;
                Complete(Fail($"Dump start failed: {e.GetBaseException().Message}"));
            }
            return s_Tcs.Task;
        }

        static void BeginTraversal()
        {
            s_WaitingForCapture = false;
            s_EventTypes = FrameDebuggerReflection.GetEventTypeNames(s_Count);
            s_BreakCauses = FrameDebuggerReflection.GetBreakCauseStrings();
            s_Events = new List<FrameEvent>(s_Count);
            s_Index = 0;
            s_Attempts = 0;
            FrameDebuggerReflection.SetLimit(1);
        }

        static void Tick()
        {
            try
            {
                if (s_WaitingForCapture)
                {
                    s_Count = FrameDebuggerReflection.GetCount();
                    if (s_Count > 0)
                    {
                        BeginTraversal();
                    }
                    else if (++s_Attempts > MaxAttemptsPerStep)
                    {
                        Finish("Frame Debugger produced no capture after enabling.");
                    }
                    else
                    {
                        FrameDebuggerReflection.Repaint();
                    }

                    return;
                }

                if (FrameDebuggerReflection.TryGetEventData(s_Index, out var fields))
                {
                    s_Events.Add(FrameEvent.FromFields(
                        s_Index, s_EventTypes[s_Index], fields, s_BreakCauses, ResolveGameObjectPath(fields)));
                    Advance();
                }
                else if (++s_Attempts > MaxAttemptsPerStep)
                {
                    // The game may have advanced during the dump; once the live event count shrank
                    // below the current index the remaining indices can never resolve.
                    if (s_Index >= FrameDebuggerReflection.GetCount())
                    {
                        Finish(null);
                        return;
                    }

                    s_Events.Add(new FrameEvent { index = s_Index, eventType = s_EventTypes[s_Index] });
                    Advance();
                }
                else
                {
                    FrameDebuggerReflection.Repaint();
                }

                if (IsRunning && s_Index % 5 == 0)
                    EditorUtility.DisplayProgressBar(
                        "Frame Debugger dump", $"{s_Index}/{s_Count}", (float)s_Index / s_Count);
            }
            catch (Exception e)
            {
                Finish(e.GetBaseException().Message);
            }
        }

        static void Advance()
        {
            s_Index++;
            s_Attempts = 0;
            if (s_Index >= s_Count)
                Finish(null);
            else
                FrameDebuggerReflection.SetLimit(s_Index + 1);
        }

        static void Finish(string error)
        {
            EditorApplication.update -= Tick;
            EditorUtility.ClearProgressBar();
            try
            {
                if (s_Count > 0)
                    FrameDebuggerReflection.SetLimit(s_Count);
            }
            catch
            {
            }

            if (s_Events == null || s_Events.Count == 0)
            {
                Complete(Fail(error ?? "No events captured."));
                return;
            }

            try
            {
                var analysis = FrameDumpAnalyzer.Analyze(s_Events);
                var now = DateTime.Now;
                var dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, OutputDirName);
                Directory.CreateDirectory(dir);
                var stamp = now.ToString("yyyyMMdd_HHmmss");
                var mdPath = Path.Combine(dir, $"FrameDebuggerDump_{stamp}.md");
                var jsonPath = Path.Combine(dir, $"FrameDebuggerDump_{stamp}.json");
                var timestamp = now.ToString("yyyy-MM-dd HH:mm:ss");
                File.WriteAllText(mdPath, FrameDumpReportWriter.ToMarkdown(timestamp, s_Events, analysis), Encoding.UTF8);
                File.WriteAllText(jsonPath, FrameDumpReportWriter.ToJson(timestamp, s_Events, analysis), Encoding.UTF8);

                var top = analysis.callsByBreakCause.Count > 0 ? analysis.callsByBreakCause[0] : null;
                var result = new FrameDumpResult
                {
                    success = error == null,
                    error = error ?? "",
                    eventCount = s_Count,
                    capturedCount = s_Events.Count,
                    markdownPath = mdPath,
                    jsonPath = jsonPath,
                    topBreakCause = top == null ? "" : $"{top.key} ({top.count})",
                };
                Debug.Log(
                    $"Frame Debugger dump: {result.capturedCount}/{result.eventCount} events\n" +
                    $"Top batch breaker: {result.topBreakCause}\n" +
                    $"Report: {mdPath}");
                Complete(result);
            }
            catch (Exception ex)
            {
                Complete(Fail($"Report write failed: {ex.GetBaseException().Message}"));
            }
        }

        static FrameDumpResult Fail(string error) => new FrameDumpResult { success = false, error = error };

        static void Complete(FrameDumpResult result)
        {
            LastResult = result;
            s_Events = null;
            s_Tcs.TrySetResult(result);
        }

        static string ResolveGameObjectPath(IReadOnlyDictionary<string, string> fields)
        {
            if (!fields.TryGetValue("ComponentInstanceID", out var raw)
                || !int.TryParse(raw, out var id)
                || id == 0)
            {
                return "";
            }

            var obj = EditorUtility.InstanceIDToObject(id);
            if (obj is Component c)
                return HierarchyPath(c.gameObject);
            return obj ? obj.name : "";
        }

        static string HierarchyPath(GameObject go)
        {
            var sb = new StringBuilder(go.name);
            for (var t = go.transform.parent; t != null; t = t.parent)
                sb.Insert(0, t.name + "/");
            return sb.ToString();
        }
    }
}
