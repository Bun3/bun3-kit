using UnityEditor;
using UnityEngine;

namespace Bun3.Unity.Audio.Editor
{
    /// <summary>
    /// Editor preview of a <see cref="SoundDef"/> through one hidden 2D <see cref="AudioSource"/>, so sounds can be
    /// compared by ear. The player's group volume is unknown in the editor and is not applied.
    /// </summary>
    internal static class SoundPreview
    {
        private static readonly System.Random Rng = new();
        private static AudioSource _source;
        private static SoundDef _lastDef;
        private static int _lastClip = -1;

        static SoundPreview() => AssemblyReloadEvents.beforeAssemblyReload += Stop;

        /// <summary>
        /// Plays one of the definition's direct clips, avoiding its previous pick, at a rolled
        /// <see cref="SoundDef.EffectiveVolume"/> and <see cref="SoundDef.EffectivePitch"/>, replacing any earlier preview.
        /// A definition without direct clips plays nothing.
        /// </summary>
        public static void Play(SoundDef def)
        {
            var clip = PickClip(def);
            if (clip == null) return;
            if (_source == null)
            {
                var host = EditorUtility.CreateGameObjectWithHideFlags("Bun3 Sound Preview", HideFlags.HideAndDontSave, typeof(AudioSource));
                _source = host.GetComponent<AudioSource>();
                _source.playOnAwake = false;
                _source.ignoreListenerPause = true;
            }
            _source.clip = clip;
            _source.volume = def.EffectiveVolume.Roll(Rng);
            _source.pitch = def.EffectivePitch.Roll(Rng);
            _source.Play();
        }

        /// <summary>Stops the preview.</summary>
        public static void Stop()
        {
            if (_source != null) Object.DestroyImmediate(_source.gameObject);
            _source = null;
        }

        private static AudioClip PickClip(SoundDef def)
        {
            var clips = def != null ? def.Clips : null;
            var count = 0;
            if (clips != null)
                foreach (var clip in clips)
                    if (clip != null) count++;
            if (count == 0) return null;
            int index;
            do index = Rng.Next(clips.Length);
            while (clips[index] == null || (count > 1 && def == _lastDef && index == _lastClip));
            _lastDef = def;
            _lastClip = index;
            return clips[index];
        }
    }
}
