# Unity Frame Debugger Dump Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** New Editor-only package `com.bun3.unity.diagnostics` that dumps Frame Debugger captures to Markdown + JSON with an automated batching-analysis report, plus optional unity-cli commands.

**Architecture:** Reflection layer (`FrameDebuggerReflection`, name-based + failure-tolerant) feeds an `EditorApplication.update` traversal state machine (`FrameDebuggerDumper`) that collects plain `FrameEvent` records. Pure static `FrameDumpAnalyzer` produces aggregations/interleave detection; `FrameDumpReportWriter` renders `.md`/`.json`. A separate `Editor/Cli` assembly (compiled only when `com.unity.pipeline` is present via versionDefines + defineConstraints) exposes `[CliCommand]`s.

**Tech Stack:** Unity Editor API + reflection, NUnit EditMode tests, `JsonUtility`, zero package dependencies (no UniTask — plain `Task` + `TaskCompletionSource`).

**Spec:** `docs/superpowers/specs/2026-08-29-unity-frame-debugger-dump-design.md`

## Global Constraints

- Package min Unity: `"unity": "2022.3"` — no dependency on any other package (spec: works on 2022.3 games via git URL).
- English only in all code files (comments, XML docs, strings); no legacy-project mentions in code.
- All public members carry English XML docs; build must produce 0 warnings.
- C#9, block namespaces; namespace is `Bun3.Unity.Diagnostics` (per spec — not `.Editor` suffixed).
- Folder = namespace; keep `Editor/` flat.
- Comments only for constraints the code can't show.
- Commit style: gitmoji title + trailer, via double `-m` flags: `git commit -m "<title>" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"`.
- Verification uses the unity-cli against the running editor for the `unity/` project: `unity command recompile` → poll `unity command recompile_status` → `unity command run_tests --filter <TestClass>` (EditMode). If no editor is running, open the `unity/` project first (unity-cli skill). If `run_tests` needs different args, check `unity command list_tests` output and adapt.
- After each recompile, Unity generates `.meta` files for new files/folders under `unity/Packages/com.bun3.unity.diagnostics/` — always `git add` the whole package folder so `.meta` files are committed alongside.

## File Structure

```
unity/Packages/com.bun3.unity.diagnostics/
  package.json
  README.md
  Editor/
    Bun3.Unity.Diagnostics.Editor.asmdef
    AssemblyInfo.cs                  (InternalsVisibleTo tests)
    FrameEvent.cs                    (public data record + pure FromFields mapping)
    FrameDumpAnalyzer.cs             (analysis result types + pure analyzer)
    FrameDebuggerReflection.cs       (internal: all reflection over FrameDebuggerUtility)
    FrameDumpReportWriter.cs         (internal: md/json rendering + FrameDumpDocument)
    FrameDebuggerDumper.cs           (public API + menu + traversal state machine + FrameDumpResult)
    Cli/
      Bun3.Unity.Diagnostics.Editor.Cli.asmdef   (defineConstraints: BUN3_UNITY_PIPELINE)
      FrameDebuggerCliCommands.cs
  Tests/Editor/
    Bun3.Unity.Diagnostics.Editor.Tests.asmdef
    FrameEventTests.cs
    FrameDumpAnalyzerTests.cs
    FrameDumpReportWriterTests.cs
    FrameDebuggerReflectionTests.cs
```

---

### Task 1: Package scaffold

**Files:**
- Create: `unity/Packages/com.bun3.unity.diagnostics/package.json`
- Create: `unity/Packages/com.bun3.unity.diagnostics/README.md`
- Create: `unity/Packages/com.bun3.unity.diagnostics/Editor/Bun3.Unity.Diagnostics.Editor.asmdef`
- Create: `unity/Packages/com.bun3.unity.diagnostics/Editor/AssemblyInfo.cs`
- Create: `unity/Packages/com.bun3.unity.diagnostics/Tests/Editor/Bun3.Unity.Diagnostics.Editor.Tests.asmdef`

**Interfaces:**
- Produces: assemblies `Bun3.Unity.Diagnostics.Editor` (Editor-only) and `Bun3.Unity.Diagnostics.Editor.Tests`; internals of the former visible to the latter.

- [ ] **Step 1: Write package.json**

```json
{
  "name": "com.bun3.unity.diagnostics",
  "displayName": "Bun3 Unity Diagnostics",
  "version": "0.1.0",
  "unity": "2022.3",
  "description": "Editor-only diagnostics for Unity. Dumps Frame Debugger captures to Markdown/JSON with batching analysis: per-shader and per-break-cause call counts, shader interleave detection, run-length stats, single-quad draw counts, and hierarchy-path aggregation. Built for humans and AI agents driving draw-call optimization without the GUI.",
  "author": {
    "name": "Bun3",
    "url": "https://github.com/Bun3",
    "email": "bun3.dev@gmail.com"
  }
}
```

- [ ] **Step 2: Write README.md**

```markdown
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
```

- [ ] **Step 3: Write the Editor asmdef**

`Editor/Bun3.Unity.Diagnostics.Editor.asmdef`:

