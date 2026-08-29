using System.Collections.Generic;
using UnityEditor.Profiling;
using UnityEditorInternal;

namespace Bun3.Unity.Diagnostics
{
    // Reads the ProfilerDriver frame buffer into plain data. Main-thread markers are flattened by
    // merged sample name; the render thread contributes only its per-frame total time. All reads
    // are synchronous — the buffer holds already-collected frames.
    internal static class ProfilerCapture
    {
        const int MainThreadIndex = 0;
        const int MaxThreadProbe = 32;
        const string RenderThreadName = "Render Thread";

        internal static int FrameCount =>
            ProfilerDriver.firstFrameIndex < 0 ? 0 : ProfilerDriver.lastFrameIndex - ProfilerDriver.firstFrameIndex + 1;

        internal static bool HasFrames => FrameCount > 0;

        internal static List<ProfilerFrameSample> ReadAll()
        {
            var samples = new List<ProfilerFrameSample>();
            var scratch = new List<int>();
            int renderThreadIndex = -1; // resolved lazily on the first frame that finds it

            for (int frame = ProfilerDriver.firstFrameIndex; frame <= ProfilerDriver.lastFrameIndex; frame++)
            {
                using (var view = OpenView(frame, MainThreadIndex))
                {
                    if (view == null || !view.valid)
                        continue;

                    var sample = new ProfilerFrameSample();
                    sample.stat.frameIndex = frame;
                    sample.stat.cpuMs = view.frameTimeMs;
                    sample.stat.gpuMs = view.frameGpuTimeMs;
                    int root = view.GetRootItemID();
                    sample.stat.gcAllocBytes = (long)view.GetItemColumnDataAsFloat(root, HierarchyFrameDataView.columnGcMemory);
                    CollectMarkers(view, root, scratch, sample.markers);
                    sample.stat.renderThreadMs = ReadRenderThreadMs(frame, ref renderThreadIndex);
                    samples.Add(sample);
                }
            }

            return samples;
        }

        static HierarchyFrameDataView OpenView(int frame, int threadIndex) =>
            ProfilerDriver.GetHierarchyFrameDataView(
                frame, threadIndex, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                HierarchyFrameDataView.columnSelfTime, false);

        static void CollectMarkers(HierarchyFrameDataView view, int root, List<int> scratch, List<MarkerSample> markers)
        {
            var byName = new Dictionary<string, MarkerSample>();
            var stack = new Stack<int>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                int id = stack.Pop();
                scratch.Clear();
                view.GetItemChildren(id, scratch);
                foreach (int child in scratch)
                {
                    stack.Push(child);
                    string name = view.GetItemName(child);
                    if (string.IsNullOrEmpty(name))
                        continue;
                    if (!byName.TryGetValue(name, out var m))
                        byName[name] = m = new MarkerSample { name = name };
                    m.selfMs += view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnSelfTime);
                    m.calls += (int)view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnCalls);
                    m.gcAllocBytes += (long)view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnGcMemory);
                }
            }

            markers.AddRange(byName.Values);
        }

        static float ReadRenderThreadMs(int frame, ref int renderThreadIndex)
        {
            if (renderThreadIndex >= 0)
            {
                using (var view = OpenView(frame, renderThreadIndex))
                    return view != null && view.valid
                        ? view.GetItemColumnDataAsFloat(view.GetRootItemID(), HierarchyFrameDataView.columnTotalTime)
                        : 0f;
            }

            for (int ti = 1; ti < MaxThreadProbe; ti++)
            {
                using (var view = OpenView(frame, ti))
                {
                    if (view == null || !view.valid)
                        return 0f;
                    if (view.threadName != RenderThreadName)
                        continue;
                    renderThreadIndex = ti;
                    return view.GetItemColumnDataAsFloat(view.GetRootItemID(), HierarchyFrameDataView.columnTotalTime);
                }
            }

            return 0f;
        }
    }
}
