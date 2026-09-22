using System;
using System.Runtime.InteropServices;
using UnityEngine;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio
{
    /// <summary>Cold synchronous native path baking for caller-generated geometry, without asset-database side effects.</summary>
    public static class SteamAudioAcousticBaker
    {
        static readonly SA.ProgressCallback Progress = (_, __) => { };

        /// <summary>
        /// Bakes a default native scene and exact probe spheres into a new ScriptableObject on the Unity
        /// main thread. All positions use native Steam Audio coordinates. The caller owns persistence
        /// and must destroy the returned transient asset if it is not saved. Dynamic blockers belong to
        /// runtime geometry updates rather than this open-connectivity bake.
        /// </summary>
        public static SteamAudioAcousticAsset Bake(SA.Context context, SA.Vector3[] vertices, SA.Triangle[] triangles,
            int[] materialIndices, SA.Material[] materials, SA.Sphere[] probes, SteamAudioPathBakeSettings settings,
            string geometryFingerprint)
        {
            if (context == null || context.Get() == IntPtr.Zero) throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(geometryFingerprint)) throw new ArgumentException("A geometry fingerprint is required.", nameof(geometryFingerprint));
            settings.Validate(); SteamAudioAcousticAsset.ValidateProbes(probes);
            ValidateMesh(vertices, triangles, materialIndices, materials);
            NativePathSimulation.ValidateLayout();
            SA.Scene scene = null;
            SA.ProbeBatch batch = null;
            IntPtr mesh = IntPtr.Zero;
            SteamAudioAcousticAsset asset = null;
            try
            {
                scene = new SA.Scene(context, SA.SceneType.Default, null, null, null, null);
                mesh = CreateMesh(scene, vertices, triangles, materialIndices, materials);
                SA.API.iplStaticMeshAdd(mesh, scene.Get());
                scene.Commit();
                batch = new SA.ProbeBatch(context);
                if (batch.Get() == IntPtr.Zero) throw new InvalidOperationException("Steam Audio could not create a probe batch.");
                for (int i = 0; i < probes.Length; i++) batch.AddProbe(probes[i]);
                batch.Commit();
                BakePathing(context, scene, batch, settings);
                byte[] sceneBytes = Serialize(context, scene.Get(), true);
                byte[] probeBytes = Serialize(context, batch.Get(), false);
                asset = ScriptableObject.CreateInstance<SteamAudioAcousticAsset>();
                asset.Initialize(sceneBytes, probeBytes, probes, settings, geometryFingerprint);
                return asset;
            }
            catch
            {
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
                throw;
            }
            finally
            {
                batch?.Release();
                if (mesh != IntPtr.Zero) SA.API.iplStaticMeshRelease(ref mesh);
                scene?.Release();
            }
        }

        static void BakePathing(SA.Context context, SA.Scene scene, SA.ProbeBatch batch, SteamAudioPathBakeSettings settings)
        {
            var parameters = new SA.PathBakeParams
            {
                scene = scene.Get(), probeBatch = batch.Get(),
                identifier = new SA.BakedDataIdentifier { type = SA.BakedDataType.Pathing, variation = SA.BakedDataVariation.Dynamic },
                numSamples = settings.VisibilitySamples, radius = settings.VisibilityRadius,
                threshold = settings.VisibilityThreshold, visRange = settings.VisibilityRange,
                pathRange = settings.PathRange, numThreads = settings.Threads
            };
            SA.API.iplPathBakerBake(context.Get(), ref parameters, Progress, IntPtr.Zero);
            if (batch.GetDataSize(parameters.identifier).ToUInt64() == 0)
                throw new InvalidOperationException("Steam Audio did not produce a pathing data layer.");
        }

        static byte[] Serialize(SA.Context context, IntPtr value, bool scene)
        {
            var serialized = new SA.SerializedObject(context);
            try
            {
                if (serialized.Get() == IntPtr.Zero) throw new InvalidOperationException("Steam Audio could not allocate serialization storage.");
                if (scene) SA.API.iplSceneSave(value, serialized.Get());
                else SA.API.iplProbeBatchSave(value, serialized.Get());
                ulong size = serialized.GetSize().ToUInt64();
                if (size < 8 || size > int.MaxValue || serialized.GetData() == IntPtr.Zero)
                    throw new InvalidOperationException("Steam Audio returned an invalid serialized blob.");
                var bytes = new byte[(int)size];
                Marshal.Copy(serialized.GetData(), bytes, 0, bytes.Length);
                return bytes;
            }
            finally { serialized.Release(); }
        }

        internal static IntPtr CreateMesh(SA.Scene scene, SA.Vector3[] vertices, SA.Triangle[] triangles, int[] indices, SA.Material[] materials)
        {
            var settings = new SA.StaticMeshSettings
            { numVertices = vertices.Length, numTriangles = triangles.Length, numMaterials = materials.Length };
            IntPtr mesh = IntPtr.Zero;
            try
            {
                settings.vertices = Copy(vertices);
                settings.triangles = Copy(triangles);
                settings.materialIndices = Marshal.AllocHGlobal(checked(indices.Length * sizeof(int)));
                Marshal.Copy(indices, 0, settings.materialIndices, indices.Length);
                settings.materials = Copy(materials);
                var result = SA.API.iplStaticMeshCreate(scene.Get(), ref settings, out mesh);
                if (result != SA.Error.Success || mesh == IntPtr.Zero)
                    throw new InvalidOperationException("Steam Audio could not create the acoustic mesh.");
                return mesh;
            }
            catch { if (mesh != IntPtr.Zero) SA.API.iplStaticMeshRelease(ref mesh); throw; }
            finally
            {
                Marshal.FreeHGlobal(settings.vertices); Marshal.FreeHGlobal(settings.triangles);
                Marshal.FreeHGlobal(settings.materialIndices); Marshal.FreeHGlobal(settings.materials);
            }
        }
        static IntPtr Copy<T>(T[] values) where T : struct
        {
            int stride = Marshal.SizeOf<T>();
            IntPtr memory = Marshal.AllocHGlobal(checked(stride * values.Length));
            try
            {
                for (int i = 0; i < values.Length; i++) Marshal.StructureToPtr(values[i], IntPtr.Add(memory, i * stride), false);
                return memory;
            }
            catch { Marshal.FreeHGlobal(memory); throw; }
        }
        internal static void ValidateMesh(SA.Vector3[] vertices, SA.Triangle[] triangles, int[] indices, SA.Material[] materials)
        {
            if (vertices == null || vertices.Length < 3) throw new ArgumentException("Mesh vertices are required.", nameof(vertices));
            if (triangles == null || triangles.Length == 0) throw new ArgumentException("Mesh triangles are required.", nameof(triangles));
            if (materials == null || materials.Length == 0) throw new ArgumentException("Mesh materials are required.", nameof(materials));
            if (indices == null || indices.Length != triangles.Length) throw new ArgumentException("Each triangle needs a material index.", nameof(indices));
            for (int i = 0; i < vertices.Length; i++)
                if (!Finite(vertices[i].x) || !Finite(vertices[i].y) || !Finite(vertices[i].z)) throw new ArgumentException("Mesh vertices must be finite.", nameof(vertices));
            for (int i = 0; i < triangles.Length; i++)
            {
                var triangle = triangles[i];
                bool validVertices = IsVertexIndex(triangle.index0, vertices.Length) &&
                    IsVertexIndex(triangle.index1, vertices.Length) && IsVertexIndex(triangle.index2, vertices.Length);
                bool distinctVertices = triangle.index0 != triangle.index1 && triangle.index1 != triangle.index2 &&
                    triangle.index0 != triangle.index2;
                bool validMaterial = indices[i] >= 0 && indices[i] < materials.Length;
                if (!validVertices || !distinctVertices || !validMaterial)
                    throw new ArgumentException("Mesh topology or material indices are invalid.", nameof(triangles));
            }
            for (int i = 0; i < materials.Length; i++)
            {
                if (!HasValidCoefficients(materials[i]))
                    throw new ArgumentException("Acoustic material coefficients must be within zero and one.", nameof(materials));
            }
        }
        static bool IsVertexIndex(int index, int vertexCount) => index >= 0 && index < vertexCount;

        static bool HasValidCoefficients(SA.Material material) =>
            Unit(material.absorptionLow) && Unit(material.absorptionMid) && Unit(material.absorptionHigh) &&
            Unit(material.scattering) && Unit(material.transmissionLow) && Unit(material.transmissionMid) &&
            Unit(material.transmissionHigh);

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Unit(float value) => Finite(value) && value >= 0 && value <= 1;
    }
}
