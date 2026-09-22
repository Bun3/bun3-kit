using System;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio
{
    /// <summary>Cold-created default-scene mesh with explicit control-thread membership and deterministic ownership.</summary>
    public sealed class SteamAudioMeshScope : IDisposable
    {
        readonly int thread = Environment.CurrentManagedThreadId;
        IntPtr scene, mesh;
        bool added, disposed;

        /// <summary>Validates and copies native-coordinate mesh arrays; retains the scene without adding the mesh.</summary>
        public SteamAudioMeshScope(SA.Scene scene, SA.Vector3[] vertices, SA.Triangle[] triangles, int[] materialIndices, SA.Material[] materials)
        {
            if (scene == null || scene.Get() == IntPtr.Zero) throw new ArgumentNullException(nameof(scene));
            SteamAudioAcousticBaker.ValidateMesh(vertices, triangles, materialIndices, materials);
            this.scene = SA.API.iplSceneRetain(scene.Get());
            try { mesh = SteamAudioAcousticBaker.CreateMesh(scene, vertices, triangles, materialIndices, materials); }
            catch { SA.API.iplSceneRelease(ref this.scene); throw; }
        }

        /// <summary>Whether the mesh has been added, independently of the caller's scene commit.</summary>
        public bool IsAdded => added && !disposed;

        /// <summary>Adds once. Caller commits its scene and invalidates simulation snapshots after membership changes.</summary>
        public void Add()
        {
            Ensure(); if (added) return;
            SA.API.iplStaticMeshAdd(mesh, scene); added = true;
        }

        /// <summary>Removes once. Caller commits its scene and invalidates simulation snapshots after membership changes.</summary>
        public void Remove()
        {
            Ensure(); if (!added) return;
            SA.API.iplStaticMeshRemove(mesh, scene); added = false;
        }

        /// <summary>Removes and releases the mesh and retained scene on the creating thread. Does not commit the scene.</summary>
        public void Dispose()
        {
            EnsureThread(); if (disposed) return;
            if (added) SA.API.iplStaticMeshRemove(mesh, scene);
            added = false; disposed = true;
            SA.API.iplStaticMeshRelease(ref mesh); SA.API.iplSceneRelease(ref scene);
        }
        void EnsureThread()
        {
            if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Mesh ownership belongs to the creating thread.");
        }
        void Ensure() { EnsureThread(); if (disposed) throw new ObjectDisposedException(nameof(SteamAudioMeshScope)); }
    }
}
