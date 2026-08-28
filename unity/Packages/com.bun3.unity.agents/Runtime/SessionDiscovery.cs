using System;
using System.Collections.Generic;
using System.IO;

namespace Bun3.Unity.Agents
{
    /// <summary>
    /// Finds CLI sessions from their transcript files (&lt;root&gt;/&lt;project&gt;/&lt;sessionId&gt;.jsonl).
    /// A recent transcript means a session existed lately even if it never emitted a hook
    /// event since our heartbeat was cleaned — the worker list offers these for adoption.
    /// Which providers have transcripts, and how an exited one looks, comes from each
    /// manifest's "discovery" rule.
    /// </summary>
    public static class SessionDiscovery
    {
        public readonly struct Candidate
        {
            public Candidate(string sessionId, string projectName, long lastWriteMs)
            {
                SessionId = sessionId;
                ProjectName = projectName;
                LastWriteMs = lastWriteMs;
            }

            public string SessionId { get; }
            public string ProjectName { get; }
            public long LastWriteMs { get; }
        }

        public static List<Candidate> FindRecent(string root, long nowMs, long maxAgeMs, string exitMarker = null)
        {
            var result = new List<Candidate>();
            if (!Directory.Exists(root))
                return result;

            foreach (var dir in Directory.GetDirectories(root))
            {
                var project = Path.GetFileName(dir);
                foreach (var file in Directory.GetFiles(dir, "*.jsonl"))
                {
                    var writeMs = new DateTimeOffset(File.GetLastWriteTimeUtc(file)).ToUnixTimeMilliseconds();
                    if (nowMs - writeMs > maxAgeMs || EndedCleanly(file, exitMarker))
                        continue;
                    result.Add(new Candidate(Path.GetFileNameWithoutExtension(file), project, writeMs));
                }
            }

            result.Sort((a, b) => b.LastWriteMs.CompareTo(a.LastWriteMs));
            return result;
        }

        /// <summary>An exited session must not be offered for adoption. The manifest's
        /// exitMarker names the last-line prefix the CLI flushes on exit (Claude Code:
        /// a cost-state record; live sessions, idle included, end mid-conversation).</summary>
        public static bool EndedCleanly(string file, string exitMarker)
        {
            if (string.IsNullOrEmpty(exitMarker))
                return false;
            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var read = (int)Math.Min(2048, fs.Length);
                if (read == 0)
                    return false;
                fs.Seek(-read, SeekOrigin.End);
                var buf = new byte[read];
                var got = fs.Read(buf, 0, read);
                var text = System.Text.Encoding.UTF8.GetString(buf, 0, got).TrimEnd('\r', '\n', ' ');
                var lastLine = text.Substring(text.LastIndexOf('\n') + 1);
                return lastLine.StartsWith(exitMarker);
            }
            catch (IOException)
            {
                return false;
            }
        }
    }
}
