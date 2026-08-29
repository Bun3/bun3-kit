# Unity Profiler Dump Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add profiler-buffer dump + spike/marker analysis (md/json) to `com.bun3.unity.diagnostics`, with a bounded recording helper and unity-cli commands.

**Architecture:** `ProfilerCapture` (internal) reads the `ProfilerDriver` frame buffer via public `HierarchyFrameDataView` APIs into plain data; pure `ProfilerDumpAnalyzer` computes overview/spikes/marker aggregations; `ProfilerDumpReportWriter` renders md/json; `ProfilerDumper` is the synchronous public entry (menu + API) plus the only async piece, `RecordAsync(frameCount)`. CLI commands join the existing gated `Editor/Cli` assembly. No reflection, no binding tests — public APIs are compile-time verified.

**Tech Stack:** UnityEditorInternal.ProfilerDriver + UnityEditor.Profiling.HierarchyFrameDataView, NUnit (+ one `[UnityTest]` EditMode integration test), JsonUtility. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-08-29-unity-profiler-dump-design.md`

## Global Constraints

- Work in the worktree `E:/Projects/orca/workspace/bun3-kit/profiler-dump` on branch `Bun3/profiler-dump` — never touch `E:/Projects/bun3-kit`.
- Verification targets the worktree's own editor: every unity CLI call passes `--project E:/Projects/orca/workspace/bun3-kit/profiler-dump/unity` (two editors may be running; never rely on auto-discovery). Loop: `unity command recompile --project <path>` → poll `recompile_status` until idle → `run_tests --filter <TestClass>`.
- English only in code; all public members carry English XML docs; 0 compile warnings; namespace `Bun3.Unity.Diagnostics`; `Editor/` stays flat; C#9 (target-typed `new` avoided for consistency with existing files).
- Package min Unity stays `2022.3` (spec decision).
- Commit Unity-generated `.meta` files with every commit (`git add` the whole package folder, check `git status --short` afterwards — folder metas included).
- Commit style: gitmoji title + trailer via double `-m`: `git commit -m "<title>" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"`.
- Spike defaults (spec): budget 33.3 ms, worst 5 frames + budget-exceeders, spike list capped at 10, top 10 markers per spike, top 20 markers in aggregations.

## File Structure

```
unity/Packages/com.bun3.unity.diagnostics/
  Editor/
    ProfilerFrameSample.cs        (public data records: ProfilerFrameStat, MarkerSample, ProfilerFrameSample)
    ProfilerDumpAnalyzer.cs       (public: SpikeFrame, ProfilerMarkerStat, ProfilerDumpAnalysis, ProfilerDumpAnalyzer)
    ProfilerDumpReportWriter.cs   (internal writer + ProfilerDumpDocument)
    ProfilerCapture.cs            (internal buffer reader)
    ProfilerDumper.cs             (public API + menu + RecordAsync + ProfilerDumpResult)
    Cli/ProfilerCliCommands.cs    (3 CLI commands, existing gated asmdef)
  Tests/Editor/
    ProfilerDumpAnalyzerTests.cs
    ProfilerDumpReportWriterTests.cs
    ProfilerRecordingTests.cs     ([UnityTest] integration)
  package.json                    (0.1.0 → 0.2.0, description update)
  README.md                       (profiler section)
```

## Setup (before Task 1)