```json
{
    "name": "Bun3.Unity.Diagnostics.Editor",
    "rootNamespace": "Bun3.Unity.Diagnostics",
    "references": [],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 4: Write AssemblyInfo.cs**

`Editor/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Bun3.Unity.Diagnostics.Editor.Tests")]
```

- [ ] **Step 5: Write the Tests asmdef**

`Tests/Editor/Bun3.Unity.Diagnostics.Editor.Tests.asmdef` (mirrors `Bun3.Unity.Window.Editor.Tests`):

```json
{
    "name": "Bun3.Unity.Diagnostics.Editor.Tests",
    "rootNamespace": "Bun3.Unity.Diagnostics.Editor.Tests",
    "references": [
        "Bun3.Unity.Diagnostics.Editor",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 6: Recompile and verify**

Run: `unity command recompile`, then poll `unity command recompile_status` until done.
Expected: success, 0 errors, 0 warnings (empty assemblies are fine).

- [ ] **Step 7: Commit (include generated .meta files)**

```bash
git add unity/Packages/com.bun3.unity.diagnostics
git commit -m "✨ Scaffold com.bun3.unity.diagnostics editor package" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: FrameEvent record + field mapping

**Files:**
- Create: `unity/Packages/com.bun3.unity.diagnostics/Editor/FrameEvent.cs`
- Test: `unity/Packages/com.bun3.unity.diagnostics/Tests/Editor/FrameEventTests.cs`

**Interfaces:**
- Produces: `public sealed class FrameEvent` — `[Serializable]`, public fields `int index; string eventType, shader, pass; int vertexCount, drawCallCount, instanceCount; string batchBreakCause, renderTarget, gameObjectPath` and
  `public static FrameEvent FromFields(int index, string eventType, IReadOnlyDictionary<string, string> fields, IReadOnlyList<string> breakCauses, string gameObjectPath)`.
  Field-dictionary keys are FrameDebuggerEventData field names with leading `m`/`_` trimmed (e.g. `m_VertexCount` → `VertexCount`).

- [ ] **Step 1: Write the failing tests**

`Tests/Editor/FrameEventTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;

namespace Bun3.Unity.Diagnostics.Editor.Tests
{
    public class FrameEventTests
    {
        [Test]
        public void FromFields_MapsKnownFields()
        {
            var fields = new Dictionary<string, string>
            {
                ["OriginalShaderName"] = "Sprites/Default",
                ["PassName"] = "Pass 0",
                ["VertexCount"] = "4",
                ["DrawCallCount"] = "1",
                ["InstanceCount"] = "2",
                ["BatchBreakCause"] = "2",
                ["RenderTargetName"] = "BackBuffer",
            };
            var causes = new[] { "a", "b", "Different materials" };

            var e = FrameEvent.FromFields(7, "Draw Mesh", fields, causes, "Root/Child");

            Assert.AreEqual(7, e.index);
            Assert.AreEqual("Draw Mesh", e.eventType);
            Assert.AreEqual("Sprites/Default", e.shader);
            Assert.AreEqual("Pass 0", e.pass);
            Assert.AreEqual(4, e.vertexCount);
            Assert.AreEqual(1, e.drawCallCount);
            Assert.AreEqual(2, e.instanceCount);
            Assert.AreEqual("Different materials", e.batchBreakCause);
            Assert.AreEqual("BackBuffer", e.renderTarget);
            Assert.AreEqual("Root/Child", e.gameObjectPath);
        }

        [Test]
        public void FromFields_MissingFieldsFallBackToDefaults()
        {
            var e = FrameEvent.FromFields(0, "Clear", new Dictionary<string, string>(), null, "");

            Assert.AreEqual("", e.shader);
            Assert.AreEqual(0, e.vertexCount);
            Assert.AreEqual("", e.batchBreakCause);
            Assert.AreEqual("", e.gameObjectPath);
        }

        [Test]
        public void FromFields_OutOfRangeBreakCauseKeepsRawValue()
        {
            var fields = new Dictionary<string, string> { ["BatchBreakCause"] = "9" };

            var e = FrameEvent.FromFields(0, "Draw", fields, new[] { "a" }, "");

            Assert.AreEqual("9", e.batchBreakCause);
        }

        [Test]
        public void FromFields_PrefersRenderTargetRenderTexture()
        {
            var fields = new Dictionary<string, string>
            {
                ["RenderTargetRenderTexture"] = "RT",
                ["RenderTargetName"] = "BB",
            };

            var e = FrameEvent.FromFields(0, "Draw", fields, null, "");

            Assert.AreEqual("RT", e.renderTarget);
        }
    }
}
```

- [ ] **Step 2: Recompile — verify compile failure (FrameEvent not defined)**

Run: `unity command recompile` → `unity command recompile_status`.
Expected: compile error mentioning `FrameEvent`.

- [ ] **Step 3: Implement FrameEvent**

`Editor/FrameEvent.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Bun3.Unity.Diagnostics
{
    /// <summary>One captured Frame Debugger event, flattened for reports and JSON output.</summary>
    [Serializable]
    public sealed class FrameEvent
    {
        /// <summary>Zero-based event index within the capture.</summary>
        public int index;

        /// <summary>Frame Debugger event type name (e.g. "Draw Mesh", "Clear").</summary>
        public string eventType = "";

        /// <summary>Original shader name; empty when the event has none.</summary>
        public string shader = "";

        /// <summary>Shader pass name.</summary>
        public string pass = "";

        /// <summary>Vertex count of the draw.</summary>
        public int vertexCount;

        /// <summary>Number of draw calls merged into this event.</summary>
        public int drawCallCount;

        /// <summary>Instance count.</summary>
        public int instanceCount;

        /// <summary>Human-readable batching break cause.</summary>
        public string batchBreakCause = "";

        /// <summary>Render target name.</summary>
        public string renderTarget = "";

        /// <summary>Scene hierarchy path of the source game object; empty when unresolved.</summary>
        public string gameObjectPath = "";

        /// <summary>
        /// Builds a <see cref="FrameEvent"/> from one FrameDebuggerEventData flattened into a
        /// name/value dictionary (field names with leading "m"/"_" trimmed). Missing fields
        /// resolve to empty/zero so editor-version field changes degrade instead of failing.
        /// </summary>
        public static FrameEvent FromFields(
            int index,
            string eventType,
            IReadOnlyDictionary<string, string> fields,
            IReadOnlyList<string> breakCauses,
            string gameObjectPath)
        {
            return new FrameEvent
            {
                index = index,
                eventType = eventType ?? "",
                shader = Get(fields, "OriginalShaderName"),
                pass = Get(fields, "PassName"),
                vertexCount = GetInt(fields, "VertexCount"),
                drawCallCount = GetInt(fields, "DrawCallCount"),
                instanceCount = GetInt(fields, "InstanceCount"),
                batchBreakCause = ResolveBreakCause(Get(fields, "BatchBreakCause"), breakCauses),
                renderTarget = Get(fields, "RenderTargetRenderTexture", Get(fields, "RenderTargetName")),
                gameObjectPath = gameObjectPath ?? "",
            };
        }

        static string Get(IReadOnlyDictionary<string, string> fields, string key, string fallback = "") =>
            fields.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : fallback;

        static int GetInt(IReadOnlyDictionary<string, string> fields, string key) =>
            int.TryParse(Get(fields, key), out var v) ? v : 0;

        static string ResolveBreakCause(string raw, IReadOnlyList<string> causes)
        {
            if (string.IsNullOrEmpty(raw))
                return "";
            if (causes != null && int.TryParse(raw, out var i) && i >= 0 && i < causes.Count)
                return causes[i];
            return raw;
        }
    }
}
```

- [ ] **Step 4: Recompile and run tests**

Run: `unity command recompile` → `unity command recompile_status` → `unity command run_tests --filter FrameEventTests`.
Expected: 4 tests PASS, 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add unity/Packages/com.bun3.unity.diagnostics
git commit -m "✨ FrameEvent record with tolerant field mapping" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Analyzer — aggregations, single-quad, path prefix

**Files:**
- Create: `unity/Packages/com.bun3.unity.diagnostics/Editor/FrameDumpAnalyzer.cs`
- Test: `unity/Packages/com.bun3.unity.diagnostics/Tests/Editor/FrameDumpAnalyzerTests.cs`

**Interfaces:**
- Consumes: `FrameEvent` (Task 2).
- Produces: `[Serializable]` result types `CountEntry { string key; int count; }`, `InterleaveSpan { int startIndex, endIndex; string shaderA, shaderB; int switchCount, wastedCalls; }`, `ShaderRun { int startIndex, length; string shader; }`, `FrameDumpAnalysis { int totalEvents; List<CountEntry> callsByShader, callsByBreakCause, callsByShaderAndBreakCause; List<InterleaveSpan> interleaves; List<ShaderRun> longestRuns; int singleQuadDrawCount; List<CountEntry> callsByPathPrefix; }` and
  `public static class FrameDumpAnalyzer { public const int InterleaveMinSwitches = 4; public static FrameDumpAnalysis Analyze(IReadOnlyList<FrameEvent> events); }`.
  (Interleave/run detection bodies land in Task 4; this task stubs them returning empty lists.)

- [ ] **Step 1: Write the failing tests (aggregation half)**

`Tests/Editor/FrameDumpAnalyzerTests.cs`:

```csharp
using NUnit.Framework;

namespace Bun3.Unity.Diagnostics.Editor.Tests
{
    public class FrameDumpAnalyzerTests
    {
        static FrameEvent E(int index, string shader, string breakCause = "", int vtx = 0, string path = "") =>
            new FrameEvent
            {
                index = index,
                shader = shader,
                batchBreakCause = breakCause,
                vertexCount = vtx,
                gameObjectPath = path,
            };

        [Test]
        public void Analyze_CountsByShaderDescending()
        {
            var a = FrameDumpAnalyzer.Analyze(new[] { E(0, "A"), E(1, "B"), E(2, "A") });

            Assert.AreEqual(3, a.totalEvents);
            Assert.AreEqual(2, a.callsByShader.Count);
            Assert.AreEqual("A", a.callsByShader[0].key);
            Assert.AreEqual(2, a.callsByShader[0].count);
            Assert.AreEqual("B", a.callsByShader[1].key);
        }

        [Test]
        public void Analyze_SkipsEmptyKeys()
        {
            var a = FrameDumpAnalyzer.Analyze(new[] { E(0, ""), E(1, "A") });

            Assert.AreEqual(1, a.callsByShader.Count);
        }

        [Test]
        public void Analyze_CountsShaderBreakCausePairs()
        {
            var a = FrameDumpAnalyzer.Analyze(new[]
            {
                E(0, "A", "Different materials"),
                E(1, "A", "Different materials"),
                E(2, "A", "First batch"),
            });

            Assert.AreEqual("A | Different materials", a.callsByShaderAndBreakCause[0].key);
            Assert.AreEqual(2, a.callsByShaderAndBreakCause[0].count);
        }

        [Test]
        public void Analyze_CountsSingleQuadDraws()
        {
            var a = FrameDumpAnalyzer.Analyze(new[] { E(0, "A", vtx: 4), E(1, "A", vtx: 4), E(2, "B", vtx: 600) });

            Assert.AreEqual(2, a.singleQuadDrawCount);
        }

        [Test]
        public void Analyze_AggregatesByTwoSegmentPathPrefix()
        {
            var a = FrameDumpAnalyzer.Analyze(new[]
            {
                E(0, "A", path: "UI/Main/Btn1"),
                E(1, "A", path: "UI/Main/Btn2"),
                E(2, "A", path: "World"),
            });

            Assert.AreEqual("UI/Main", a.callsByPathPrefix[0].key);
            Assert.AreEqual(2, a.callsByPathPrefix[0].count);
            Assert.AreEqual("World", a.callsByPathPrefix[1].key);
        }
    }
}
```

- [ ] **Step 2: Recompile — verify compile failure (FrameDumpAnalyzer not defined)**

Run: `unity command recompile` → `unity command recompile_status`. Expected: compile error.

- [ ] **Step 3: Implement result types + analyzer (interleave/runs stubbed)**

`Editor/FrameDumpAnalyzer.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Bun3.Unity.Diagnostics
{
    /// <summary>A key with an aggregated call count.</summary>
    [Serializable]
    public sealed class CountEntry
    {
        /// <summary>Aggregation key (shader name, break cause, pair, or path prefix).</summary>
        public string key = "";

        /// <summary>Number of events aggregated under the key.</summary>
        public int count;
    }

    /// <summary>A span where two shaders alternate A-B-A-B, defeating batching.</summary>
    [Serializable]
    public sealed class InterleaveSpan
    {
        /// <summary>Event index where the span starts.</summary>
        public int startIndex;

        /// <summary>Event index where the span ends (inclusive).</summary>
        public int endIndex;

        /// <summary>First alternating shader.</summary>
        public string shaderA = "";

        /// <summary>Second alternating shader.</summary>
        public string shaderB = "";

        /// <summary>Number of shader switches inside the span.</summary>
        public int switchCount;

        /// <summary>Draw calls beyond the two the span could batch down to.</summary>
        public int wastedCalls;
    }

    /// <summary>A run of consecutive events sharing one shader.</summary>
    [Serializable]
    public sealed class ShaderRun
    {
        /// <summary>Event index where the run starts.</summary>
        public int startIndex;

        /// <summary>Number of consecutive events in the run.</summary>
        public int length;

        /// <summary>Shader shared by the run.</summary>
        public string shader = "";
    }

    /// <summary>Aggregated batching analysis of one Frame Debugger capture.</summary>
    [Serializable]
    public sealed class FrameDumpAnalysis
    {
        /// <summary>Total captured events.</summary>
        public int totalEvents;

        /// <summary>Call counts per shader, descending.</summary>
        public List<CountEntry> callsByShader = new List<CountEntry>();

        /// <summary>Call counts per batch-break cause, descending.</summary>
        public List<CountEntry> callsByBreakCause = new List<CountEntry>();

        /// <summary>Call counts per "shader | break cause" pair, descending.</summary>
        public List<CountEntry> callsByShaderAndBreakCause = new List<CountEntry>();

        /// <summary>Spans where two shaders alternate A-B-A-B.</summary>
        public List<InterleaveSpan> interleaves = new List<InterleaveSpan>();

        /// <summary>Longest consecutive same-shader runs, descending by length.</summary>
        public List<ShaderRun> longestRuns = new List<ShaderRun>();

        /// <summary>Number of draws with exactly four vertices (missing-atlas signal).</summary>
        public int singleQuadDrawCount;

        /// <summary>Call counts per two-segment hierarchy path prefix, descending.</summary>
        public List<CountEntry> callsByPathPrefix = new List<CountEntry>();
    }

    /// <summary>Pure analysis over captured <see cref="FrameEvent"/> lists; no editor state.</summary>
    public static class FrameDumpAnalyzer
    {
        /// <summary>Minimum shader switches for a span to be reported as interleaved.</summary>
        public const int InterleaveMinSwitches = 4;

        const int TopRunCount = 10;

        /// <summary>Runs every aggregation and detector over the events.</summary>
        public static FrameDumpAnalysis Analyze(IReadOnlyList<FrameEvent> events)
        {
            return new FrameDumpAnalysis
            {
                totalEvents = events.Count,
                callsByShader = CountBy(events, e => e.shader),
                callsByBreakCause = CountBy(events, e => e.batchBreakCause),
                callsByShaderAndBreakCause = CountBy(
                    events,
                    e => string.IsNullOrEmpty(e.shader) ? "" : $"{e.shader} | {e.batchBreakCause}"),
                interleaves = DetectInterleaves(events),
                longestRuns = LongestRuns(events),
                singleQuadDrawCount = events.Count(e => e.vertexCount == 4),
                callsByPathPrefix = CountBy(events, e => PathPrefix(e.gameObjectPath)),
            };
        }

        static List<CountEntry> CountBy(IReadOnlyList<FrameEvent> events, Func<FrameEvent, string> keyOf)
        {
            var counts = new Dictionary<string, int>();
            foreach (var e in events)
            {
                var key = keyOf(e);
                if (string.IsNullOrEmpty(key))
                    continue;
                counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
            }

            return counts
                .Select(p => new CountEntry { key = p.Key, count = p.Value })
                .OrderByDescending(x => x.count)
                .ThenBy(x => x.key, StringComparer.Ordinal)
                .ToList();
        }

        static List<InterleaveSpan> DetectInterleaves(IReadOnlyList<FrameEvent> events)
        {
            return new List<InterleaveSpan>();
        }

        static List<ShaderRun> LongestRuns(IReadOnlyList<FrameEvent> events)
        {
            return new List<ShaderRun>();
        }

        static string PathPrefix(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "";
            int first = path.IndexOf('/');
            if (first < 0)
                return path;
            int second = path.IndexOf('/', first + 1);
            return second < 0 ? path : path.Substring(0, second);
        }
    }
}
```

- [ ] **Step 4: Recompile and run tests**

Run: `unity command recompile` → `unity command recompile_status` → `unity command run_tests --filter FrameDumpAnalyzerTests`.
Expected: 5 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add unity/Packages/com.bun3.unity.diagnostics
git commit -m "✨ Frame dump analyzer aggregations" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Analyzer — interleave detection + run-length

**Files:**
- Modify: `unity/Packages/com.bun3.unity.diagnostics/Editor/FrameDumpAnalyzer.cs` (replace the two stub bodies)
- Test: `unity/Packages/com.bun3.unity.diagnostics/Tests/Editor/FrameDumpAnalyzerTests.cs` (append tests)

**Interfaces:**
- Consumes/Produces: same types as Task 3; only `DetectInterleaves` and `LongestRuns` gain real bodies.

- [ ] **Step 1: Append the failing tests**

Append inside `FrameDumpAnalyzerTests`:

```csharp
        [Test]
        public void Analyze_ReportsAlternatingInterleaveSpan()
        {
            var a = FrameDumpAnalyzer.Analyze(new[] { E(0, "A"), E(1, "B"), E(2, "A"), E(3, "B"), E(4, "A") });

            Assert.AreEqual(1, a.interleaves.Count);
            var s = a.interleaves[0];
            Assert.AreEqual(0, s.startIndex);
            Assert.AreEqual(4, s.endIndex);
            Assert.AreEqual("A", s.shaderA);
            Assert.AreEqual("B", s.shaderB);
            Assert.AreEqual(4, s.switchCount);
            Assert.AreEqual(3, s.wastedCalls);
        }

        [Test]
        public void Analyze_IgnoresShortAlternation()
        {
            var a = FrameDumpAnalyzer.Analyze(new[] { E(0, "A"), E(1, "B"), E(2, "A"), E(3, "C"), E(4, "C") });

            Assert.IsEmpty(a.interleaves);
        }

        [Test]
        public void Analyze_EmptyShaderBreaksInterleaveSpan()
        {
            var a = FrameDumpAnalyzer.Analyze(new[]
            {
                E(0, "A"), E(1, "B"), E(2, ""), E(3, "A"), E(4, "B"), E(5, "A"),
            });

            Assert.IsEmpty(a.interleaves);
        }

        [Test]
        public void Analyze_FindsLongestRunsDescending()
        {
            var a = FrameDumpAnalyzer.Analyze(new[] { E(0, "A"), E(1, "A"), E(2, "A"), E(3, "B"), E(4, "B") });

            Assert.AreEqual(2, a.longestRuns.Count);
            Assert.AreEqual("A", a.longestRuns[0].shader);
            Assert.AreEqual(3, a.longestRuns[0].length);
            Assert.AreEqual(0, a.longestRuns[0].startIndex);
            Assert.AreEqual("B", a.longestRuns[1].shader);
        }
```

- [ ] **Step 2: Recompile and run — verify the new tests FAIL (stubs return empty)**

Run: `unity command recompile` → `unity command recompile_status` → `unity command run_tests --filter FrameDumpAnalyzerTests`.
Expected: the four new tests FAIL, earlier five still PASS.

- [ ] **Step 3: Implement the detectors**

Replace the two stub bodies in `FrameDumpAnalyzer.cs`:

```csharp
        static List<InterleaveSpan> DetectInterleaves(IReadOnlyList<FrameEvent> events)
        {
            var spans = new List<InterleaveSpan>();
            int i = 0;
            while (i < events.Count - 1)
            {
                string a = events[i].shader;
                string b = events[i + 1].shader;
                if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || a == b)
                {
                    i++;
                    continue;
                }

                int j = i + 1;
                int switches = 1;
                while (j + 1 < events.Count)
                {
                    string expected = events[j].shader == a ? b : a;
                    if (events[j + 1].shader != expected)
                        break;
                    j++;
                    switches++;
                }

                if (switches >= InterleaveMinSwitches)
                {
                    spans.Add(new InterleaveSpan
                    {
                        startIndex = events[i].index,
                        endIndex = events[j].index,
                        shaderA = a,
                        shaderB = b,
                        switchCount = switches,
                        // A span of N alternating events could collapse to two batches.
                        wastedCalls = j - i - 1,
                    });
                }

                i = j;
            }

            return spans;
        }

        static List<ShaderRun> LongestRuns(IReadOnlyList<FrameEvent> events)
        {
            var runs = new List<ShaderRun>();
            for (int i = 0; i < events.Count;)
            {
                string shader = events[i].shader;
                int start = i;
                while (i < events.Count && events[i].shader == shader)
                    i++;
                if (!string.IsNullOrEmpty(shader))
                    runs.Add(new ShaderRun { startIndex = events[start].index, length = i - start, shader = shader });
            }

            return runs
                .OrderByDescending(r => r.length)
                .ThenBy(r => r.startIndex)
                .Take(TopRunCount)
                .ToList();
        }
```

- [ ] **Step 4: Recompile and run tests**

Run: `unity command recompile` → `unity command recompile_status` → `unity command run_tests --filter FrameDumpAnalyzerTests`.
Expected: all 9 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add unity/Packages/com.bun3.unity.diagnostics
git commit -m "✨ Interleave detection and shader run-length analysis" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: Reflection layer + binding tests

**Files:**
- Create: `unity/Packages/com.bun3.unity.diagnostics/Editor/FrameDebuggerReflection.cs`
- Test: `unity/Packages/com.bun3.unity.diagnostics/Tests/Editor/FrameDebuggerReflectionTests.cs`

**Interfaces:**
- Produces (internal, visible to tests): `internal static class FrameDebuggerReflection` with
  `static List<string> Bind()` (returns missing member names; empty = fully bound),
  `static bool IsBound { get; }` (core members bound: type, GetFrameEventData, GetFrameEvents, limit, count),
  `static int GetCount()`, `static void SetLimit(int value)`, `static void Repaint()`,
  `static bool TryEnable()`, `static string[] GetEventTypeNames(int count)`,
  `static string[] GetBreakCauseStrings()`,
  `static bool TryGetEventData(int index, out Dictionary<string, string> fields)`,
  `static Type GetEventDataType()`.

- [ ] **Step 1: Write the failing tests**

`Tests/Editor/FrameDebuggerReflectionTests.cs`:

```csharp
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Bun3.Unity.Diagnostics.Editor.Tests
{
    public class FrameDebuggerReflectionTests
    {
        [Test]
        public void AllFrameDebuggerBindingsResolve()
        {
            var missing = FrameDebuggerReflection.Bind();

            Assert.IsEmpty(missing, "Editor upgrade broke Frame Debugger reflection: " + string.Join(", ", missing));
            Assert.IsTrue(FrameDebuggerReflection.IsBound);
        }

        [Test]
        public void EventDataExposesMappedFields()
        {
            FrameDebuggerReflection.Bind();
            var type = FrameDebuggerReflection.GetEventDataType();
            Assert.IsNotNull(type);

            var names = type
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(f => f.Name.TrimStart('m', '_'))
                .ToArray();
            var required = new[]
            {
                "OriginalShaderName", "PassName", "VertexCount", "DrawCallCount",
                "InstanceCount", "BatchBreakCause", "ComponentInstanceID",
            };
            foreach (var name in required)
                Assert.Contains(name, names, $"FrameDebuggerEventData no longer exposes {name}");
        }
    }
}
```

- [ ] **Step 2: Recompile — verify compile failure (FrameDebuggerReflection not defined)**

Run: `unity command recompile` → `unity command recompile_status`. Expected: compile error.

- [ ] **Step 3: Implement the reflection layer**

`Editor/FrameDebuggerReflection.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditorInternal;

namespace Bun3.Unity.Diagnostics
{
    // Name-based reflection over UnityEditor's internal FrameDebuggerUtility. Bind() reports what
    // is missing instead of throwing, so editor upgrades fail loudly in the binding test rather
    // than silently at dump time. FrameDebuggerUtility lives in a split editor module assembly, so
    // the type is searched across all loaded assemblies, not typeof(Editor).Assembly.
    internal static class FrameDebuggerReflection
    {
        const BindingFlags Flags =
            BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static Type s_Util;
        static MethodInfo s_GetFrameEventData;
        static MethodInfo s_GetFrameEvents;
        static MethodInfo s_GetBatchBreakCauseStrings;
        static MethodInfo s_SetEnabled;
        static PropertyInfo s_Limit;

        internal static bool IsBound { get; private set; }

        internal static List<string> Bind()
        {
            var missing = new List<string>();
            IsBound = false;
            s_Util = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Type.EmptyTypes; }
                })
                .FirstOrDefault(t => t.Name == "FrameDebuggerUtility");
            if (s_Util == null)
            {
                missing.Add("FrameDebuggerUtility");
                return missing;
            }

            s_GetFrameEventData = s_Util.GetMethods(Flags).FirstOrDefault(m => m.Name == "GetFrameEventData");
            if (s_GetFrameEventData == null)
                missing.Add("FrameDebuggerUtility.GetFrameEventData");
            s_GetFrameEvents = s_Util.GetMethod("GetFrameEvents", Flags);
            if (s_GetFrameEvents == null)
                missing.Add("FrameDebuggerUtility.GetFrameEvents");
            s_Limit = s_Util.GetProperty("limit", Flags);
            if (s_Limit == null)
                missing.Add("FrameDebuggerUtility.limit");
            if (s_Util.GetProperty("count", Flags) == null && s_Util.GetField("count", Flags) == null)
                missing.Add("FrameDebuggerUtility.count");

            IsBound = missing.Count == 0;

            // Optional members: their absence only degrades features (raw cause indexes, no
            // auto-enable) and must not block dumping, but the binding test still reports them.
            s_GetBatchBreakCauseStrings = s_Util.GetMethod("GetBatchBreakCauseStrings", Flags);
            if (s_GetBatchBreakCauseStrings == null)
                missing.Add("FrameDebuggerUtility.GetBatchBreakCauseStrings (optional)");
            s_SetEnabled = s_Util.GetMethod("SetEnabled", Flags);
            if (s_SetEnabled == null)
                missing.Add("FrameDebuggerUtility.SetEnabled (optional)");

            return missing;
        }

        internal static int GetCount() => Convert.ToInt32(GetStatic("count") ?? 0);

        internal static void SetLimit(int value)
        {
            s_Limit.SetValue(null, value);
            InternalEditorUtility.RepaintAllViews();
        }

        internal static void Repaint() => InternalEditorUtility.RepaintAllViews();

        internal static bool TryEnable()
        {
            if (s_SetEnabled == null)
                return false;
            try
            {
                s_SetEnabled.Invoke(null, new object[] { true, ProfilerDriver.connectedProfiler });
                InternalEditorUtility.RepaintAllViews();
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static string[] GetEventTypeNames(int count)
        {
            var names = new string[count];
            var events = (Array)s_GetFrameEvents.Invoke(null, null);
            for (int i = 0; i < count; i++)
            {
                names[i] = "?";
                if (i >= events.Length)
                    continue;
                object ev = events.GetValue(i);
                names[i] = (GetMember(ev, "type") ?? GetMember(ev, "Type"))?.ToString() ?? "?";
            }

            return names;
        }

        internal static string[] GetBreakCauseStrings()
        {
            try
            {
                return (string[])s_GetBatchBreakCauseStrings?.Invoke(null, null);
            }
            catch
            {
                return null;
            }
        }

        internal static Type GetEventDataType()
        {
            if (s_GetFrameEventData == null)
                return null;
            var pt = s_GetFrameEventData.GetParameters()[1].ParameterType;
            return pt.IsByRef ? pt.GetElementType() : pt;
        }

        internal static bool TryGetEventData(int index, out Dictionary<string, string> fields)
        {
            fields = null;
            var ps = s_GetFrameEventData.GetParameters();
            object[] args =
            {
                index,
                ps[1].ParameterType.IsByRef ? null : Activator.CreateInstance(GetEventDataType()),
            };
            bool ok;
            try
            {
                ok = (bool)s_GetFrameEventData.Invoke(null, args);
            }
            catch
            {
                return false;
            }

            if (!ok || args[1] == null)
                return false;

            // Right after moving the limit, the previous event's data can linger; require an
            // index match before trusting the payload.
            object fei = GetMember(args[1], "frameEventIndex") ?? GetMember(args[1], "FrameEventIndex");
            if (fei != null && Convert.ToInt32(fei) != index)
                return false;

            fields = Flatten(args[1]);
            return true;
        }

        static Dictionary<string, string> Flatten(object data)
        {
            var fields = new Dictionary<string, string>();
            foreach (var fi in data.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object v;
                try
                {
                    v = fi.GetValue(data);
                }
                catch
                {
                    continue;
                }

                if (v == null)
                    continue;
                var vt = v.GetType();
                if (vt.IsPrimitive || vt.IsEnum || v is string)
                    fields[fi.Name.TrimStart('m', '_')] = v.ToString();
                else if (v is UnityEngine.Object uo && uo)
                    fields[fi.Name.TrimStart('m', '_')] = uo.name;
            }

            return fields;
        }

        static object GetStatic(string name)
        {
            var p = s_Util.GetProperty(name, Flags);
            if (p != null)
                return p.GetValue(null);
            return s_Util.GetField(name, Flags)?.GetValue(null);
        }

        static object GetMember(object o, string name)
        {
            var t = o.GetType();
            var f = t.GetField(name, Flags) ?? t.GetField("m_" + char.ToUpper(name[0]) + name.Substring(1), Flags);
            if (f != null)
                return f.GetValue(o);
            var p = t.GetProperty(name, Flags);
            return p != null && p.GetIndexParameters().Length == 0 ? p.GetValue(o) : null;
        }
    }
}
```

- [ ] **Step 4: Recompile and run tests**

Run: `unity command recompile` → `unity command recompile_status` → `unity command run_tests --filter FrameDebuggerReflectionTests`.
Expected: 2 tests PASS on the current 6000.3 editor.

**If a binding test fails here, that is the spec's "6000.x 검증 필요" item surfacing** — do not weaken the test. Find the renamed member (`unity command` eval or decompiled UnityEditor sources), add the 6000.x name as an alias (in `Bind()` lookups or as an extra key checked by `FrameEvent.FromFields` — add the alias to both the mapping and the test's required-name list), and keep the 2022.3 name first.

- [ ] **Step 5: Commit**

```bash
git add unity/Packages/com.bun3.unity.diagnostics
git commit -m "✨ Tolerant reflection binding for FrameDebuggerUtility" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: Report writer (Markdown + JSON)

