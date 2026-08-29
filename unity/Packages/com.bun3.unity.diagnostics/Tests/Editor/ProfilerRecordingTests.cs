using System.Collections;
using NUnit.Framework;
using UnityEditorInternal;
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
            }
            finally
            {
                ProfilerDriver.enabled = prevEnabled;
                ProfilerDriver.profileEditor = prevProfileEditor;
            }
        }
    }
}
