using System;
using System.Collections.Generic;
using System.IO;

namespace Bun3.Unity.Agents
{
    /// <summary>
    /// The heartbeat directory: reads the current set of agent heartbeats and prunes
    /// files that carry no information (dead-owner corpses, orphan watch flags,
    /// half-finished writes). Time alone never removes a heartbeat — an agent leaves
    /// via its own file deletion, a dead owner pid, or the consumer hiding it.
    /// Consumers poll <see cref="ReadAndPrune"/> and reconcile into their own domain.
    /// </summary>
    public static class AgentHeartbeatStore
    {
        /// <summary>Tests point this at a temp directory; null = the real location.</summary>
        public static string DirectoryOverride;

        /// <summary>Protocol namespace shared by every consumer app — not per-app data.</summary>
        public static string HeartbeatDirectory =>
            DirectoryOverride
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "bun3-agents", "agents");

        /// <summary>Injectable for tests.</summary>
        public static Func<int, bool> ProcessAlive = pid =>
        {
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById(pid);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        };

        /// <summary>Removes an agent's heartbeat and watch flag — the consumer-initiated
        /// exit ("fire this worker"). A session still alive recreates its file on its
        /// next hook event, so removing a live one is self-healing, not destructive.</summary>
        public static void Remove(string id)
        {
            try
            {
                File.Delete(Path.Combine(HeartbeatDirectory, id + ".json"));
                File.Delete(Path.Combine(HeartbeatDirectory, id + ".watch"));
            }
            catch (IOException)
            {
            }
        }

        public static List<AgentHeartbeat> ReadAndPrune()
        {
            var entries = new List<(AgentHeartbeat hb, string file)>();
            var directory = HeartbeatDirectory;
            if (!Directory.Exists(directory))
                return new List<AgentHeartbeat>();
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (var file in Directory.GetFiles(directory, "*.watch"))
            {
                // watch flags from crashed sessions never get cleaned by SessionEnd
                if (nowMs - new DateTimeOffset(File.GetLastWriteTimeUtc(file)).ToUnixTimeMilliseconds() >= AgentHeartbeat.CrashedAfterMs)
                    File.Delete(file);
            }

            foreach (var file in Directory.GetFiles(directory, "*.json"))
            {
                try
                {
                    var hb = AgentHeartbeat.FromJson(File.ReadAllText(file));
                    if (hb == null)
                    {
                        // unparseable and old = write that never completed (session died mid-write)
                        if (nowMs - new DateTimeOffset(File.GetLastWriteTimeUtc(file)).ToUnixTimeMilliseconds() >= AgentHeartbeat.CrashedAfterMs)
                            File.Delete(file);
                        continue;
                    }

                    // Time NEVER removes a worker — but a dead owner process does.
                    // Sessions killed without a clean exit leave corpse files behind;
                    // hook scripts rewrite pid on every event, so a gone process means
                    // the session is over. Wrong once? A live session recreates its
                    // file on its next event. pid 0 (adopted / manual entries) is exempt.
                    if (hb.pid > 0 && !ProcessAlive(hb.pid))
                    {
                        File.Delete(file);
                        continue;
                    }

                    // pid -1 = unverified placeholder (legacy adoption, manual entries):
                    // a live session overwrites it with its next hook event; one that
                    // never speaks expires instead of living forever.
                    if (hb.pid == -1 && hb.IsCrashed(nowMs))
                    {
                        File.Delete(file);
                        continue;
                    }

                    entries.Add((hb, file));
                }
                catch (IOException)
                {
                    // mid-write; next scan picks it up
                }
            }

            // One CLI process hosts one session at a time — /clear and resume mint a
            // new session id inside the same process, and the replaced session never
            // gets a SessionEnd. Its heartbeat would pass the dead-pid check forever,
            // so among session heartbeats claiming the same live pid only the newest
            // survives. Chicks share their parent session's pid by design (p set).
            var newestByPid = new Dictionary<int, long>();
            foreach (var (hb, _) in entries)
            {
                if (hb.pid > 0 && string.IsNullOrEmpty(hb.p)
                    && (!newestByPid.TryGetValue(hb.pid, out var best) || hb.ts > best))
                    newestByPid[hb.pid] = hb.ts;
            }

            var result = new List<AgentHeartbeat>(entries.Count);
            foreach (var (hb, file) in entries)
            {
                if (hb.pid > 0 && string.IsNullOrEmpty(hb.p) && hb.ts < newestByPid[hb.pid])
                    File.Delete(file);
                else
                    result.Add(hb);
            }

            return result;
        }
    }
}
