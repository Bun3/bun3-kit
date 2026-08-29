using System;
using System.Collections.Generic;

namespace Bun3.Unity.Diagnostics
{
    /// <summary>Per-frame stats captured from the profiler buffer.</summary>
    [Serializable]
    public sealed class ProfilerFrameStat
    {
        /// <summary>Profiler frame index.</summary>
        public int frameIndex;

        /// <summary>Main-thread CPU frame time in milliseconds.</summary>
        public float cpuMs;

        /// <summary>GPU frame time in milliseconds; 0 when unavailable.</summary>
        public float gpuMs;

        /// <summary>Render-thread total time in milliseconds; 0 when the thread was not found.</summary>
        public float renderThreadMs;

        /// <summary>GC allocation in bytes during the frame (main thread).</summary>
        public long gcAllocBytes;
    }

    /// <summary>One marker's totals within a single frame, merged by name (main thread).</summary>
    [Serializable]
    public sealed class MarkerSample
    {
        /// <summary>Profiler marker name.</summary>
        public string name = "";

        /// <summary>Self time in milliseconds within the frame.</summary>
        public float selfMs;

        /// <summary>Call count within the frame.</summary>
        public int calls;

        /// <summary>GC allocation in bytes within the frame.</summary>
        public long gcAllocBytes;
    }

    /// <summary>One captured frame: frame-level stats plus its main-thread marker samples.</summary>
    public sealed class ProfilerFrameSample
    {
        /// <summary>Frame-level stats.</summary>
        public ProfilerFrameStat stat = new ProfilerFrameStat();

        /// <summary>Marker samples merged by name within the frame.</summary>
        public List<MarkerSample> markers = new List<MarkerSample>();
    }
}
