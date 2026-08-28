using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace Bun3.Unity.Agents
{
    /// <summary>
    /// Workers for desktop AI apps that expose no hooks (Claude App, ChatGPT App):
    /// process presence creates the worker, CPU activity approximates Working, and
    /// process exit removes it. Writes the same heartbeat files as hook scripts, so
    /// the rest of the pipeline (display, sharing) needs nothing special.
    /// </summary>
    public sealed class DesktopAppAdapter : MonoBehaviour
    {
        private const float PollIntervalSeconds = 5f;
        private const double BusyCpuFraction = 0.08; // of one core over the poll window

        private sealed class Entry
        {
            public string ProcessName;
            public string[] PathHints; // separates the app from same-named CLIs (claude.exe!)
            public string WorkerId;
            public string Display;
            public TimeSpan LastCpu;
            public bool WasPresent;
            public int LastState = -1;
        }

        private Entry[] _entries;
        private float _nextPoll;

        private void Awake()
        {
            // every provider manifest with a process rule becomes a watched desktop app
            var entries = new List<Entry>();
            foreach (var def in ProviderRegistry.All)
            {
                if (!def.WatchesProcess)
                    continue;
                entries.Add(new Entry
                {
                    ProcessName = def.process.processName,
                    PathHints = def.process.pathHints ?? new[] { def.process.processName },
                    WorkerId = def.provider + "-desktop",
                    Display = def.displayName + " App",
                });
            }

            _entries = entries.ToArray();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var go = new GameObject(nameof(DesktopAppAdapter));
            DontDestroyOnLoad(go);
            go.AddComponent<DesktopAppAdapter>();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextPoll)
                return;
            _nextPoll = Time.unscaledTime + PollIntervalSeconds;

            foreach (var entry in _entries)
                PollEntry(entry);
        }

        private static string BeatPath(Entry entry) =>
            Path.Combine(AgentHeartbeatStore.HeartbeatDirectory, entry.WorkerId + ".json");

        private void PollEntry(Entry entry)
        {
            var totalCpu = TimeSpan.Zero;
            var present = false;
            var pid = 0;
            foreach (var process in Process.GetProcessesByName(entry.ProcessName))
            {
                try
                {
                    var path = process.MainModule.FileName;
                    var matched = false;
                    foreach (var hint in entry.PathHints)
                    {
                        if (path.Contains(hint))
                        {
                            matched = true;
                            break;
                        }
                    }

                    if (!matched)
                        continue;
                    present = true;
                    pid = process.Id;
                    totalCpu += process.TotalProcessorTime;
                }
                catch (Exception)
                {
                    // access denied / exited mid-poll — ignore this process
                }
                finally
                {
                    process.Dispose();
                }
            }

            if (!present)
            {
                if (entry.WasPresent)
                {
                    try
                    {
                        File.Delete(BeatPath(entry));
                    }
                    catch (IOException)
                    {
                    }
                }

                entry.WasPresent = false;
                entry.LastState = -1;
                return;
            }

            var busy = entry.WasPresent
                       && (totalCpu - entry.LastCpu).TotalSeconds / PollIntervalSeconds >= BusyCpuFraction;
            entry.LastCpu = totalCpu;
            entry.WasPresent = true;

            var state = busy ? 2 : 0;
            // rewrite on state change (fresh ts drives tap/wake); leave untouched while
            // idle so the sleep rule can kick in naturally
            if (state == entry.LastState && state == 0)
                return;
            entry.LastState = state;

            Directory.CreateDirectory(AgentHeartbeatStore.HeartbeatDirectory);
            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            File.WriteAllText(BeatPath(entry),
                $"{{\"id\":\"{entry.WorkerId}\",\"n\":\"{entry.Display}\",\"st\":{state},\"ts\":{ts},\"pid\":{pid}}}");
        }
    }
}
