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
