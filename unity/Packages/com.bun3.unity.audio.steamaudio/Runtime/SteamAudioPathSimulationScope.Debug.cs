// Optional bounded native path-validation diagnostics.
using System;
using System.Runtime.InteropServices;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio
{
    /// <summary>A native validation segment; not an ordered final route or a path-length measurement.</summary>
    public readonly struct SteamAudioPathDebugSegment
    {
        /// <summary>Gets the native starting probe position.</summary>
        public SA.Vector3 From { get; }
        /// <summary>Gets the native ending probe position.</summary>
        public SA.Vector3 To { get; }
        /// <summary>Gets whether validation rejected this segment as occluded.</summary>
        public bool Occluded { get; }
        internal SteamAudioPathDebugSegment(SA.Vector3 from, SA.Vector3 to, bool occluded)
        { From = from; To = to; Occluded = occluded; }
    }

    public sealed partial class SteamAudioPathSimulationScope
    {
        sealed class DebugCapture
        {
            internal readonly SteamAudioPathDebugSegment[] Segments = new SteamAudioPathDebugSegment[4096];
            internal int Count, Dropped;
            internal void Clear() { lock (Segments) { Count = Dropped = 0; } }
        }
        static readonly SA.PathingVisualizationCallback DebugCallback = CaptureDebugSegment;
        DebugCapture debugCapture;
        GCHandle debugRoot;

        /// <summary>Enables native validation diagnostics. Segments aggregate all active sources and may include rejected candidate paths.</summary>
        public void SetPathDebugEnabled(bool enabled)
        {
            EnsureUsable();
            if (enabled && debugCapture == null)
            {
                debugCapture = new DebugCapture();
                debugRoot = GCHandle.Alloc(debugCapture);
            }
            shared.VisualizationCallback = enabled ? Marshal.GetFunctionPointerForDelegate(DebugCallback) : IntPtr.Zero;
            shared.UserData = enabled ? GCHandle.ToIntPtr(debugRoot) : IntPtr.Zero;
            if (!enabled) debugCapture?.Clear();
        }

        /// <summary>Copies the most recent run's validation segments. Dropped includes capacity truncation; no route ordering is implied.</summary>
        public int CopyPathDebugSegments(Span<SteamAudioPathDebugSegment> destination, out int dropped)
        {
            EnsureUsable(); dropped = 0;
            if (debugCapture == null) return 0;
            lock (debugCapture.Segments)
            {
                int count = Math.Min(destination.Length, debugCapture.Count);
                debugCapture.Segments.AsSpan(0, count).CopyTo(destination);
                dropped = debugCapture.Dropped + debugCapture.Count - count;
                return count;
            }
        }

        [AOT.MonoPInvokeCallback(typeof(SA.PathingVisualizationCallback))]
        static void CaptureDebugSegment(SA.Vector3 from, SA.Vector3 to, SA.Bool occluded, IntPtr userData)
        {
            var capture = (DebugCapture)GCHandle.FromIntPtr(userData).Target;
            lock (capture.Segments)
            {
                if (capture.Count == capture.Segments.Length) { capture.Dropped++; return; }
                capture.Segments[capture.Count++] = new SteamAudioPathDebugSegment(from, to, occluded != SA.Bool.False);
            }
        }
    }
}