**Files:**
- Create: `unity/Packages/com.bun3.unity.diagnostics/Editor/FrameDumpReportWriter.cs`
- Test: `unity/Packages/com.bun3.unity.diagnostics/Tests/Editor/FrameDumpReportWriterTests.cs`

**Interfaces:**
- Consumes: `FrameEvent`, `FrameDumpAnalysis` (+ its entry types).
- Produces (internal): `internal static class FrameDumpReportWriter` with
  `static string ToMarkdown(string timestamp, IReadOnlyList<FrameEvent> events, FrameDumpAnalysis analysis)` and
  `static string ToJson(string timestamp, List<FrameEvent> events, FrameDumpAnalysis analysis)`;
  `[Serializable] internal sealed class FrameDumpDocument { string timestamp; List<FrameEvent> events; FrameDumpAnalysis analysis; }`.

- [ ] **Step 1: Write the failing tests**

`Tests/Editor/FrameDumpReportWriterTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Diagnostics.Editor.Tests
{
    public class FrameDumpReportWriterTests
    {
        static List<FrameEvent> SampleEvents() => new List<FrameEvent>
        {
            new FrameEvent { index = 0, eventType = "Draw Mesh", shader = "A", vertexCount = 4 },
            new FrameEvent { index = 1, eventType = "Draw Mesh", shader = "B", batchBreakCause = "Different materials" },
        };

        [Test]
        public void Markdown_ContainsSummarySectionsAndEventLines()
        {
            var events = SampleEvents();

            var md = FrameDumpReportWriter.ToMarkdown("2026-08-29 12:00:00", events, FrameDumpAnalyzer.Analyze(events));

            StringAssert.Contains("# Frame Debugger Dump 2026-08-29 12:00:00", md);
            StringAssert.Contains("## Calls by shader", md);
            StringAssert.Contains("## Calls by batch-break cause", md);
            StringAssert.Contains("## Interleaved spans", md);
            StringAssert.Contains("## Longest same-shader runs", md);
            StringAssert.Contains("## Calls by path prefix", md);
            StringAssert.Contains("Single-quad draws (vtx=4): 1", md);
            StringAssert.Contains("[0] Draw Mesh shader=A", md);
            StringAssert.Contains("break=Different materials", md);
        }

        [Test]
        public void Json_RoundTripsThroughJsonUtility()
        {
            var events = SampleEvents();

            var json = FrameDumpReportWriter.ToJson("t", events, FrameDumpAnalyzer.Analyze(events));
            var doc = JsonUtility.FromJson<FrameDumpDocument>(json);

            Assert.AreEqual("t", doc.timestamp);
            Assert.AreEqual(2, doc.events.Count);
            Assert.AreEqual("A", doc.events[0].shader);
            Assert.AreEqual(2, doc.analysis.totalEvents);
        }
    }
}
```

