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

        /// <summary>Ids of provisional registrations that expired unconfirmed — corpses
        /// sync must never adopt again. (A session that later proves alive registers
        /// itself through its hooks regardless.)</summary>
        public static string TombstonePath =>
            Path.Combine(HeartbeatDirectory, "adoption-tombstones.txt");

        public static HashSet<string> ReadTombstones()
        {
            var result = new HashSet<string>();
            try
            {
                if (File.Exists(TombstonePath))
                {
                    foreach (var line in File.ReadAllLines(TombstonePath))
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            result.Add(line.Trim());
                    }
                }
            }
            catch (IOException)
            {
            }

            return result;
        }

        /// <summary>Registers a session we could not verify (pid -1): the real session
        /// overwrites this on its next hook event; a corpse's placeholder expires after
        /// the deep-quiet threshold instead of living forever.</summary>
        public static void WriteProvisional(string id, string name)
        {
            Directory.CreateDirectory(HeartbeatDirectory);
            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            File.WriteAllText(Path.Combine(HeartbeatDirectory, id + ".json"),
                $"{{\"id\":\"{id}\",\"n\":\"{name}\",\"st\":0,\"ts\":{ts},\"pid\":-1}}");
        }

        public static List<AgentHeartbeat> ReadAndPrune()
        {
            var result = new List<AgentHeartbeat>();
            var directory = HeartbeatDirectory;
            if (!Directory.Exists(directory))
                return result;
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

                    // pid -1 = unverified adoption placeholder: a live session overwrites
                    // it with its next hook event; one that never speaks was a corpse.
                    // Tombstone it so sync never re-adopts the same corpse.
                    if (hb.pid == -1 && hb.IsCrashed(nowMs))
                    {
                        File.Delete(file);
                        File.AppendAllText(TombstonePath, hb.id + "\n");
                        continue;
                    }

                    result.Add(hb);
                }
                catch (IOException)
                {
                    // mid-write; next scan picks it up
                }
            }

            return result;
        }
    }
}
