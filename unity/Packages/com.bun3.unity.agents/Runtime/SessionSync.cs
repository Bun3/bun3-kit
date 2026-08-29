using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace Bun3.Unity.Agents
{
    /// <summary>
    /// Closes the desync window between reality and the heartbeat store: a session
    /// already running when the consumer app starts stays invisible until its next
    /// hook event. Sync counts the provider's live CLI process TREES not accounted
    /// for by any heartbeat pid, and provisionally registers that many of the newest
    /// unregistered transcript sessions — zero unaccounted trees means zero
    /// registrations, so corpses never enter. Provisional entries (pid -1) are
    /// confirmed by the session's next hook event or expire.
    /// </summary>
    public static class SessionSync
    {
        public const long TranscriptWindowMs = 6 * 60 * 60 * 1000;

        /// <summary>One live CLI-named process: its pid, parent pid (0 = unknown),
        /// and start time. The parent link is what lets the counter collapse a
        /// session's helper spawns into one tree.</summary>
        public readonly struct ProcInfo
        {
            public ProcInfo(int pid, int parentPid, long startUnixMs)
            {
                Pid = pid;
                ParentPid = parentPid;
                StartUnixMs = startUnixMs;
            }

            public int Pid { get; }
            public int ParentPid { get; }
            public long StartUnixMs { get; }
        }

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
                    pendingProvisionals++; // presumed to cover one unaccounted tree until it confirms or expires
            }

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // Idempotency: pending provisionals count against the gap, so re-syncing
            // while they await confirmation adopts nothing more.
            var missing = CountUnaccountedSessionTrees(CensusCliProcesses(def), accountedPids, nowMs) - pendingProvisionals;
            if (missing <= 0)
                return 0;

            var root = ProviderHookInstaller.ExpandHome(def.discovery.transcriptRoot);
            var adopted = 0;
            // one candidate per project per pass — a hard-killed pile shares one project,
            // and adopting several of its corpses to fill the count just makes ghosts.
            // Keyed on the transcript directory: the display name falls back to the
            // path-encoded dir when no cwd is found, which would let a project through
            // twice under two spellings. Tombstoned ids already failed to confirm once:
            // never again.
            var adoptedProjects = new HashSet<string>();
            var tombstones = AgentHeartbeatStore.ReadTombstones();
            foreach (var candidate in SessionDiscovery.FindRecent(root, nowMs, TranscriptWindowMs, def.discovery.exitMarker))
            {
                if (adopted >= missing)
                    break;
                var id = def.provider + "-" + candidate.SessionId;
                if (registeredIds.Contains(id) || tombstones.Contains(id) || !adoptedProjects.Add(candidate.DirName))
                    continue;
                AgentHeartbeatStore.WriteProvisional(id, candidate.ProjectName);
                adopted++;
            }

            return adopted;
        }

        /// <summary>
        /// Counts the session process trees no heartbeat accounts for. The CLI runs as
        /// a small tree (wrapper → session, daemon → pty host → session) while hooks
        /// record a single pid somewhere inside it, so accounting is per tree, not per
        /// process — counting processes mints one ghost per helper spawn. A tree with
        /// no heartbeat and no member younger than the transcript window is a long-idle
        /// stranger (pre-hook session, forgotten shell): it has no recent transcript of
        /// its own to adopt, so counting it would only ghost other projects' corpses.
        /// </summary>
        public static int CountUnaccountedSessionTrees(List<ProcInfo> processes, HashSet<int> accountedPids, long nowMs)
        {
            var byPid = new Dictionary<int, ProcInfo>();
            foreach (var p in processes)
                byPid[p.Pid] = p;

            var trees = new Dictionary<int, (bool accounted, bool recent)>();
            foreach (var p in processes)
            {
                var cur = p;
                // ponytail: a stale/reused ppid can merge unrelated trees — that only
                // undercounts (fewer adoptions), never ghosts. Bounded against cycles.
                for (var i = 0; i < 64 && byPid.TryGetValue(cur.ParentPid, out var parent) && parent.Pid != cur.Pid; i++)
                    cur = parent;
                trees.TryGetValue(cur.Pid, out var t);
                trees[cur.Pid] = (t.accounted || accountedPids.Contains(p.Pid),
                                  t.recent || nowMs - p.StartUnixMs <= TranscriptWindowMs);
            }

            return trees.Count(kv => !kv.Value.accounted && kv.Value.recent);
        }

        /// <summary>Live processes bearing this provider's CLI name, with parent links.
        /// The name alone overcounts: the CLI shares it with the desktop app
        /// (claude.exe!) — excluded via the manifest's path hints — and with its own
        /// helper spawns, which the parent links collapse into trees.</summary>
        private static List<ProcInfo> CensusCliProcesses(ProviderDef def)
        {
            var processName = def.process != null && !string.IsNullOrEmpty(def.process.processName)
                ? def.process.processName
                : def.provider;
            var parents = SnapshotParentPids();
            var result = new List<ProcInfo>();
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    if (def.WatchesProcess && def.process.pathHints != null)
                    {
                        var path = process.MainModule.FileName;
                        if (def.process.pathHints.Any(hint => path.Contains(hint)))
                            continue; // the desktop app, tracked by DesktopAppAdapter
                    }

                    var startMs = new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeMilliseconds();
                    result.Add(new ProcInfo(process.Id, parents.TryGetValue(process.Id, out var pp) ? pp : 0, startMs));
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

            return result;
        }

        private const uint TH32CS_SNAPPROCESS = 0x2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        /// <summary>pid → parent pid for every process. Windows-only; anywhere else (or
        /// on failure) the empty map degrades tree grouping to one process per tree —
        /// the pre-tree behavior.</summary>
        private static Dictionary<int, int> SnapshotParentPids()
        {
            var map = new Dictionary<int, int>();
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                return map;
            var snapshot = IntPtr.Zero;
            try
            {
                snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
                if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
                    return map;
                var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
                if (!Process32First(snapshot, ref entry))
                    return map;
                do
                {
                    map[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
                }
                while (Process32Next(snapshot, ref entry));
            }
            catch (Exception)
            {
                map.Clear();
            }
            finally
            {
                if (snapshot != IntPtr.Zero && snapshot != new IntPtr(-1))
                    CloseHandle(snapshot);
            }

            return map;
        }
    }
}
