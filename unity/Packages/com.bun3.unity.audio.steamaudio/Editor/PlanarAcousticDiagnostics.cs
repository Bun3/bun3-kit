using System;
using System.Collections.Generic;
using UnityEngine;
namespace Bun3.Unity.Audio.SteamAudio.Editor
{
    /// <summary>Opt-in control-thread snapshots of actual native paths and output state. Editor-only allocations.</summary>
    public static class PlanarAcousticDiagnostics
    {
        /// <summary>One native path contribution, expressed in Unity planar coordinates.</summary>
        [Serializable] public sealed class NativePath
        {
            /// <summary>Physical endpoints and the native virtual source.</summary>
            public Vector3 source, listener, virtualSource;
            /// <summary>Native evaluated distance, distance gain, and contribution weight.</summary>
            public float distance, gain, weight;
            /// <summary>Whether the vertex buffer omitted part of the path.</summary>
            public bool truncated;
            /// <summary>Ordered native probe vertices.</summary>
            public Vector3[] probes;
        }
        /// <summary>One registered voice or SFX diagnostic snapshot.</summary>
        [Serializable] public sealed class Voice
        {
            /// <summary>Display label supplied by the output.</summary>
            public string name;
            /// <summary>Native distance range and authored attenuation end distance.</summary>
            public float nativeMin, nativeMax, maximum;
            /// <summary>Requested stereo width: zero is dual mono, one is full native binaural.</summary>
            public float spatialBlend;
            /// <summary>Authored distance-to-volume curve.</summary>
            public AnimationCurve curve;
            /// <summary>Cached editor curve evaluator.</summary>
            [NonSerialized] public Bun3.Unity.Audio.DistanceAttenuationCurve evaluation;
            /// <summary>Counts of native path evaluations and positive distance gains.</summary>
            public int nativeCount, nativeNonzeroCount;
            /// <summary>Availability, attenuation, path-signal, output-gate, and route-coverage states.</summary>
            public bool nativeDistancesAvailable, attenuationEnabled, hasPath, blocked, routeCovered;
            /// <summary>Current source position.</summary>
            public Vector3 position;
            /// <summary>Captured native contributions.</summary>
            public NativePath[] paths;
            /// <summary>Contributions omitted because diagnostic capacity was exceeded.</summary>
            public int droppedPaths;
        }
        /// <summary>A timestamped capture for local or remote editor presentation.</summary>
        [Serializable] public sealed class Snapshot
        {
            /// <summary>UTC capture time in DateTime ticks.</summary>
            public long ticks;
            /// <summary>Scene label supplied by the host.</summary>
            public string scene;
            /// <summary>Last simulated listener position.</summary>
            public Vector3 listener;
            /// <summary>Registered sources with valid simulation handles.</summary>
            public Voice[] voices;
            /// <summary>Whether the native library supports detailed path capture.</summary>
            public bool nativePathExtension;
        }
        static readonly SteamAudioPathDiagnostic[] pathBuffer = new SteamAudioPathDiagnostic[64];
        static readonly global::SteamAudio.Vector3[] pointBuffer = new global::SteamAudio.Vector3[4096];
        static Vector3 GamePoint(global::SteamAudio.Vector3 value) => new Vector3(value.x, -value.z, 0);

        static NativePath[] ReadPaths(PlanarAcousticWorld world, PlanarAcousticSourceHandle handle, out int dropped)
        {
            int count = world.CopyNativePathDiagnostics(handle, pathBuffer, out dropped);
            var result = new NativePath[count];
            for (int i = 0; i < count; i++)
            {
                var path = pathBuffer[i];
                int points = world.CopyNativePathDiagnosticPoints(handle, i, pointBuffer, out bool truncated);
                var vertices = new Vector3[points];
                for (int j = 0; j < points; j++) vertices[j] = GamePoint(pointBuffer[j]);
                result[i] = new NativePath { source = GamePoint(path.Source), listener = GamePoint(path.Listener),
                    virtualSource = GamePoint(path.VirtualSource), distance = path.Distance, gain = path.DistanceGain,
                    weight = path.Weight, truncated = truncated, probes = vertices };
            }
            return result;
        }

        /// <summary>Captures registered diagnostic sources without inspecting private implementation fields.</summary>
        public static Snapshot Capture(PlanarAcousticWorld world, IReadOnlyList<IPlanarAcousticOutput> outputs, string scene)
        {
            if (world == null || world.IsDisposed) throw new ArgumentException("A live world is required.", nameof(world));
            var voices = new List<Voice>();
            var listener = world.ListenerPosition;
            var coefficients = new float[4];
            for (int i = 0; i < outputs.Count; i++)
            {
                if (!(outputs[i] is IPlanarAcousticDiagnostics source)) continue;
                var handle = source.SourceHandle;
                if (!handle.IsValid) continue;
                voices.Add(CaptureVoice(world, source, handle, listener, coefficients));
            }
            return new Snapshot { ticks = DateTime.UtcNow.Ticks, scene = scene, listener = listener,
                voices = voices.ToArray(), nativePathExtension = world.SupportsNativePathDiagnostics };
        }
        static Voice CaptureVoice(PlanarAcousticWorld world, IPlanarAcousticDiagnostics source,
            PlanarAcousticSourceHandle handle, Vector2 listener, float[] coefficients)
        {
            var profile = source.AttenuationProfile;
            bool measured = world.TryGetNativePathDistances(handle, out float min, out float max, out int count, out int positive);
            var nativePaths = ReadPaths(world, handle, out int dropped);
            return new Voice
            {
                name = source.Label, position = source.Position, nativeDistancesAvailable = measured,
                paths = nativePaths, droppedPaths = dropped, nativeMin = min, nativeMax = max,
                nativeCount = count, nativeNonzeroCount = positive, spatialBlend = source is IPlanarSpatialBlendDiagnostics width ? width.GetSpatialBlend(world) : world.GetSpatialBlend(handle),
                maximum = profile != null ? profile.GetSnapshot().MaximumDistance : source.MaximumDistance,
                curve = profile != null ? profile.VolumeByDistance : DistanceAttenuationProfile.CreateLegacyCurve(source.MinimumDistance, source.MaximumDistance, .2f),
                attenuationEnabled = source.DistanceAttenuation,
                hasPath = world.TryGetPath(handle, coefficients, out _), blocked = source.IsBlocked,
                routeCovered = world.IsRouteCovered(source.Position, listener)
            };
        }

    }
}
