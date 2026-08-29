using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Bun3.Unity.Agents
{
    /// <summary>
    /// Process-tree operations on CLI sessions. Automatic transcript adoption used to
    /// live here and was removed in 0.7.0: heartbeats are written by the hooks whether
    /// or not any consumer app runs, so a live session always resurfaces on its own
    /// next hook event — while process-count guessing kept minting ghost workers
    /// (helper spawns, pty hosts, attach/agents utilities all share the CLI's exe).
    /// </summary>
    public static class SessionSync
    {
        /// <summary>
        /// Kills a session's whole CLI process tree: climbs to the topmost same-named
        /// ancestor (pty host, wrapper), then kills it and every descendant in the
        /// snapshot — the session, its MCP servers, its shells. A survivor (spawned
        /// after the snapshot, kill denied) re-registers on its next hook event, so a
        /// miss self-heals into reappearance rather than a ghost.
        /// </summary>
        public static void KillSessionTree(int pid)
        {
            string name;
            try
            {
                using var target = Process.GetProcessById(pid);
                name = target.ProcessName;
            }
            catch (Exception)
            {
                return; // already gone
            }

            var parents = SnapshotParentPids();
            var root = pid;
            for (var i = 0; i < 64 && parents.TryGetValue(root, out var pp) && pp > 0 && pp != root; i++)
            {
                try
                {
                    using var parent = Process.GetProcessById(pp);
                    if (parent.ProcessName != name)
                        break;
                }
                catch (Exception)
                {
                    break; // parent gone / inaccessible — root stays here
                }

                root = pp;
            }

            var children = new Dictionary<int, List<int>>();
            foreach (var kv in parents)
            {
                if (!children.TryGetValue(kv.Value, out var list))
                    children[kv.Value] = list = new List<int>();
                list.Add(kv.Key);
            }

            var stack = new Stack<int>();
            stack.Push(root);
            var seen = new HashSet<int>();
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (!seen.Add(cur))
                    continue;
                if (children.TryGetValue(cur, out var kids))
                {
                    foreach (var kid in kids)
                        stack.Push(kid);
                }

                try
                {
                    using var p = Process.GetProcessById(cur);
                    p.Kill();
                }
                catch (Exception)
                {
                    // exited already / access denied
                }
            }
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
        /// on failure) the empty map limits the kill to the given pid alone.</summary>
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
