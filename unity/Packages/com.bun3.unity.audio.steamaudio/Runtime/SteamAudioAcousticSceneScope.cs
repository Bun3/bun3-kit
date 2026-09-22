using System;
using UnityEngine;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio
{
    /// <summary>
    /// Owns loaded native acoustic geometry and probes with a retained context. Create and dispose on
    /// the Unity main/control thread. Borrowed wrappers must not be released by consumers. Normally dispose
    /// simulations before this scope; their independently retained native handles remain valid until released.
    /// SDK-internal serialization temporaries may require finalizer cleanup if native loading itself throws.
    /// </summary>
    public sealed class SteamAudioAcousticSceneScope : IDisposable
    {
        readonly int ownerThread;
        SA.Context context;
        SA.Scene scene;
        SA.ProbeBatch probes;
        bool disposed;

        /// <summary>
        /// Checks metadata and checksums, then loads an asset through supported SDK constructors. These
        /// checks detect accidental corruption, not arbitrary untrusted native formats. Successfully loaded
        /// wrappers and temporary Unity objects are cleaned deterministically on normal and partial failure paths.
        /// </summary>
        public SteamAudioAcousticSceneScope(SA.Context context, SteamAudioAcousticAsset asset)
        {
            ownerThread = Environment.CurrentManagedThreadId;
            if (context == null || context.Get() == IntPtr.Zero) throw new ArgumentNullException(nameof(context));
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            asset.ValidateForLoad();
            NativePathSimulation.ValidateLayout();
            SA.SerializedData sceneData = null, probeData = null;
            try
            {
                this.context = new SA.Context(context);
                sceneData = ScriptableObject.CreateInstance<SA.SerializedData>(); sceneData.data = asset.SceneData;
                probeData = ScriptableObject.CreateInstance<SA.SerializedData>(); probeData.data = asset.ProbeData;
                scene = new SA.Scene(this.context, new SA.SceneSettings { type = SA.SceneType.Default }, sceneData);
                if (scene.Get() == IntPtr.Zero) throw new InvalidOperationException("Steam Audio returned an empty loaded scene.");
                probes = new SA.ProbeBatch(this.context, probeData);
                if (probes.Get() == IntPtr.Zero) throw new InvalidOperationException("Steam Audio returned an empty loaded probe batch.");
                scene.Commit();
                probes.Commit();
                var pathing = new SA.BakedDataIdentifier { type = SA.BakedDataType.Pathing, variation = SA.BakedDataVariation.Dynamic };
                if (probes.GetDataSize(pathing).ToUInt64() == 0)
                    throw new InvalidOperationException("The acoustic asset has no baked pathing data layer.");
            }
            catch { Release(); disposed = true; throw; }
            finally
            {
                if (probeData != null) UnityEngine.Object.DestroyImmediate(probeData);
                if (sceneData != null) UnityEngine.Object.DestroyImmediate(sceneData);
            }
        }

        /// <summary>Gets the borrowed retained SDK context, usable for same-thread dynamic mesh construction.</summary>
        public SA.Context Context { get { EnsureUsable(); return context; } }
        /// <summary>Gets the borrowed default native scene. Commit dynamic edits before invalidating simulation results.</summary>
        public SA.Scene Scene { get { EnsureUsable(); return scene; } }
        /// <summary>Gets the borrowed loaded pathing probe batch.</summary>
        public SA.ProbeBatch Probes { get { EnsureUsable(); return probes; } }
        /// <summary>Gets whether the owned wrappers have been released.</summary>
        public bool IsDisposed => disposed;

        /// <summary>Releases loaded probes, geometry and the retained context. Repeated disposal is harmless.</summary>
        public void Dispose()
        {
            EnsureThread();
            if (disposed) return;
            disposed = true; Release();
        }
        void EnsureThread()
        {
            if (Environment.CurrentManagedThreadId != ownerThread)
                throw new InvalidOperationException("Acoustic scene access must remain on its creating control thread.");
        }
        void EnsureUsable()
        {
            EnsureThread();
            if (disposed) throw new ObjectDisposedException(nameof(SteamAudioAcousticSceneScope));
        }
        void Release()
        {
            probes?.Release(); probes = null;
            scene?.Release(); scene = null;
            context?.Release(); context = null;
        }
    }
}