Ensure a Unity editor is open on the worktree project: from `E:/Projects/orca/workspace/bun3-kit/profiler-dump/unity` run `unity open .` in the background and wait until `unity command recompile_status --project <worktree>/unity` responds (first open imports the whole Library — allow 5-15 minutes; poll, don't spin).

---

### Task 1: Data records + analyzer overview stats

**Files:**
- Create: `unity/Packages/com.bun3.unity.diagnostics/Editor/ProfilerFrameSample.cs`
- Create: `unity/Packages/com.bun3.unity.diagnostics/Editor/ProfilerDumpAnalyzer.cs`
- Test: `unity/Packages/com.bun3.unity.diagnostics/Tests/Editor/ProfilerDumpAnalyzerTests.cs`

**Interfaces:**
- Produces: `[Serializable] ProfilerFrameStat { int frameIndex; float cpuMs, gpuMs, renderThreadMs; long gcAllocBytes; }`, `[Serializable] MarkerSample { string name; float selfMs; int calls; long gcAllocBytes; }`, `ProfilerFrameSample { ProfilerFrameStat stat; List<MarkerSample> markers; }` (not serialized, analyzer input);
  `[Serializable] SpikeFrame`, `[Serializable] ProfilerMarkerStat`, `[Serializable] ProfilerDumpAnalysis`;
  `public static ProfilerDumpAnalysis ProfilerDumpAnalyzer.Analyze(IReadOnlyList<ProfilerFrameSample> frames, float budgetMs)` — this task fills only the overview fields; `SelectSpikes`/`AggregateMarkers` are stubbed (empty lists), Task 2 fills them.
- Median = lower median (`sorted[(n-1)/2]`); p95 = `sorted[min(n-1, ceil(n*0.95)-1)]`.

- [ ] **Step 1: Write the failing tests**

`Tests/Editor/ProfilerDumpAnalyzerTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Bun3.Unity.Diagnostics.Editor.Tests
{
    public class ProfilerDumpAnalyzerTests
    {
        internal static ProfilerFrameSample F(int index, float cpuMs, long gc = 0, float renderMs = 0, params MarkerSample[] markers)
        {
            var f = new ProfilerFrameSample();
            f.stat.frameIndex = index;
            f.stat.cpuMs = cpuMs;
            f.stat.gcAllocBytes = gc;
            f.stat.renderThreadMs = renderMs;
            f.markers.AddRange(markers);
            return f;
        }

        internal static MarkerSample M(string name, float selfMs, int calls = 1, long gc = 0) =>
            new MarkerSample { name = name, selfMs = selfMs, calls = calls, gcAllocBytes = gc };

        [Test]
        public void Analyze_EmptyInputYieldsEmptyAnalysis()
        {
            var a = ProfilerDumpAnalyzer.Analyze(new List<ProfilerFrameSample>(), 33.3f);

            Assert.AreEqual(0, a.frameCount);
            Assert.AreEqual(0f, a.worstCpuMs);
            Assert.IsEmpty(a.spikes);
        }

        [Test]
        public void Analyze_ComputesCpuOverviewStats()
        {
            var frames = Enumerable.Range(1, 20).Select(i => F(i, i)).ToList(); // 1..20 ms

            var a = ProfilerDumpAnalyzer.Analyze(frames, 33.3f);

            Assert.AreEqual(20, a.frameCount);
            Assert.AreEqual(10f, a.medianCpuMs);      // lower median of 1..20
            Assert.AreEqual(10.5f, a.averageCpuMs, 0.001f);
            Assert.AreEqual(19f, a.p95CpuMs);          // ceil(20*0.95)-1 = index 18 -> value 19
            Assert.AreEqual(20f, a.worstCpuMs);
        }

        [Test]
        public void Analyze_ComputesGcAndRenderThreadStats()
        {
            var frames = new List<ProfilerFrameSample>
            {
                F(0, 10f, gc: 100, renderMs: 2f),
                F(1, 12f, gc: 300, renderMs: 4f),
            };

            var a = ProfilerDumpAnalyzer.Analyze(frames, 33.3f);

            Assert.AreEqual(400L, a.totalGcAllocBytes);
            Assert.AreEqual(300L, a.worstFrameGcAllocBytes);
            Assert.AreEqual(3f, a.averageRenderThreadMs, 0.001f);
        }
    }
}
```

- [ ] **Step 2: Recompile — verify compile failure (types not defined)**

Run: `unity command recompile --project E:/Projects/orca/workspace/bun3-kit/profiler-dump/unity` → poll `recompile_status`. Expected: compile errors.

- [ ] **Step 3: Implement the data records**

`Editor/ProfilerFrameSample.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Bun3.Unity.Diagnostics
{
    /// <summary>Per-frame stats captured from the profiler buffer.</summary>
    [Serializable]
    public sealed class ProfilerFrameStat
    {
        /// <summary>Profiler frame index.</summary>
        public int frameIndex;

        /// <summary>Main-thread CPU frame time in milliseconds.</summary>
        public float cpuMs;

        /// <summary>GPU frame time in milliseconds; 0 when unavailable.</summary>
        public float gpuMs;

        /// <summary>Render-thread total time in milliseconds; 0 when the thread was not found.</summary>
        public float renderThreadMs;

        /// <summary>GC allocation in bytes during the frame (main thread).</summary>
        public long gcAllocBytes;
    }

    /// <summary>One marker's totals within a single frame, merged by name (main thread).</summary>
    [Serializable]
    public sealed class MarkerSample
    {
        /// <summary>Profiler marker name.</summary>
        public string name = "";

        /// <summary>Self time in milliseconds within the frame.</summary>
        public float selfMs;

        /// <summary>Call count within the frame.</summary>
        public int calls;

        /// <summary>GC allocation in bytes within the frame.</summary>
        public long gcAllocBytes;
    }

    /// <summary>One captured frame: frame-level stats plus its main-thread marker samples.</summary>
    public sealed class ProfilerFrameSample
    {
        /// <summary>Frame-level stats.</summary>
        public ProfilerFrameStat stat = new ProfilerFrameStat();

        /// <summary>Marker samples merged by name within the frame.</summary>
        public List<MarkerSample> markers = new List<MarkerSample>();
    }
}
```

- [ ] **Step 4: Implement the analyzer (overview only, spikes/markers stubbed)**

`Editor/ProfilerDumpAnalyzer.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Bun3.Unity.Diagnostics
{
    /// <summary>A profiler frame flagged as a spike, with its heaviest markers.</summary>
    [Serializable]
    public sealed class SpikeFrame
    {
        /// <summary>Profiler frame index.</summary>
        public int frameIndex;

        /// <summary>Main-thread CPU time of the frame in milliseconds.</summary>
        public float cpuMs;

        /// <summary>Heaviest markers of the frame by self time.</summary>
        public List<MarkerSample> topMarkers = new List<MarkerSample>();
    }

    /// <summary>Marker totals aggregated across every captured frame.</summary>
    [Serializable]
    public sealed class ProfilerMarkerStat
    {
        /// <summary>Profiler marker name.</summary>
        public string name = "";

        /// <summary>Self time summed across all frames, in milliseconds.</summary>
        public float totalSelfMs;

        /// <summary>Average self time per captured frame, in milliseconds.</summary>
        public float avgSelfMsPerFrame;

        /// <summary>Largest single-frame self time, in milliseconds.</summary>
        public float maxSelfMs;

        /// <summary>Call count summed across all frames.</summary>
        public int callCount;

        /// <summary>GC allocation summed across all frames, in bytes.</summary>
        public long gcAllocBytes;
    }

    /// <summary>Aggregated analysis of one profiler capture.</summary>
    [Serializable]
    public sealed class ProfilerDumpAnalysis
    {
        /// <summary>Number of analyzed frames.</summary>
        public int frameCount;

        /// <summary>CPU frame budget used for spike reporting, in milliseconds.</summary>
        public float budgetMs;

        /// <summary>Lower-median main-thread CPU time, in milliseconds.</summary>
        public float medianCpuMs;

        /// <summary>Average main-thread CPU time, in milliseconds.</summary>
        public float averageCpuMs;

        /// <summary>95th-percentile main-thread CPU time, in milliseconds.</summary>
        public float p95CpuMs;

        /// <summary>Worst main-thread CPU time, in milliseconds.</summary>
        public float worstCpuMs;

        /// <summary>Average GPU time, in milliseconds; 0 when unavailable.</summary>
        public float averageGpuMs;

        /// <summary>Average render-thread total time, in milliseconds.</summary>
        public float averageRenderThreadMs;

        /// <summary>GC allocation summed across all frames, in bytes.</summary>
        public long totalGcAllocBytes;

        /// <summary>Largest single-frame GC allocation, in bytes.</summary>
        public long worstFrameGcAllocBytes;

        /// <summary>Spike frames: worst frames plus budget-exceeders, capped.</summary>
        public List<SpikeFrame> spikes = new List<SpikeFrame>();

        /// <summary>Markers with the highest summed self time.</summary>
        public List<ProfilerMarkerStat> topMarkersBySelfTime = new List<ProfilerMarkerStat>();

        /// <summary>Markers with the highest summed GC allocation.</summary>
        public List<ProfilerMarkerStat> topMarkersByGcAlloc = new List<ProfilerMarkerStat>();
    }

    /// <summary>Pure analysis over captured profiler frames; no editor state.</summary>
    public static class ProfilerDumpAnalyzer
    {
        /// <summary>Markers reported in each aggregation list.</summary>
        public const int TopMarkerCount = 20;

        /// <summary>Markers reported per spike frame.</summary>
        public const int SpikeTopMarkerCount = 10;

        /// <summary>Worst frames always included as spikes.</summary>
        public const int WorstFrameCount = 5;

        /// <summary>Maximum spike frames reported.</summary>
        public const int MaxSpikeCount = 10;

        /// <summary>Runs the full analysis over the captured frames.</summary>
        public static ProfilerDumpAnalysis Analyze(IReadOnlyList<ProfilerFrameSample> frames, float budgetMs)
        {
            var analysis = new ProfilerDumpAnalysis { frameCount = frames.Count, budgetMs = budgetMs };
            if (frames.Count == 0)
                return analysis;

            var cpu = frames.Select(f => f.stat.cpuMs).OrderBy(v => v).ToArray();
            analysis.medianCpuMs = cpu[(cpu.Length - 1) / 2];
            analysis.averageCpuMs = cpu.Average();
            analysis.p95CpuMs = cpu[Math.Min(cpu.Length - 1, (int)Math.Ceiling(cpu.Length * 0.95) - 1)];
            analysis.worstCpuMs = cpu[cpu.Length - 1];
            analysis.averageGpuMs = frames.Average(f => f.stat.gpuMs);
            analysis.averageRenderThreadMs = frames.Average(f => f.stat.renderThreadMs);
            analysis.totalGcAllocBytes = frames.Sum(f => f.stat.gcAllocBytes);
            analysis.worstFrameGcAllocBytes = frames.Max(f => f.stat.gcAllocBytes);
            analysis.spikes = SelectSpikes(frames, budgetMs);
            AggregateMarkers(frames, analysis);
            return analysis;
        }

        static List<SpikeFrame> SelectSpikes(IReadOnlyList<ProfilerFrameSample> frames, float budgetMs)
        {
            return new List<SpikeFrame>();
        }

        static void AggregateMarkers(IReadOnlyList<ProfilerFrameSample> frames, ProfilerDumpAnalysis analysis)
        {
        }
    }
}
```

- [ ] **Step 5: Recompile and run tests**

Run: recompile → `unity command run_tests --filter ProfilerDumpAnalyzerTests --project <worktree>/unity`.
Expected: 3 tests PASS (plus the existing 17 untouched).

- [ ] **Step 6: Commit**

```bash
git add unity/Packages/com.bun3.unity.diagnostics
git commit -m "✨ Profiler frame records and overview analysis" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Analyzer — spikes + marker aggregation

**Files:**
- Modify: `unity/Packages/com.bun3.unity.diagnostics/Editor/ProfilerDumpAnalyzer.cs` (replace the two stub bodies)
- Test: `unity/Packages/com.bun3.unity.diagnostics/Tests/Editor/ProfilerDumpAnalyzerTests.cs` (append)

**Interfaces:**
- Spike selection: order frames by cpuMs desc; keep rank < `WorstFrameCount` OR cpuMs > budgetMs; take `MaxSpikeCount`; each spike carries top `SpikeTopMarkerCount` markers by selfMs.
- Marker aggregation: sum selfMs/calls/gcAllocBytes by name across frames, `avgSelfMsPerFrame = totalSelfMs / frameCount`, `maxSelfMs` = per-frame max; lists ordered desc (self-time list by totalSelfMs, GC list by gcAllocBytes filtering zero), tie-break name ordinal, top 20.

- [ ] **Step 1: Append the failing tests**

```csharp
        [Test]
        public void Analyze_SpikesIncludeWorstFivePlusBudgetExceeders()
        {
            var frames = new List<ProfilerFrameSample>();
            for (int i = 0; i < 8; i++)
                frames.Add(F(i, 10f)); // calm frames
            frames.Add(F(100, 40f));   // over budget
            frames.Add(F(101, 50f));   // over budget

            var a = ProfilerDumpAnalyzer.Analyze(frames, 33.3f);

            Assert.AreEqual(5, a.spikes.Count); // worst 5 (two exceeders are inside the worst five)
            Assert.AreEqual(101, a.spikes[0].frameIndex);
            Assert.AreEqual(100, a.spikes[1].frameIndex);
            Assert.AreEqual(10f, a.spikes[2].cpuMs);
        }

        [Test]
        public void Analyze_SpikeListIsCappedAtTen()
        {
            var frames = Enumerable.Range(0, 30).Select(i => F(i, 100f + i)).ToList(); // all over budget

            var a = ProfilerDumpAnalyzer.Analyze(frames, 33.3f);

            Assert.AreEqual(10, a.spikes.Count);
            Assert.AreEqual(129f, a.spikes[0].cpuMs);
        }

        [Test]
        public void Analyze_SpikeCarriesTopMarkersBySelfTime()
        {
            var spike = F(7, 60f, markers: new[]
            {
                M("Cheap", 1f), M("Heavy", 30f), M("Mid", 10f),
            });
            var frames = new List<ProfilerFrameSample> { spike, F(8, 5f) };

            var a = ProfilerDumpAnalyzer.Analyze(frames, 33.3f);

            Assert.AreEqual(7, a.spikes[0].frameIndex);
            Assert.AreEqual("Heavy", a.spikes[0].topMarkers[0].name);
            Assert.AreEqual("Mid", a.spikes[0].topMarkers[1].name);
        }

        [Test]
        public void Analyze_AggregatesMarkersAcrossFrames()
        {
            var frames = new List<ProfilerFrameSample>
            {
                F(0, 10f, markers: new[] { M("Update", 4f, calls: 2, gc: 100) }),
                F(1, 12f, markers: new[] { M("Update", 6f, calls: 3, gc: 50), M("Render", 8f) }),
            };

            var a = ProfilerDumpAnalyzer.Analyze(frames, 33.3f);

            var update = a.topMarkersBySelfTime.First(m => m.name == "Update");
            Assert.AreEqual(10f, update.totalSelfMs, 0.001f);
            Assert.AreEqual(5f, update.avgSelfMsPerFrame, 0.001f);
            Assert.AreEqual(6f, update.maxSelfMs, 0.001f);
            Assert.AreEqual(5, update.callCount);
            Assert.AreEqual(150L, update.gcAllocBytes);
        }

        [Test]
        public void Analyze_GcListFiltersZeroAllocMarkers()
        {
            var frames = new List<ProfilerFrameSample>
            {
                F(0, 10f, markers: new[] { M("Alloc", 1f, gc: 500), M("Clean", 9f) }),
            };

            var a = ProfilerDumpAnalyzer.Analyze(frames, 33.3f);

            Assert.AreEqual(1, a.topMarkersByGcAlloc.Count);
            Assert.AreEqual("Alloc", a.topMarkersByGcAlloc[0].name);
            Assert.AreEqual("Clean", a.topMarkersBySelfTime[0].name);
        }
```

- [ ] **Step 2: Recompile and run — the 5 new tests FAIL against the stubs** (`Analyze_SpikeListIsCappedAtTen` etc.), earlier 3 pass.

- [ ] **Step 3: Implement the detectors**

Replace the two stub bodies:

```csharp
        static List<SpikeFrame> SelectSpikes(IReadOnlyList<ProfilerFrameSample> frames, float budgetMs)
        {
            return frames
                .OrderByDescending(f => f.stat.cpuMs)
                .Where((f, rank) => rank < WorstFrameCount || f.stat.cpuMs > budgetMs)
                .Take(MaxSpikeCount)
                .Select(f => new SpikeFrame
                {
                    frameIndex = f.stat.frameIndex,
                    cpuMs = f.stat.cpuMs,
                    topMarkers = f.markers
                        .OrderByDescending(m => m.selfMs)
                        .Take(SpikeTopMarkerCount)
                        .ToList(),
                })
                .ToList();
        }

        static void AggregateMarkers(IReadOnlyList<ProfilerFrameSample> frames, ProfilerDumpAnalysis analysis)
        {
            var byName = new Dictionary<string, ProfilerMarkerStat>();
            foreach (var frame in frames)
            {
                foreach (var m in frame.markers)
                {
                    if (string.IsNullOrEmpty(m.name))
                        continue;
                    if (!byName.TryGetValue(m.name, out var stat))
                        byName[m.name] = stat = new ProfilerMarkerStat { name = m.name };
                    stat.totalSelfMs += m.selfMs;
                    stat.maxSelfMs = Math.Max(stat.maxSelfMs, m.selfMs);
                    stat.callCount += m.calls;
                    stat.gcAllocBytes += m.gcAllocBytes;
                }
            }

            foreach (var stat in byName.Values)
                stat.avgSelfMsPerFrame = stat.totalSelfMs / frames.Count;

            analysis.topMarkersBySelfTime = byName.Values
                .OrderByDescending(s => s.totalSelfMs).ThenBy(s => s.name, StringComparer.Ordinal)
                .Take(TopMarkerCount).ToList();
            analysis.topMarkersByGcAlloc = byName.Values
                .Where(s => s.gcAllocBytes > 0)
                .OrderByDescending(s => s.gcAllocBytes).ThenBy(s => s.name, StringComparer.Ordinal)
                .Take(TopMarkerCount).ToList();
        }
```

- [ ] **Step 4: Recompile and run tests** — all 8 analyzer tests PASS.

- [ ] **Step 5: Commit**

```bash
git add unity/Packages/com.bun3.unity.diagnostics
git commit -m "✨ Profiler spike selection and marker aggregation" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Report writer

**Files:**
- Create: `unity/Packages/com.bun3.unity.diagnostics/Editor/ProfilerDumpReportWriter.cs`
- Test: `unity/Packages/com.bun3.unity.diagnostics/Tests/Editor/ProfilerDumpReportWriterTests.cs`

**Interfaces:**
- Produces (internal): `ProfilerDumpReportWriter.ToMarkdown(string timestamp, string source, IReadOnlyList<ProfilerFrameStat> frames, ProfilerDumpAnalysis analysis)` and `ToJson(string timestamp, string source, List<ProfilerFrameStat> frames, ProfilerDumpAnalysis analysis)`; `[Serializable] internal ProfilerDumpDocument { timestamp, source, frames, analysis }`. Markdown does NOT list per-frame rows (they live in JSON); byte values render via a `FormatBytes` helper (B/KB/MB, one decimal).

- [ ] **Step 1: Write the failing tests**

`Tests/Editor/ProfilerDumpReportWriterTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Diagnostics.Editor.Tests
{
    public class ProfilerDumpReportWriterTests
    {
        static (List<ProfilerFrameStat> frames, ProfilerDumpAnalysis analysis) Sample()
        {
            var samples = new List<ProfilerFrameSample>
            {
                ProfilerDumpAnalyzerTests.F(0, 40f, gc: 2048, markers: new[] { ProfilerDumpAnalyzerTests.M("Heavy", 30f, gc: 2048) }),
                ProfilerDumpAnalyzerTests.F(1, 10f),
            };
            var analysis = ProfilerDumpAnalyzer.Analyze(samples, 33.3f);
            var frames = new List<ProfilerFrameStat> { samples[0].stat, samples[1].stat };
            return (frames, analysis);
        }

        [Test]
        public void Markdown_ContainsOverviewSpikesAndMarkerSections()
        {
            var (frames, analysis) = Sample();

            var md = ProfilerDumpReportWriter.ToMarkdown("2026-08-30 12:00:00", "live", frames, analysis);

            StringAssert.Contains("# Profiler Dump 2026-08-30 12:00:00", md);
            StringAssert.Contains("Source: live", md);
            StringAssert.Contains("## Spike frames", md);
            StringAssert.Contains("[frame 0] 40.0 ms", md);
            StringAssert.Contains("Heavy: 30.0 ms self", md);
            StringAssert.Contains("## Top markers by self time", md);
            StringAssert.Contains("## Top markers by GC alloc", md);
            StringAssert.Contains("2.0 KB", md);
        }

        [Test]
        public void Json_RoundTripsThroughJsonUtility()
        {
            var (frames, analysis) = Sample();

            var json = ProfilerDumpReportWriter.ToJson("t", "live", frames, analysis);
            var doc = JsonUtility.FromJson<ProfilerDumpDocument>(json);

            Assert.AreEqual("t", doc.timestamp);
            Assert.AreEqual("live", doc.source);
            Assert.AreEqual(2, doc.frames.Count);
            Assert.AreEqual(40f, doc.frames[0].cpuMs);
            Assert.AreEqual(2, doc.analysis.frameCount);
            Assert.AreEqual(2, doc.analysis.spikes.Count); // both frames sit inside the worst-5 window
        }
    }
}
```

(Make the `F`/`M` helpers in `ProfilerDumpAnalyzerTests` `internal static` as written in Task 1 so this file can reuse them.)

- [ ] **Step 2: Recompile — verify compile failure.**

- [ ] **Step 3: Implement the writer**

`Editor/ProfilerDumpReportWriter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Bun3.Unity.Diagnostics
{
    // JSON document shape: { timestamp, source, frames[], analysis } — per-frame stats live here;
    // the Markdown report carries only the analysis sections.
    [Serializable]
    internal sealed class ProfilerDumpDocument
    {
        public string timestamp = "";
        public string source = "";
        public List<ProfilerFrameStat> frames = new List<ProfilerFrameStat>();
        public ProfilerDumpAnalysis analysis = new ProfilerDumpAnalysis();
    }

    internal static class ProfilerDumpReportWriter
    {
        internal static string ToJson(string timestamp, string source, List<ProfilerFrameStat> frames, ProfilerDumpAnalysis analysis) =>
            JsonUtility.ToJson(
                new ProfilerDumpDocument { timestamp = timestamp, source = source, frames = frames, analysis = analysis },
                true);

        internal static string ToMarkdown(string timestamp, string source, IReadOnlyList<ProfilerFrameStat> frames, ProfilerDumpAnalysis analysis)
        {
            var sb = new StringBuilder(1 << 18);
            sb.AppendLine($"# Profiler Dump {timestamp}");
            sb.AppendLine();
            sb.AppendLine($"- Source: {source}");
            sb.AppendLine($"- Frames: {analysis.frameCount}");
            sb.AppendLine($"- CPU ms: median {analysis.medianCpuMs:F1} / avg {analysis.averageCpuMs:F1} / p95 {analysis.p95CpuMs:F1} / worst {analysis.worstCpuMs:F1} (budget {analysis.budgetMs:F1})");
            sb.AppendLine($"- GPU avg: {analysis.averageGpuMs:F1} ms, render thread avg: {analysis.averageRenderThreadMs:F1} ms");
            sb.AppendLine($"- GC alloc: total {FormatBytes(analysis.totalGcAllocBytes)}, worst frame {FormatBytes(analysis.worstFrameGcAllocBytes)}");

            sb.AppendLine();
            sb.AppendLine("## Spike frames");
            if (analysis.spikes.Count == 0)
                sb.AppendLine("(none)");
            foreach (var s in analysis.spikes)
            {
                sb.AppendLine($"- [frame {s.frameIndex}] {s.cpuMs:F1} ms");
                foreach (var m in s.topMarkers)
                    sb.AppendLine($"  - {m.name}: {m.selfMs:F1} ms self, {m.calls} calls, {FormatBytes(m.gcAllocBytes)}");
            }

            sb.AppendLine();
            sb.AppendLine("## Top markers by self time");
            if (analysis.topMarkersBySelfTime.Count == 0)
                sb.AppendLine("(none)");
            foreach (var m in analysis.topMarkersBySelfTime)
                sb.AppendLine($"- {m.name}: total {m.totalSelfMs:F1} ms, avg {m.avgSelfMsPerFrame:F2} ms/frame, max {m.maxSelfMs:F1} ms, {m.callCount} calls");

            sb.AppendLine();
            sb.AppendLine("## Top markers by GC alloc");
            if (analysis.topMarkersByGcAlloc.Count == 0)
                sb.AppendLine("(none)");
            foreach (var m in analysis.topMarkersByGcAlloc)
                sb.AppendLine($"- {m.name}: {FormatBytes(m.gcAllocBytes)} total, {m.callCount} calls");

            return sb.ToString();
        }

        static string FormatBytes(long bytes)
        {
            if (bytes >= 1 << 20)
                return $"{bytes / (float)(1 << 20):F1} MB";
            if (bytes >= 1 << 10)
                return $"{bytes / (float)(1 << 10):F1} KB";
            return $"{bytes} B";
        }
    }
}
```

- [ ] **Step 4: Recompile and run tests** — `run_tests --filter ProfilerDumpReportWriterTests`, 2 PASS.

- [ ] **Step 5: Commit**

```bash
git add unity/Packages/com.bun3.unity.diagnostics
git commit -m "✨ Profiler dump report writer" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: ProfilerCapture buffer reader

**Files:**
- Create: `unity/Packages/com.bun3.unity.diagnostics/Editor/ProfilerCapture.cs`

**Interfaces:**
- Produces (internal): `static int FrameCount`, `static bool HasFrames`, `static List<ProfilerFrameSample> ReadAll()`. Public-API-only (compile-time verified) — no unit test in this task; a live integration test lands in Task 5. Gate: clean compile + existing suite green.

- [ ] **Step 1: Implement**

`Editor/ProfilerCapture.cs`:

```csharp
using System.Collections.Generic;
using UnityEditor.Profiling;
using UnityEditorInternal;

namespace Bun3.Unity.Diagnostics
{
    // Reads the ProfilerDriver frame buffer into plain data. Main-thread markers are flattened by
    // merged sample name; the render thread contributes only its per-frame total time. All reads
    // are synchronous — the buffer holds already-collected frames.
    internal static class ProfilerCapture
    {
        const int MainThreadIndex = 0;
        const int MaxThreadProbe = 32;
        const string RenderThreadName = "Render Thread";

        internal static int FrameCount =>
            ProfilerDriver.firstFrameIndex < 0 ? 0 : ProfilerDriver.lastFrameIndex - ProfilerDriver.firstFrameIndex + 1;

        internal static bool HasFrames => FrameCount > 0;

        internal static List<ProfilerFrameSample> ReadAll()
        {
            var samples = new List<ProfilerFrameSample>();
            var scratch = new List<int>();
            int renderThreadIndex = -1; // resolved lazily on the first frame that finds it

            for (int frame = ProfilerDriver.firstFrameIndex; frame <= ProfilerDriver.lastFrameIndex; frame++)
            {
                using (var view = OpenView(frame, MainThreadIndex))
                {
                    if (view == null || !view.valid)
                        continue;

                    var sample = new ProfilerFrameSample();
                    sample.stat.frameIndex = frame;
                    sample.stat.cpuMs = view.frameTimeMs;
                    sample.stat.gpuMs = view.frameGpuTimeMs;
                    int root = view.GetRootItemID();
                    sample.stat.gcAllocBytes = (long)view.GetItemColumnDataAsFloat(root, HierarchyFrameDataView.columnGcMemory);
                    CollectMarkers(view, root, scratch, sample.markers);
                    sample.stat.renderThreadMs = ReadRenderThreadMs(frame, ref renderThreadIndex);
                    samples.Add(sample);
                }
            }

            return samples;
        }

        static HierarchyFrameDataView OpenView(int frame, int threadIndex) =>
            ProfilerDriver.GetHierarchyFrameDataView(
                frame, threadIndex, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                HierarchyFrameDataView.columnSelfTime, false);

        static void CollectMarkers(HierarchyFrameDataView view, int root, List<int> scratch, List<MarkerSample> markers)
        {
            var byName = new Dictionary<string, MarkerSample>();
            var stack = new Stack<int>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                int id = stack.Pop();
                scratch.Clear();
                view.GetItemChildren(id, scratch);
                foreach (int child in scratch)
                {
                    stack.Push(child);
                    string name = view.GetItemName(child);
                    if (string.IsNullOrEmpty(name))
                        continue;
                    if (!byName.TryGetValue(name, out var m))
                        byName[name] = m = new MarkerSample { name = name };
                    m.selfMs += view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnSelfTime);
                    m.calls += (int)view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnCalls);
                    m.gcAllocBytes += (long)view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnGcMemory);
                }
            }

            markers.AddRange(byName.Values);
        }

        static float ReadRenderThreadMs(int frame, ref int renderThreadIndex)
        {
            if (renderThreadIndex >= 0)
            {
                using (var view = OpenView(frame, renderThreadIndex))
                    return view != null && view.valid
                        ? view.GetItemColumnDataAsFloat(view.GetRootItemID(), HierarchyFrameDataView.columnTotalTime)
                        : 0f;
            }

            for (int ti = 1; ti < MaxThreadProbe; ti++)
            {
                using (var view = OpenView(frame, ti))
                {
                    if (view == null || !view.valid)
                        return 0f;
                    if (view.threadName != RenderThreadName)
                        continue;
                    renderThreadIndex = ti;
                    return view.GetItemColumnDataAsFloat(view.GetRootItemID(), HierarchyFrameDataView.columnTotalTime);
                }
            }

            return 0f;
        }
    }
}
```

- [ ] **Step 2: Recompile clean (0 warnings) and run the full package suite** — all prior tests still green.

- [ ] **Step 3: Commit**

```bash
git add unity/Packages/com.bun3.unity.diagnostics
git commit -m "✨ Profiler buffer reader over public frame-data APIs" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: ProfilerDumper + menu + recording integration test

