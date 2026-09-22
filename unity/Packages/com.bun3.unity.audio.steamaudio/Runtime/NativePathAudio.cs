using System;
using System.Runtime.InteropServices;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio
{
    internal static class NativePathAudio
    {
#if (UNITY_IOS || UNITY_WEBGL) && !UNITY_EDITOR
        private const string Library = "__Internal";
#else
        private const string Library = "phonon";
#endif

        [StructLayout(LayoutKind.Sequential)]
        internal struct SpeakerLayout
        {
            internal int Type;
            internal int NumSpeakers;
            internal IntPtr Speakers;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Settings
        {
            internal int MaxOrder;
            internal int Spatialize;
            internal SpeakerLayout SpeakerLayout;
            internal IntPtr Hrtf;
        }

        // Matches the three-band Steam Audio 4.8.1 C ABI, including its trailing boolean.
        [StructLayout(LayoutKind.Sequential)]
        internal struct Parameters
        {
            internal float EqLow;
            internal float EqMid;
            internal float EqHigh;
            internal IntPtr ShCoefficients;
            internal int Order;
            internal int Binaural;
            internal IntPtr Hrtf;
            internal SA.CoordinateSpace3 Listener;
            internal int NormalizeEq;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct AudioBuffer
        {
            internal int NumChannels;
            internal int NumSamples;
            internal IntPtr Data;
        }

        [DllImport(Library, EntryPoint = "iplPathEffectCreate", CallingConvention = CallingConvention.Winapi)]
        internal static extern SA.Error Create(IntPtr context, ref SA.AudioSettings audioSettings,
            ref Settings settings, out IntPtr effect);

        [DllImport(Library, EntryPoint = "iplPathEffectApply", CallingConvention = CallingConvention.Winapi)]
        internal static extern int Apply(IntPtr effect, ref Parameters parameters, ref AudioBuffer input, ref AudioBuffer output);

        [DllImport(Library, EntryPoint = "iplPathEffectGetTailSize", CallingConvention = CallingConvention.Winapi)]
        internal static extern int GetTailSize(IntPtr effect);

        [DllImport(Library, EntryPoint = "iplPathEffectGetTail", CallingConvention = CallingConvention.Winapi)]
        internal static extern int GetTail(IntPtr effect, ref AudioBuffer output);

        [DllImport(Library, EntryPoint = "iplPathEffectReset", CallingConvention = CallingConvention.Winapi)]
        internal static extern void Reset(IntPtr effect);

        [DllImport(Library, EntryPoint = "iplPathEffectRelease", CallingConvention = CallingConvention.Winapi)]
        internal static extern void Release(ref IntPtr effect);

        [DllImport(Library, EntryPoint = "iplAudioBufferAllocate", CallingConvention = CallingConvention.Winapi)]
        internal static extern SA.Error AllocateBuffer(IntPtr context, int channels, int samples, out AudioBuffer buffer);

        [DllImport(Library, EntryPoint = "iplAudioBufferFree", CallingConvention = CallingConvention.Winapi)]
        internal static extern void FreeBuffer(IntPtr context, ref AudioBuffer buffer);
    }
}
