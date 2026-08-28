using System;
using UnityEngine;

namespace Bun3.Unity.Agents
{
    /// <summary>
    /// One agent's heartbeat file: %LOCALAPPDATA%/bun3-agents/agents/&lt;id&gt;.json.
    /// Any local AI agent (Claude Code hook, Codex wrapper, ...) writes
    /// {"id":"...","n":"Claude","st":1,"ts":1724650000000} to appear as a worker.
    /// </summary>
    [Serializable]
    public sealed class AgentHeartbeat
    {
        /// <summary>
        /// Deep-quiet threshold: past this, a worker sleeps regardless of its last
        /// state, and leftover files that carry no information (orphan watch flags,
        /// half-written json) get cleaned. Time never removes a heartbeat — workers
        /// leave via SessionEnd, the user hiding them, or their owner pid dying.
        /// </summary>
        public const long CrashedAfterMs = 15 * 60 * 1000;

        public string id;
        public string n;
        public int st;
        public long ts;
        public string p;   // parent worker id — set for subagent chicks
        public int pid;    // session process id (informational)

        public bool IsCrashed(long nowMs) => nowMs - ts >= CrashedAfterMs;

        public static AgentHeartbeat FromJson(string json)
        {
            try
            {
                var hb = JsonUtility.FromJson<AgentHeartbeat>(json);
                return string.IsNullOrEmpty(hb?.id) ? null : hb;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