**Files:**
- Create: `unity/Packages/com.bun3.unity.diagnostics/Editor/ProfilerDumper.cs`
- Test: `unity/Packages/com.bun3.unity.diagnostics/Tests/Editor/ProfilerRecordingTests.cs`

**Interfaces:**
- Produces: `[Serializable] ProfilerDumpResult { bool success; string error, source; int frameCount; float worstCpuMs; string topMarker; string markdownPath, jsonPath; }`;
  `public static class ProfilerDumper { const float DefaultBudgetMs = 33.3f; static bool IsRecording; static int BufferedFrameCount; static ProfilerDumpResult LastResult; static ProfilerDumpResult Dump(float budgetMs = DefaultBudgetMs); static ProfilerDumpResult LoadAndDump(string path, float budgetMs = DefaultBudgetMs); static Task<int> RecordAsync(int frameCount); }`; menu `Tools/Bun3/Profiler Dump`.
- `Dump` is synchronous and never throws (failures come back as a failed result). `RecordAsync` clears the buffer, enables the profiler, completes when `frameCount` frames exist or after `1000` consecutive stalled editor updates, then disables the profiler.

- [ ] **Step 1: Write the integration test (RED: compile failure)**

`Tests/Editor/ProfilerRecordingTests.cs`:

```csharp
using System.Collections;
using NUnit.Framework;
using UnityEditorInternal;
using UnityEngine.TestTools;

namespace Bun3.Unity.Diagnostics.Editor.Tests
{
    public class ProfilerRecordingTests
    {
        [UnityTest]
        public IEnumerator RecordAndRead_CapturesEditorFrames()
        {
            bool prevEnabled = ProfilerDriver.enabled;
            bool prevProfileEditor = ProfilerDriver.profileEditor;
            ProfilerDriver.profileEditor = true;
            try
            {
                var task = ProfilerDumper.RecordAsync(3);
                int guard = 0;
                while (!task.IsCompleted && guard++ < 3000)
                {
                    InternalEditorUtility.RepaintAllViews();
                    yield return null;
                }

                Assert.IsTrue(task.IsCompleted, "recording did not finish");
                Assert.Greater(task.Result, 0, "no frames captured");
                var frames = ProfilerCapture.ReadAll();
                Assert.IsNotEmpty(frames);
                Assert.IsNotEmpty(frames[0].markers, "main-thread markers empty");
            }
            finally
            {
                ProfilerDriver.enabled = prevEnabled;
                ProfilerDriver.profileEditor = prevProfileEditor;
            }
        }
    }
}
```