- [ ] **Step 2: Recompile — verify compile failure**

Run: `unity command recompile` → `unity command recompile_status`. Expected: compile error.

- [ ] **Step 3: Implement the writer**

`Editor/FrameDumpReportWriter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Bun3.Unity.Diagnostics
{
    // JSON document shape: { timestamp, events[], analysis } — the structured twin of the
    // Markdown report, serialized with JsonUtility (no external JSON dependency).
    [Serializable]
    internal sealed class FrameDumpDocument
    {
        public string timestamp = "";
        public List<FrameEvent> events = new List<FrameEvent>();
        public FrameDumpAnalysis analysis = new FrameDumpAnalysis();
    }

    internal static class FrameDumpReportWriter
    {
        const int TopEntryCount = 15;

        internal static string ToJson(string timestamp, List<FrameEvent> events, FrameDumpAnalysis analysis) =>
            JsonUtility.ToJson(
                new FrameDumpDocument { timestamp = timestamp, events = events, analysis = analysis },
                true);

        internal static string ToMarkdown(string timestamp, IReadOnlyList<FrameEvent> events, FrameDumpAnalysis analysis)
        {
            var sb = new StringBuilder(1 << 20);
            sb.AppendLine($"# Frame Debugger Dump {timestamp}");
            sb.AppendLine();
            sb.AppendLine($"- Total events: {analysis.totalEvents}");
            sb.AppendLine($"- Single-quad draws (vtx=4): {analysis.singleQuadDrawCount}");
            if (analysis.callsByBreakCause.Count > 0)
                sb.AppendLine($"- Top batch breaker: {analysis.callsByBreakCause[0].key} ({analysis.callsByBreakCause[0].count} calls)");

            CountSection(sb, "Calls by shader", analysis.callsByShader);
            CountSection(sb, "Calls by batch-break cause", analysis.callsByBreakCause);
            CountSection(sb, "Calls by shader x break cause", analysis.callsByShaderAndBreakCause);

            sb.AppendLine();
            sb.AppendLine("## Interleaved spans (A-B-A-B)");
            if (analysis.interleaves.Count == 0)
                sb.AppendLine("(none)");
            foreach (var s in analysis.interleaves)
                sb.AppendLine($"- [{s.startIndex}..{s.endIndex}] {s.shaderA} <-> {s.shaderB}: {s.switchCount} switches, ~{s.wastedCalls} wasted calls");

            sb.AppendLine();
            sb.AppendLine("## Longest same-shader runs");
            if (analysis.longestRuns.Count == 0)
                sb.AppendLine("(none)");
            foreach (var r in analysis.longestRuns)
                sb.AppendLine($"- [{r.startIndex}..{r.startIndex + r.length - 1}] {r.shader}: {r.length} calls");

            CountSection(sb, "Calls by path prefix", analysis.callsByPathPrefix);

            sb.AppendLine();
            sb.AppendLine("## Events");
            foreach (var e in events)
                sb.AppendLine(
                    $"[{e.index}] {e.eventType} shader={e.shader} pass={e.pass} vtx={e.vertexCount}" +
                    $" draws={e.drawCallCount} inst={e.instanceCount} break={e.batchBreakCause}" +
                    $" rt={e.renderTarget} go={e.gameObjectPath}");

            return sb.ToString();
        }

        static void CountSection(StringBuilder sb, string title, List<CountEntry> entries)
        {
            sb.AppendLine();
            sb.AppendLine($"## {title}");
            if (entries.Count == 0)
            {
                sb.AppendLine("(none)");
                return;
            }

            int shown = Math.Min(entries.Count, TopEntryCount);
            for (int i = 0; i < shown; i++)
                sb.AppendLine($"- {entries[i].key}: {entries[i].count}");
            if (entries.Count > shown)
                sb.AppendLine($"- ... {entries.Count - shown} more");
        }
    }
}
```

