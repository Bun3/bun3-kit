using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Bun3.Unity.Agents
{
    /// <summary>
    /// Closes the desync window between reality and the heartbeat store: a session
    /// already running when the consumer app starts stays invisible until its next
    /// hook event. Sync counts the provider's live CLI processes not accounted for
    /// by any heartbeat pid, and provisionally registers that many of the newest
    /// unregistered transcript sessions — zero unaccounted processes means zero
    /// registrations, so corpses never enter. Provisional entries (pid -1) are
    /// confirmed by the session's next hook event or expire.
    /// </summary>
    public static class SessionSync
    {
        public const long TranscriptWindowMs = 6 * 60 * 60 * 1000;

        /// <summary>Runs one sync pass over every provider with a discovery rule.
        /// Returns the number of sessions provisionally registered.</summary>
        public static int SyncNow()
        {
            var heartbeats = AgentHeartbeatStore.ReadAndPrune();
            var adopted = 0;
            foreach (var def in ProviderRegistry.All)
            {
                if (def.DiscoversSessions)
                    adopted += SyncProvider(def, heartbeats);
            }

            return adopted;
        }

        private static int SyncProvider(ProviderDef def, List<AgentHeartbeat> heartbeats)
        {
            var accountedPids = new HashSet<int>();
            var registeredIds = new HashSet<string>();
            var pendingProvisionals = 0;
            foreach (var hb in heartbeats)
            {
                if (!hb.id.StartsWith(def.provider + "-"))
                    continue;
                registeredIds.Add(hb.id);
                if (hb.pid > 0)
                    accountedPids.Add(hb.pid);
                else if (hb.pid == -1)
                    pendingProvisionals++; // presumed to cover one unaccounted process until it confirms or expires
            }

            // Idempotency: pending provisionals count against the gap, so re-syncing
            // while they await confirmation adopts nothing more.
            var missing = CountUnaccountedCliProcesses(def, accountedPids) - pendingProvisionals;
            if (missing <= 0)
                return 0;

            var root = ProviderHookInstaller.ExpandHome(def.discovery.transcriptRoot);
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var adopted = 0;
            // one candidate per project per pass — a hard-killed pile shares one project,
            // and adopting several of its corpses to fill the count just makes ghosts
            var adoptedProjects = new HashSet<string>();
            foreach (var candidate in SessionDiscovery.FindRecent(root, nowMs, TranscriptWindowMs, def.discovery.exitMarker))
            {
                if (adopted >= missing)
                    break;
                var id = def.provider + "-" + candidate.SessionId;
                if (registeredIds.Contains(id) || !adoptedProjects.Add(candidate.ProjectName))
                    continue;
                AgentHeartbeatStore.WriteProvisional(id, candidate.ProjectName);
                adopted++;
            }

            return adopted;
        }

        /// <summary>Live CLI processes of this provider whose pid no heartbeat claims.
        /// The CLI often shares its process name with the desktop app (claude.exe!) —
        /// processes matching the manifest's desktop path hints are excluded.</summary>
        private static int CountUnaccountedCliProcesses(ProviderDef def, HashSet<int> accountedPids)
        {
            var processName = def.process != null && !string.IsNullOrEmpty(def.process.processName)
                ? def.process.processName
                : def.provider;
            var count = 0;
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    if (accountedPids.Contains(process.Id))
                        continue;
                    if (def.WatchesProcess && def.process.pathHints != null)
                    {
                        var path = process.MainModule.FileName;
                        if (def.process.pathHints.Any(hint => path.Contains(hint)))
                            continue; // the desktop app, tracked by DesktopAppAdapter
                    }

                    count++;
                }
                catch (Exception)
                {
                    // access denied / exited mid-scan — not countable, skip
                }
                finally
                {
                    process.Dispose();
                }
            }

            return count;
        }
    }
}