If this test proves flaky in this environment (editor produces no frames while unfocused despite RepaintAllViews), report it in the task report — do not silently delete it; the controller decides whether it stays.

- [ ] **Step 2: Recompile — compile failure (ProfilerDumper not defined).**

- [ ] **Step 3: Implement the dumper**

`Editor/ProfilerDumper.cs`:

```csharp
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
```

- [ ] **Step 4: Recompile clean, run the recording test and the full suite**

`run_tests --filter ProfilerRecordingTests` (a `[UnityTest]` may need `run_tests` without class filter if the runner skips it — then run the full suite and check its name in the results). Then the full package suite: everything green.

- [ ] **Step 5: Menu smoke check (optional)** — `unity command eval` with `UnityEditor.Menu.MenuItemExists("Tools/Bun3/Profiler Dump")` if eval accepts it; otherwise skip (compile success + attribute is enough, per the frame-debugger precedent).

- [ ] **Step 6: Commit**

```bash
git add unity/Packages/com.bun3.unity.diagnostics
git commit -m "✨ Profiler dumper with bounded recording and menu" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: CLI commands + version bump + README

**Files:**
- Create: `unity/Packages/com.bun3.unity.diagnostics/Editor/Cli/ProfilerCliCommands.cs`
- Modify: `unity/Packages/com.bun3.unity.diagnostics/package.json` (version + description)
- Modify: `unity/Packages/com.bun3.unity.diagnostics/README.md` (profiler section)

**Interfaces:**
- Consumes: `ProfilerDumper` public surface (Task 5), `CliCommand`/`CliArg` from `Unity.Pipeline` (already referenced by the Cli asmdef — no asmdef change needed).
- Produces: CLI commands `profiler_record`, `profiler_record_status`, `profiler_dump`.

- [ ] **Step 1: Implement the commands**

`Editor/Cli/ProfilerCliCommands.cs`:

```csharp
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
```

- [ ] **Step 2: Bump package.json**

Change `"version": "0.1.0"` → `"0.2.0"` and extend the description's feature list with: `Also dumps Profiler captures (frame overview, spike frames, per-marker self-time and GC-allocation aggregation, bounded recording helper).` (append to the existing sentence flow, keep English).

- [ ] **Step 3: Update README.md**

Append after the Frame Debugger usage section:

```markdown
## Profiler dump

