#if BUN3_ADDRESSABLES
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Bun3.Unity.Audio
{
    /// <summary>
    /// Addressables preload/release lifecycle for <see cref="SoundDef.AddressableClips"/>.
    /// Cold path only: preload runs from loading screens/setup, never the per-frame Tick,
    /// so the Dictionary/array allocations below are sanctioned.
    /// </summary>
    public sealed partial class SoundSystem
    {
        private Dictionary<SoundDef, AsyncOperationHandle<AudioClip>[]> _preloaded;

        /// <summary>
        /// Loads <paramref name="def"/>'s AddressableClips and fills its RuntimeClips. No-op
        /// when the def has no AddressableClips or is already preloaded. On a load failure,
        /// every handle loaded so far for this call is released, RuntimeClips is left unset,
        /// a development-build warning is logged, and the call returns normally — silent-skip,
        /// never an exception. On cancellation, loaded handles are released and
        /// <see cref="OperationCanceledException"/> propagates.
        /// </summary>
        /// <param name="def">Sound definition whose AddressableClips to load.</param>
        /// <param name="ct">Checked between clip loads; does not abort an in-flight load.</param>
        /// <exception cref="ObjectDisposedException">The system has been disposed.</exception>
        public async UniTask PreloadAsync(SoundDef def, CancellationToken ct = default)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SoundSystem));
            }
            if (def == null || def.AddressableClips == null || def.AddressableClips.Length == 0 || IsPreloaded(def))
            {
                return;
            }

            var refs = def.AddressableClips;
            var handles = new AsyncOperationHandle<AudioClip>[refs.Length];
            var clips = new AudioClip[refs.Length];
            var loadedCount = 0;

            for (var i = 0; i < refs.Length; i++)
            {
                if (ct.IsCancellationRequested)
                {
                    ReleaseHandles(handles, loadedCount);
                    ct.ThrowIfCancellationRequested();
                }

                handles[i] = refs[i].LoadAssetAsync();
                loadedCount = i + 1;
                await handles[i].Task;

                if (handles[i].Status != AsyncOperationStatus.Succeeded)
                {
                    ReleaseHandles(handles, loadedCount);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    Debug.LogWarning($"SoundSystem.PreloadAsync: failed to load an AddressableClip on '{def.name}'; preload skipped.");
#endif
                    return;
                }
                clips[i] = handles[i].Result;

                if (ct.IsCancellationRequested)
                {
                    ReleaseHandles(handles, loadedCount);
                    ct.ThrowIfCancellationRequested();
                }
            }

            _preloaded ??= new Dictionary<SoundDef, AsyncOperationHandle<AudioClip>[]>();
            _preloaded[def] = handles;
            def.RuntimeClips = clips;
        }

        /// <summary>True when <paramref name="def"/> is tracked as preloaded with RuntimeClips set.</summary>
        public bool IsPreloaded(SoundDef def) =>
            def != null && def.RuntimeClips != null && _preloaded != null && _preloaded.ContainsKey(def);

        /// <summary>
        /// Releases <paramref name="def"/>'s preloaded handles and clears its RuntimeClips.
        /// No-op when the def was never preloaded. In development builds, logs a warning
        /// (does not block) when a voice is still actively playing this def.
        /// </summary>
        public void ReleasePreloaded(SoundDef def)
        {
            if (def == null || _preloaded == null || !_preloaded.TryGetValue(def, out var handles))
            {
                return;
            }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            for (var i = 0; i < Table.Slots.Length; i++)
            {
                if (Table.Slots[i].State != VoiceState.Idle && ReferenceEquals(Table.Slots[i].Def, def))
                {
                    Debug.LogWarning($"SoundSystem.ReleasePreloaded: '{def.name}' has an active voice; it will keep playing on its already-assigned clip.");
                    break;
                }
            }
#endif

            ReleaseHandles(handles, handles.Length);
            _preloaded.Remove(def);
            def.RuntimeClips = null;
        }

        /// <summary>Releases every preloaded def's handles; called from Dispose.</summary>
        partial void ReleaseAllPreloadedOnDispose()
        {
            if (_preloaded == null)
            {
                return;
            }
            foreach (var pair in _preloaded)
            {
                ReleaseHandles(pair.Value, pair.Value.Length);
                pair.Key.RuntimeClips = null;
            }
            _preloaded.Clear();
        }

        private static void ReleaseHandles(AsyncOperationHandle<AudioClip>[] handles, int count)
        {
            for (var i = 0; i < count; i++)
            {
                if (handles[i].IsValid())
                {
                    Addressables.Release(handles[i]);
                }
            }
        }
    }
}
#endif