- [ ] **Step 4: Recompile and run tests**

Run: `unity command recompile` → `unity command recompile_status` → `unity command run_tests --filter FrameDumpReportWriterTests`.
Expected: 2 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add unity/Packages/com.bun3.unity.diagnostics
git commit -m "✨ Markdown and JSON report writer" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: Dumper state machine + menu

**Files:**
- Create: `unity/Packages/com.bun3.unity.diagnostics/Editor/FrameDebuggerDumper.cs`

**Interfaces:**
- Consumes: `FrameDebuggerReflection` (Task 5), `FrameEvent.FromFields` (Task 2), `FrameDumpAnalyzer.Analyze` (Tasks 3-4), `FrameDumpReportWriter` (Task 6).
- Produces: `public static class FrameDebuggerDumper { public static bool IsRunning { get; } public static FrameDumpResult LastResult { get; } public static Task<FrameDumpResult> DumpAsync(); }` and
  `[Serializable] public sealed class FrameDumpResult { bool success; string error; int eventCount, capturedCount; string markdownPath, jsonPath, topBreakCause; }` (all public fields). Menu item `Tools/Bun3/Frame Debugger Dump`.

- [ ] **Step 1: Implement the dumper**

No unit test — the traversal needs a live Frame Debugger capture (spec: E2E is manual). The runnable checks are the existing suite plus compile with 0 warnings; the state-machine logic is a direct port of the prototype verified on 2022.3.

