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
            public Candidate(string sessionId, string projectName, string dirName, long lastWriteMs)
            {
                SessionId = sessionId;
                ProjectName = projectName;
                DirName = dirName;
                LastWriteMs = lastWriteMs;
            }

            public string SessionId { get; }
            public string ProjectName { get; }

            /// <summary>The transcript directory name — the stable per-project dedup
            /// key. ProjectName is display-only: it falls back to this path-encoded
            /// mush when the transcript head has no cwd, so one project can wear two
            /// different display names.</summary>
            public string DirName { get; }

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
                    result.Add(new Candidate(Path.GetFileNameWithoutExtension(file), ProjectLeaf(file) ?? project, project, writeMs));
                }
            }

            result.Sort((a, b) => b.LastWriteMs.CompareTo(a.LastWriteMs));
            return result;
        }

        /// <summary>The session's real folder name from the transcript head (records carry
        /// a cwd field) — the directory name is a path-encoded mush like
        /// "E--Projects-handwrite-scanner". Null when no cwd is found.</summary>
        public static string ProjectLeaf(string file)
        {
            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var buf = new byte[Math.Min(65536, fs.Length)]; // the cwd field can sit past a huge first record
                var got = fs.Read(buf, 0, buf.Length);
                var head = System.Text.Encoding.UTF8.GetString(buf, 0, got);
                var match = System.Text.RegularExpressions.Regex.Match(head, "\"cwd\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                if (!match.Success)
                    return null;
                var cwd = System.Text.RegularExpressions.Regex.Unescape(match.Groups[1].Value);
                var leaf = Path.GetFileName(cwd.TrimEnd('\\', '/'));
                return string.IsNullOrEmpty(leaf) ? null : leaf;
            }
            catch (Exception)
            {
                return null;
            }
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
