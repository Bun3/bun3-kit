using System;
using System.Runtime.InteropServices;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio
{
    internal static class NativePathSimulation
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct DistanceModel
        {
            public int Type;
            public float MinDistance;
            public IntPtr Callback, UserData;
            public int Dirty;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct AirModel
        {
            public int Type;
            public float Low, Mid, High;
            public IntPtr Callback, UserData;
            public int Dirty;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct DirectivityModel
        {
            public float Weight, Power;
            public IntPtr Callback, UserData;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Inputs
        {
            public SA.SimulationFlags Flags;
            public SA.DirectSimulationFlags DirectFlags;
            public SA.CoordinateSpace3 Source;
            public DistanceModel Distance;
            public AirModel Air;
            public DirectivityModel Directivity;
            public SA.OcclusionType OcclusionType;
            public float OcclusionRadius;
            public int OcclusionSamples;
            public float ReverbLow, ReverbMid, ReverbHigh;
            public float HybridTransition, HybridOverlap;
            public int Baked;
            public SA.BakedDataIdentifier BakedIdentifier;
            public IntPtr Probes;
            public float VisibilityRadius, VisibilityThreshold, VisibilityRange;
            public int Order, EnableValidation, FindAlternatePaths, TransmissionRays;
            public IntPtr DeviationModel;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SharedInputs
        {
            public SA.CoordinateSpace3 Listener;
            public int Rays, Bounces;
            public float Duration;
            public int Order;
            public float IrradianceMinDistance;
            public IntPtr VisualizationCallback, UserData;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct DirectOutput
        {
            public int Flags, TransmissionType;
            public float DistanceAttenuation, AirLow, AirMid, AirHigh;
            public float Directivity, Occlusion, TransmissionLow, TransmissionMid, TransmissionHigh;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ReflectionOutput
        {
            public int Type;
            public IntPtr ImpulseResponse;
            public float ReverbLow, ReverbMid, ReverbHigh, EqLow, EqMid, EqHigh;
            public int Delay, Channels, ImpulseResponseSize;
            public IntPtr TanDevice;
            public int TanSlot;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PathOutput
        {
            public float EqLow, EqMid, EqHigh;
            public IntPtr Coefficients;
            public int Order, Binaural;
            public IntPtr Hrtf;
            public SA.CoordinateSpace3 Listener;
            public int NormalizeEq;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Outputs
        {
            public DirectOutput Direct;
            public ReflectionOutput Reflections;
            public PathOutput Pathing;
        }

        internal static void ValidateLayout()
        {
            if (IntPtr.Size != 8 || Marshal.SizeOf<Outputs>() != 216 ||
                Marshal.OffsetOf<Outputs>(nameof(Outputs.Pathing)).ToInt32() != 120 ||
                Marshal.SizeOf<PathOutput>() != 96 ||
                Marshal.OffsetOf<PathOutput>(nameof(PathOutput.NormalizeEq)).ToInt32() != 88 ||
                Marshal.SizeOf<Inputs>() != 264 || Marshal.SizeOf<SharedInputs>() != 88)
                throw new PlatformNotSupportedException("Path simulation requires the Steam Audio 4.8.1 64-bit ABI.");
        }

        [DllImport("phonon", EntryPoint = "iplSourceSetInputs", CallingConvention = CallingConvention.Winapi)]
        internal static extern void SetInputs(IntPtr source, SA.SimulationFlags flags, ref Inputs inputs);

        [DllImport("phonon", EntryPoint = "iplSimulatorSetSharedInputs", CallingConvention = CallingConvention.Winapi)]
        internal static extern void SetSharedInputs(IntPtr simulator, SA.SimulationFlags flags, ref SharedInputs inputs);

        [DllImport("phonon", EntryPoint = "iplSourceGetOutputs", CallingConvention = CallingConvention.Winapi)]
        internal static extern void GetOutputs(IntPtr source, SA.SimulationFlags flags, out Outputs outputs);
    }
}
