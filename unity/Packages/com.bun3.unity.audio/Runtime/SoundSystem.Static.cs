// Static entry point: one process-wide instance built from a catalog and played by string key.
using System;
using UnityEngine;

namespace Bun3.Unity.Audio
{
    public sealed partial class SoundSystem
    {
        private static SoundCatalog _catalog;
        private static bool _warnedUninitialized;

        /// <summary>
        /// The instance created by <see cref="Initialize"/>; null before initialization and after shutdown or
        /// disposal. Use it for what the static API does not cover: handles, charge sounds, follow, channel volumes.
        /// </summary>
        public static SoundSystem Current { get; private set; }

        /// <summary>
        /// Validates the catalog, creates <see cref="Current"/> and prepares every catalog definition, running
        /// <see cref="SoundSystemConfig.PrepareDefinition"/> for each. <see cref="Shutdown"/> runs on application quit.
        /// </summary>
        /// <exception cref="ArgumentNullException">A null argument.</exception>
        /// <exception cref="InvalidOperationException">Already initialized; call <see cref="Shutdown"/> first.</exception>
        /// <exception cref="ArgumentException">The catalog is invalid; nothing is created.</exception>
        public static void Initialize(SoundCatalog catalog, SoundSystemConfig config)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (Current != null) throw new InvalidOperationException("SoundSystem is already initialized; call Shutdown first.");
            catalog.ValidateOrThrow();
            var system = new SoundSystem(config);
            try
            {
                system.Prepare(catalog);
                for (var i = 0; i < catalog.Entries.Count; i++) RunPrepareHook(config, catalog.Entries[i].Definition);
            }
            catch
            {
                system.Dispose();
                throw;
            }
            Current = system;
            _catalog = catalog;
            CaptureSnapshots(catalog);
            Application.quitting -= Shutdown;
            Application.quitting += Shutdown;
        }

        /// <summary>Disposes <see cref="Current"/>. Does nothing when not initialized.</summary>
        public static void Shutdown()
        {
            _warnedUninitialized = false;
            Current?.Dispose();
        }

        /// <summary>
        /// Plays a catalog key in 2D, even when its definition is positional. Unknown keys are ignored and return
        /// <see cref="SoundHandle.Invalid"/>; the handle stops a looping definition.
        /// </summary>
        public static SoundHandle Play(string key) => Play(key, null);

        /// <summary>Plays <paramref name="key"/> like <see cref="Play(string)"/>, or <paramref name="fallbackKey"/> when the catalog lacks it.</summary>
        public static SoundHandle Play(string key, string fallbackKey) =>
            TryResolve(key, fallbackKey, out var def) ? Current.Play(def, Vector3.zero, SpatialMode.None) : SoundHandle.Invalid;

        /// <summary>
        /// Plays a catalog key at a fixed position, or in 2D when its definition's spatial mode is None.
        /// Unknown keys are ignored and return <see cref="SoundHandle.Invalid"/>.
        /// </summary>
        public static SoundHandle PlayAt(string key, Vector3 position) => PlayAt(key, null, position);

        /// <summary>Plays <paramref name="key"/> like <see cref="PlayAt(string, Vector3)"/>, or <paramref name="fallbackKey"/> when the catalog lacks it.</summary>
        public static SoundHandle PlayAt(string key, string fallbackKey, Vector3 position) =>
            TryResolve(key, fallbackKey, out var def)
                ? Current.Play(def, position, def.EffectiveSpatial == SpatialMode.None ? SpatialMode.None : SpatialMode.Positional)
                : SoundHandle.Invalid;

        /// <summary>
        /// Makes a catalog key the current music with <see cref="SoundSystemConfig.MusicFadeSeconds"/>.
        /// The same key keeps playing without restarting; an unknown key stops the music.
        /// </summary>
        public static void PlayMusic(string key)
        {
            if (Current == null) { WarnUninitialized(); return; }
            if (!_catalog.TryGet(key, out var def)) { StopMusic(); return; }
            if (Current.IsMusicPlaying && Current.MusicChannels[Current.ActiveMusic].Sound == def) return;
            Current.PlayMusic(def, Current._config.MusicFadeSeconds);
        }

        /// <summary>Stops the current music with <see cref="SoundSystemConfig.MusicFadeSeconds"/>.</summary>
        public static void StopMusic()
        {
            if (Current == null) { WarnUninitialized(); return; }
            Current.StopMusic(Current._config.MusicFadeSeconds);
        }

        private static void RunPrepareHook(SoundSystemConfig config, SoundDef def)
        {
            if (config.PrepareDefinition == null) return;
            try { config.PrepareDefinition(def); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private static bool TryResolve(string key, string fallbackKey, out SoundDef def)
        {
            if (Current == null)
            {
                WarnUninitialized();
                def = null;
                return false;
            }
            return _catalog.TryGet(key, out def) || _catalog.TryGet(fallbackKey, out def);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private static void WarnUninitialized()
        {
            if (_warnedUninitialized) return;
            _warnedUninitialized = true;
            Debug.LogWarning("SoundSystem: static playback requested before SoundSystem.Initialize; ignored.");
        }

        // Also runs on Play Mode entry so a disabled domain reload cannot leak a previous session's instance.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearStaticState()
        {
            Current = null;
            _catalog = null;
            _warnedUninitialized = false;
#if UNITY_EDITOR
            _snapshots = null;
#endif
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private static void CaptureSnapshots(SoundCatalog catalog)
        {
#if UNITY_EDITOR
            var entries = catalog.Entries;
            _snapshots = new DefSnapshot[entries.Count];
            for (var i = 0; i < _snapshots.Length; i++) _snapshots[i] = new DefSnapshot(entries[i].Definition);
#endif
        }

#if UNITY_EDITOR
        // Editor only: a definition edited while playing (clips, spatial mode) is prepared again.
        private sealed class DefSnapshot
        {
            internal readonly SoundDef Def;
            private AudioClip[] _clips;
            private SpatialMode _spatial;

            internal DefSnapshot(SoundDef def)
            {
                Def = def;
                Capture();
            }

            internal void Capture()
            {
                var clips = Def.EffectiveClips;
                _clips = clips == null ? Array.Empty<AudioClip>() : (AudioClip[])clips.Clone();
                _spatial = Def.EffectiveSpatial;
            }

            internal bool Changed()
            {
                if (Def.EffectiveSpatial != _spatial) return true;
                var clips = Def.EffectiveClips;
                var length = clips == null ? 0 : clips.Length;
                if (length != _clips.Length) return true;
                for (var i = 0; i < length; i++)
                    if (!ReferenceEquals(clips[i], _clips[i])) return true;
                return false;
            }
        }

        private static DefSnapshot[] _snapshots;

        private void CheckEditedDefinitions()
        {
            var snapshots = _snapshots;
            if (snapshots == null || !ReferenceEquals(this, Current)) return;
            for (var i = 0; i < snapshots.Length; i++)
            {
                var snapshot = snapshots[i];
                if (snapshot.Def == null || !snapshot.Changed()) continue;
                try { Prepare(snapshot.Def); }
                catch (Exception e) { Debug.LogException(e); }
                RunPrepareHook(_config, snapshot.Def);
                snapshot.Capture(); // after the hook, which may assign RuntimeClips
            }
        }
#endif
    }
}
