using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio
{
    /// <summary>Immutable public view of a copied native scene/pathing bake and its exact probe metadata.</summary>
    public sealed class SteamAudioAcousticAsset : ScriptableObject
    {
        [SerializeField] int formatVersion;
        [SerializeField] string sdkVersion, geometryFingerprint;
        [SerializeField] SteamAudioPathBakeSettings bakeSettings;
        [SerializeField] byte[] sceneData, probeData, sceneChecksum, probeChecksum, metadataChecksum;
        [SerializeField] UnityEngine.Vector4[] probeSpheres;

        /// <summary>Gets the persisted format version.</summary>
        public int FormatVersion => formatVersion;
        /// <summary>Gets the native SDK version that produced the blobs.</summary>
        public string SdkVersion => sdkVersion;
        /// <summary>Gets the caller's nonblank geometry/settings freshness identifier.</summary>
        public string GeometryFingerprint => geometryFingerprint;
        /// <summary>Gets a value copy of the settings used for the bake.</summary>
        public SteamAudioPathBakeSettings BakeSettings => bakeSettings;
        /// <summary>Gets the number of exact baked probe spheres available for coverage validation.</summary>
        public int ProbeCount => probeSpheres?.Length ?? 0;

        /// <summary>Returns a value copy of one probe in native Steam Audio coordinates.</summary>
        public SA.Sphere GetProbe(int index)
        {
            if (probeSpheres == null || index < 0 || index >= probeSpheres.Length) throw new ArgumentOutOfRangeException(nameof(index));
            var sphere = probeSpheres[index];
            return new SA.Sphere { center = new SA.Vector3 { x = sphere.x, y = sphere.y, z = sphere.z }, radius = sphere.w };
        }

        internal byte[] SceneData => sceneData;
        internal byte[] ProbeData => probeData;
        internal void Initialize(byte[] sceneBytes, byte[] probeBytes, SA.Sphere[] probes,
            SteamAudioPathBakeSettings settings, string fingerprint)
        {
            settings.Validate();
            if (string.IsNullOrWhiteSpace(fingerprint)) throw new ArgumentException("A geometry fingerprint is required.", nameof(fingerprint));
            ValidateProbes(probes);
            ValidateBlob(sceneBytes); ValidateBlob(probeBytes);
            formatVersion = 1; sdkVersion = "4.8.1"; geometryFingerprint = fingerprint; bakeSettings = settings;
            sceneData = (byte[])sceneBytes.Clone(); probeData = (byte[])probeBytes.Clone();
            probeSpheres = new UnityEngine.Vector4[probes.Length];
            for (int i = 0; i < probes.Length; i++)
                probeSpheres[i] = new UnityEngine.Vector4(probes[i].center.x, probes[i].center.y, probes[i].center.z, probes[i].radius);
            sceneChecksum = Hash(sceneData); probeChecksum = Hash(probeData); metadataChecksum = HashMetadata();
        }

        internal void ValidateForLoad()
        {
            if (formatVersion != 1 || sdkVersion != "4.8.1" || string.IsNullOrWhiteSpace(geometryFingerprint))
                throw new InvalidOperationException("The acoustic asset format or SDK metadata is unsupported.");
            try { bakeSettings.Validate(); }
            catch (ArgumentException error) { throw new InvalidOperationException("The acoustic bake settings are invalid.", error); }
            ValidateBlob(sceneData); ValidateBlob(probeData);
            if (ProbeCount == 0) throw new InvalidOperationException("The acoustic asset has no probe metadata.");
            for (int i = 0; i < probeSpheres.Length; i++)
            {
                var value = probeSpheres[i];
                if (!Finite(value.x) || !Finite(value.y) || !Finite(value.z) || !Finite(value.w) || value.w <= 0)
                    throw new InvalidOperationException("The acoustic probe metadata is invalid.");
            }
            if (!EqualHash(sceneChecksum, Hash(sceneData)) || !EqualHash(probeChecksum, Hash(probeData)) ||
                !EqualHash(metadataChecksum, HashMetadata()))
                throw new InvalidOperationException("The acoustic asset checksum does not match its contents.");
        }

        internal static void ValidateProbes(SA.Sphere[] probes)
        {
            if (probes == null || probes.Length == 0) throw new ArgumentException("At least one probe sphere is required.", nameof(probes));
            for (int i = 0; i < probes.Length; i++)
                if (!Finite(probes[i].center.x) || !Finite(probes[i].center.y) || !Finite(probes[i].center.z) ||
                    !Finite(probes[i].radius) || probes[i].radius <= 0)
                    throw new ArgumentException("Probe spheres must be finite with positive radii.", nameof(probes));
        }
        static void ValidateBlob(byte[] blob)
        {
            if (blob == null || blob.Length < 8) throw new InvalidOperationException("The acoustic asset contains an empty or truncated native blob.");
        }
        byte[] HashMetadata()
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(formatVersion); writer.Write(sdkVersion); writer.Write(geometryFingerprint);
                writer.Write(bakeSettings.VisibilitySamples); writer.Write(bakeSettings.VisibilityRadius);
                writer.Write(bakeSettings.VisibilityThreshold); writer.Write(bakeSettings.VisibilityRange);
                writer.Write(bakeSettings.PathRange); writer.Write(bakeSettings.Threads);
                writer.Write(probeSpheres.Length);
                for (int i = 0; i < probeSpheres.Length; i++)
                {
                    var sphere = probeSpheres[i];
                    writer.Write(sphere.x); writer.Write(sphere.y); writer.Write(sphere.z); writer.Write(sphere.w);
                }
            }
            return Hash(stream.ToArray());
        }
        static byte[] Hash(byte[] data) { using var sha = SHA256.Create(); return sha.ComputeHash(data); }
        static bool EqualHash(byte[] expected, byte[] actual)
        {
            if (expected == null || expected.Length != actual.Length) return false;
            for (int i = 0; i < actual.Length; i++) if (expected[i] != actual[i]) return false;
            return true;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