`Editor/FrameDebuggerDumper.cs`:

```csharp
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
```

- [ ] **Step 2: Recompile and run the full package suite**

Run: `unity command recompile` → `unity command recompile_status` → `unity command run_tests --filter Bun3.Unity.Diagnostics`.
Expected: compile clean (0 warnings), all package tests PASS. (If the namespace filter matches nothing, run the four test classes by name.)

- [ ] **Step 3: Smoke-check the menu registration**

Run via unity-cli C# eval (e.g. `unity command` eval): `UnityEditor.Menu.MenuItemExists("Tools/Bun3/Frame Debugger Dump")` — expected `true`. If no eval command is available, skip; compile success already proves the attribute is valid, and the manual E2E covers the menu.

- [ ] **Step 4: Commit**

```bash
git add unity/Packages/com.bun3.unity.diagnostics
git commit -m "✨ Frame Debugger dump traversal, menu, and report output" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 8: unity-cli commands (conditional assembly)

**Files:**
- Create: `unity/Packages/com.bun3.unity.diagnostics/Editor/Cli/Bun3.Unity.Diagnostics.Editor.Cli.asmdef`
- Create: `unity/Packages/com.bun3.unity.diagnostics/Editor/Cli/FrameDebuggerCliCommands.cs`

**Interfaces:**
- Consumes: `FrameDebuggerDumper.IsRunning/LastResult/DumpAsync()` (Task 7), `FrameDumpResult` (Task 7), `Unity.Pipeline.Commands.CliCommandAttribute` (from `com.unity.pipeline`, assembly `Unity.Pipeline`).
- Produces: CLI commands `framedebugger_dump` and `framedebugger_dump_status`.

The whole assembly is gated: `versionDefines` sets `BUN3_UNITY_PIPELINE` when `com.unity.pipeline` is installed, and `defineConstraints` keeps the assembly (and its `Unity.Pipeline` reference) from compiling at all on 2022.3 projects without it. No `#if` needed inside the file.