1. Record: Profiler window, `ProfilerDumper.RecordAsync(n)`, or CLI `profiler_record --frames N`.
   Saved captures load via `ProfilerDumper.LoadAndDump(path)` / `profiler_dump --file <path>`.
2. Dump: menu `Tools/Bun3/Profiler Dump`, `ProfilerDumper.Dump()`, or CLI `profiler_dump`.
3. Reports land in `<project>/ProfilerDump/ProfilerDump_<timestamp>.md` and `.json`:
   frame overview (median/p95/worst CPU, GC, render thread), spike frames with their heaviest
   markers, and per-marker self-time / GC-allocation totals.
```

- [ ] **Step 4: Recompile clean, verify registration and run the suite**

`unity command profiler_record_status --project <worktree>/unity` → expect `running=false`, `bufferedFrames` numeric. Then the full package suite — all green.

- [ ] **Step 5: Commit**

```bash
git add unity/Packages/com.bun3.unity.diagnostics
git commit -m "✨ Profiler CLI commands, package 0.2.0, README" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Out of scope / follow-ups

- Full record-to-dump orchestration in one CLI command (spec: layered on later if needed).
- Render-thread marker tree (v0 reports the thread's total time only).
- Memory Profiler-style snapshots — different tool domain.
- Manual E2E: play a real scene, `profiler_record` → `profiler_dump`, read the report; and one `LoadAndDump` against a device capture file.
