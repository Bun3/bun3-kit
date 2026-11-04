using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Telemetry.Tests
{
    public class LocalJsonlSinkTests
    {
        static readonly DateTime Start = new(2026, 10, 29, 1, 2, 3, DateTimeKind.Utc);
        string dir;

        [SetUp] public void SetUp() =>
            dir = Path.Combine(Application.temporaryCachePath, "telemetry-tests", Guid.NewGuid().ToString("N"));

        [TearDown] public void TearDown()
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        static TelemetryRecord Rec(long seq) => new()
        {
            Name = "e", Category = TelemetryCategory.Ui, Utc = Start, Seq = seq,
            Props = Array.Empty<(string, object)>(),
        };

        [Test] public void WritesOneJsonLinePerRecord()
        {
            var sink = new LocalJsonlSink(dir, "run", Start, 5, 100000);
            sink.Write(Rec(1));
            sink.Write(Rec(2));
            sink.FlushAsync(TimeSpan.FromSeconds(1));
            var lines = File.ReadAllLines(sink.FilePath);
            Assert.AreEqual(2, lines.Length);
            foreach (var l in lines) Assert.AreEqual('{', l[0]);
        }

        [Test] public void KeepsOnlyNewestRuns()
        {
            Directory.CreateDirectory(dir);
            for (var i = 1; i <= 5; i++) File.WriteAllText(Path.Combine(dir, Cysharp.Text.ZString.Concat("2026010", i, "T000000Z-old.jsonl")), "");
            File.WriteAllText(Path.Combine(dir, "notes.jsonl"), "");
            File.WriteAllText(Path.Combine(dir, "readme.txt"), "");
            var sink = new LocalJsonlSink(dir, "run", Start, 3, 100000);
            Assert.IsTrue(File.Exists(Path.Combine(dir, "notes.jsonl")));
            Assert.IsTrue(File.Exists(Path.Combine(dir, "readme.txt")));
            var files = Array.FindAll(Directory.GetFiles(dir), f => f.EndsWith("Z-old.jsonl") || f == sink.FilePath);
            Assert.AreEqual(3, files.Length);
            CollectionAssert.Contains(files, sink.FilePath);
            Assert.IsTrue(File.Exists(Path.Combine(dir, "20260105T000000Z-old.jsonl")));
            Assert.IsFalse(File.Exists(Path.Combine(dir, "20260101T000000Z-old.jsonl")));
        }

        [Test] public void StopsAtSizeCapWithMarker()
        {
            var sink = new LocalJsonlSink(dir, "run", Start, 5, 200);
            for (var i = 0; i < 10; i++) sink.Write(Rec(i));
            sink.FlushAsync(TimeSpan.FromSeconds(1));
            sink.Write(Rec(99));
            sink.FlushAsync(TimeSpan.FromSeconds(1));
            var lines = File.ReadAllLines(sink.FilePath);
            Assert.AreEqual("{\"event\":\"log_truncated\"}", lines[^1]);
            Assert.Less(lines.Length, 10);
            StringAssert.DoesNotContain("\"seq\":99", string.Join("\n", lines));
        }

        [Test] public void ReadTailIncludesUnflushedLines()
        {
            var sink = new LocalJsonlSink(dir, "run", Start, 5, 100000);
            sink.Write(Rec(1));
            sink.Write(Rec(2));
            var text = System.Text.Encoding.UTF8.GetString(sink.ReadTail(100000));
            Assert.AreEqual(2, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        }

        [Test] public void ReadTailTrimsAtLineBoundary()
        {
            var sink = new LocalJsonlSink(dir, "run", Start, 5, 100000);
            for (var i = 1; i <= 20; i++) sink.Write(Rec(i));
            sink.FlushAsync(TimeSpan.FromSeconds(1));
            sink.Write(Rec(21));
            var text = System.Text.Encoding.UTF8.GetString(sink.ReadTail(300));
            Assert.LessOrEqual(System.Text.Encoding.UTF8.GetByteCount(text), 300);
            Assert.AreEqual('{', text[0]);
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Greater(lines.Length, 0);
            Assert.Less(lines.Length, 21);
            StringAssert.Contains("\"seq\":21", lines[lines.Length - 1]);
        }

        [Test] public void ReadTailWhileWritingNeverThrowsOrTearsLines()
        {
            var sink = new LocalJsonlSink(dir, "run", Start, 5, 100000000);
            var done = false;
            Exception error = null;
            var chunks = new System.Collections.Generic.List<byte[]>();
            var reader = System.Threading.Tasks.Task.Run(() =>
            {
                try { while (!System.Threading.Volatile.Read(ref done)) chunks.Add(sink.ReadTail(4096)); }
                catch (Exception e) { error = e; }
            });
            for (var i = 1; i <= 5000; i++)
            {
                sink.Write(Rec(i));
                if (i % 50 == 0) sink.FlushAsync(TimeSpan.FromSeconds(1));
            }
            System.Threading.Volatile.Write(ref done, true);
            reader.Wait();
            Assert.IsNull(error);
            Assert.Greater(chunks.Count, 0);
            foreach (var c in chunks)
                foreach (var l in System.Text.Encoding.UTF8.GetString(c).Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    Assert.IsTrue(l[0] == '{' && l[l.Length - 1] == '}', l);
        }
    }
}
