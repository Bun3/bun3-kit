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

        /// <summary>Pre-0.2 namespace. Sessions whose hooks were registered before the
        /// rename keep writing here until they restart — read (and prune) both so they
        /// stay visible through the migration.</summary>
        private static string LegacyDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ai-office", "agents");

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

        public static List<AgentHeartbeat> ReadAndPrune()
        {
            var result = new List<AgentHeartbeat>();
            ReadDirectory(HeartbeatDirectory, result);
            if (DirectoryOverride == null && LegacyDirectory != HeartbeatDirectory)
                ReadDirectory(LegacyDirectory, result);
            return result;
        }

        private static void ReadDirectory(string directory, List<AgentHeartbeat> result)
        {
            if (!Directory.Exists(directory))
                return;
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

                    result.Add(hb);
                }
                catch (IOException)
                {
                    // mid-write; next scan picks it up
                }
            }
        }
    }
}
