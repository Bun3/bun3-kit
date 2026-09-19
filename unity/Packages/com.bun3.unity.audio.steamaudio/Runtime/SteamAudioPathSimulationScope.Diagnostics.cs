// Optional native final-path diagnostics; independent of validation-ray visualization.
using System;
using System.Runtime.InteropServices;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio
{
    /// <summary>One actual SH path contribution, copied before native path summation. Coordinates use the native frame.</summary>
    public readonly struct SteamAudioPathDiagnostic
    {
        /// <summary>Gets the physical source position.</summary>
        public SA.Vector3 Source { get; }
        /// <summary>Gets the listener position.</summary>
        public SA.Vector3 Listener { get; }
        /// <summary>Gets the virtual source used to calculate the SH direction.</summary>
        public SA.Vector3 VirtualSource { get; }
        /// <summary>Gets the distance passed to the attenuation model, not an endpoint-inclusive polyline length.</summary>
        public float Distance { get; }
        /// <summary>Gets the attenuation model's gain.</summary>
        public float DistanceGain { get; }
        /// <summary>Gets the interpolation weight before distance attenuation and diffraction EQ.</summary>
        public float Weight { get; }
        /// <summary>Gets the number of retained probe vertices. Zero denotes a direct path.</summary>
        public int PointCount { get; }
        /// <summary>Gets whether the native or managed vertex buffer omitted vertices.</summary>
        public bool Truncated { get; }
        internal readonly int Offset;
        internal SteamAudioPathDiagnostic(SA.Vector3 source, SA.Vector3 listener, SA.Vector3 virtualSource,
            float distance, float gain, float weight, int offset, int count, bool truncated)
        {
            Source = source; Listener = listener; VirtualSource = virtualSource;
            Distance = distance; DistanceGain = gain; Weight = weight;
            Offset = offset; PointCount = count; Truncated = truncated;
        }
    }

    public sealed partial class SteamAudioPathSimulationScope
    {
        [StructLayout(LayoutKind.Sequential)]
        struct NativeDiagnostic
        {
            internal SA.Vector3 Source, Listener, VirtualSource;
            internal float Distance, Gain, Weight;
            internal int Count, Truncated;
            internal IntPtr Points, SourceUserData;
        }
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate void NativeDiagnosticCallback(ref NativeDiagnostic value);
        [DllImport("phonon", CallingConvention = CallingConvention.Winapi)]
        static extern int bun3SteamAudioSetPathDiagnosticCallbackV1(IntPtr callback);
        static readonly NativeDiagnosticCallback DiagnosticCallback = CaptureDiagnostic;
        static readonly IntPtr DiagnosticPointer = Marshal.GetFunctionPointerForDelegate(DiagnosticCallback);
        sealed class DiagnosticCapture
        {
            internal readonly SteamAudioPathDiagnostic[] Paths = new SteamAudioPathDiagnostic[64];
            internal readonly float[] Points = new float[4096 * 3];
            internal int Count, PointCount, Dropped;
            internal void Clear() { Count = PointCount = Dropped = 0; }
        }
        bool diagnosticsEnabled;
        bool? nativeDiagnosticsSupported;

        /// <summary>Gets whether the loaded native library exports Bun3 diagnostic ABI v1. Official SDKs return false.</summary>
        public bool SupportsPathDiagnostics
        {
            get
            {
                EnsureUsable();
                if (!nativeDiagnosticsSupported.HasValue)
                {
                    try { nativeDiagnosticsSupported = bun3SteamAudioSetPathDiagnosticCallbackV1(IntPtr.Zero) == 1; }
                    catch (EntryPointNotFoundException) { nativeDiagnosticsSupported = false; }
                    catch (DllNotFoundException) { nativeDiagnosticsSupported = false; }
                }
                return nativeDiagnosticsSupported.Value;
            }
        }

        /// <summary>Opts into bounded diagnostic capture. Returns false on an unextended SDK; audio remains unchanged.</summary>
        public bool SetPathDiagnosticsEnabled(bool enabled)
        {
            EnsureUsable();
            if (diagnosticsEnabled == enabled) return true;
            diagnosticsEnabled = enabled && SupportsPathDiagnostics;
            for (int i = 0; i < slots.Length; i++)
            {
                if (diagnosticsEnabled && slots[i].Distance.Diagnostics == null)
                    slots[i].Distance.Diagnostics = new DiagnosticCapture();
                slots[i].Distance.Diagnostics?.Clear();
            }
            return !enabled || diagnosticsEnabled;
        }

        /// <summary>Copies current path headers. Dropped includes header capacity overflow; each header flags vertex truncation.</summary>
        public int CopyPathDiagnostics(SteamAudioSimulationSourceHandle handle, Span<SteamAudioPathDiagnostic> destination,
            out int dropped)
        {
            EnsureUsable(); dropped = 0;
            if (!diagnosticsEnabled || !IsValid(handle) || slots[handle.Index].Result.Status != SteamAudioPathSimulationStatus.Valid) return 0;
            var capture = slots[handle.Index].Distance.Diagnostics;
            int count = Math.Min(destination.Length, capture.Count);
            capture.Paths.AsSpan(0, count).CopyTo(destination);
            dropped = capture.Dropped + capture.Count - count;
            return count;
        }

        /// <summary>Copies the indexed current path's ordered probe vertices. Source/listener attachment legs are not included.</summary>
        public int CopyPathDiagnosticPoints(SteamAudioSimulationSourceHandle handle, int pathIndex, Span<SA.Vector3> destination,
            out bool truncated)
        {
            EnsureUsable(); truncated = false;
            if (!diagnosticsEnabled || !IsValid(handle) || slots[handle.Index].Result.Status != SteamAudioPathSimulationStatus.Valid) return 0;
            var capture = slots[handle.Index].Distance.Diagnostics;
            if (pathIndex < 0 || pathIndex >= capture.Count) return 0;
            var path = capture.Paths[pathIndex];
            int count = Math.Min(destination.Length, path.PointCount);
            for (int i = 0; i < count; i++)
            {
                int at = (path.Offset + i) * 3;
                destination[i] = new SA.Vector3 { x = capture.Points[at], y = capture.Points[at + 1], z = capture.Points[at + 2] };
            }
            truncated = path.Truncated || count < path.PointCount;
            return count;
        }

        [AOT.MonoPInvokeCallback(typeof(NativeDiagnosticCallback))]
        static void CaptureDiagnostic(ref NativeDiagnostic value)
        {
            if (value.SourceUserData == IntPtr.Zero) return;
            var state = GCHandle.FromIntPtr(value.SourceUserData).Target as DistanceState;
            var capture = state?.Diagnostics;
            if (capture == null || !state.CapturePathDistances) return;
            if (capture.Count == capture.Paths.Length) { capture.Dropped++; return; }
            if (value.Count < 0 || !Finite(value.Distance) || !Finite(value.Gain) || !Finite(value.Weight) ||
                !Finite(value.Source) || !Finite(value.Listener) || !Finite(value.VirtualSource)) { capture.Dropped++; return; }
            int count = Math.Min(value.Count, capture.Points.Length / 3 - capture.PointCount);
            if (count > 0 && value.Points == IntPtr.Zero) { capture.Dropped++; return; }
            if (count > 0) Marshal.Copy(value.Points, capture.Points, capture.PointCount * 3, count * 3);
            capture.Paths[capture.Count++] = new SteamAudioPathDiagnostic(value.Source, value.Listener, value.VirtualSource,
                value.Distance, value.Gain, value.Weight, capture.PointCount, count, value.Truncated != 0 || count != value.Count);
            capture.PointCount += count;
        }
    }
}
