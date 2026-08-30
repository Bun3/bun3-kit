using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Diagnostics.Editor.Tests
{
    public class ProfilerRecordingTests
    {
        [UnityTest]
        public IEnumerator RecordAndRead_CapturesEditorFrames()
        {
            bool prevEnabled = ProfilerDriver.enabled;
            bool prevProfileEditor = ProfilerDriver.profileEditor;
            ProfilerDriver.profileEditor = true;
            try
            {
                var task = ProfilerDumper.RecordAsync(3);
                int guard = 0;
                while (!task.IsCompleted && guard++ < 3000)
                {
                    InternalEditorUtility.RepaintAllViews();
                    yield return null;
                }

                Assert.IsTrue(task.IsCompleted, "recording did not finish");
                Assert.Greater(task.Result, 0, "no frames captured");
                var frames = ProfilerCapture.ReadAll();
                Assert.IsNotEmpty(frames);
                Assert.IsNotEmpty(frames[0].markers, "main-thread markers empty");

                // TEMPORARY (finding 1 verification): self-GC marker sum vs the frame's
                // subtree-inclusive root GC total should be approximately equal after the fix.
                foreach (var f in frames)
                {
                    long markerSum = f.markers.Sum(m => m.gcAllocBytes);
                    Debug.Log($"[gc-verify] frame {f.stat.frameIndex}: rootGc={f.stat.gcAllocBytes} markerSum={markerSum}");
                    if (f.stat.gcAllocBytes > 0)
                        Assert.Greater(markerSum, 0, "marker GC sum should be positive when root GC is positive");
                    Assert.LessOrEqual(markerSum, f.stat.gcAllocBytes * 1.05 + 64,
                        "marker GC sum should not exceed the frame's inclusive root GC total (self-GC, not double-counted)");
                }
            }
            finally
            {
                ProfilerDriver.enabled = prevEnabled;
                ProfilerDriver.profileEditor = prevProfileEditor;
            }
        }
    }
}