- [ ] **Step 1: Write the Cli asmdef**

`Editor/Cli/Bun3.Unity.Diagnostics.Editor.Cli.asmdef`:

```json
{
    "name": "Bun3.Unity.Diagnostics.Editor.Cli",
    "rootNamespace": "Bun3.Unity.Diagnostics",
    "references": [
        "Bun3.Unity.Diagnostics.Editor",
        "Unity.Pipeline"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [
        "BUN3_UNITY_PIPELINE"
    ],
    "versionDefines": [
        {
            "name": "com.unity.pipeline",
            "expression": "",
            "define": "BUN3_UNITY_PIPELINE"
        }
    ],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Implement the commands**

`Editor/Cli/FrameDebuggerCliCommands.cs`:

```csharp
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
```

- [ ] **Step 3: Recompile and verify command registration**

Run: `unity command recompile` → `unity command recompile_status` (expect clean), then:
`unity command framedebugger_dump_status`
Expected: a JSON response with `"running": false` and `"message": "No dump has run yet."` — proves the define fired, the assembly compiled, and both commands registered.

- [ ] **Step 4: Run the full suite once more**

Run: `unity command run_tests --filter Bun3.Unity.Diagnostics`.
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add unity/Packages/com.bun3.unity.diagnostics
git commit -m "✨ framedebugger_dump CLI commands behind com.unity.pipeline define" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Out of scope / follow-ups

- Manual E2E (play mode + real scene capture) — per spec, verified by hand; the binding tests are the automated proxy.
- Removing `FrameDebuggerDump.cs` from the legacy game repo happens there when the package is adopted (spec 미결).
- Material-level interleave detection: the current key is shader name only; same-shader/different-material interleaves are not reported. Add a material field to `FrameEvent` if a mapped field turns out to exist on the target editor versions.
- 2022.3 in-situ verification: the binding tests must also be run once inside a 2022.3 project when the package is first installed into one (git URL), since this repo's editor is 6000.3.
