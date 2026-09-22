using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bun3.Unity.Audio.SteamAudio
{
    /// <summary>Explicit cold-path decoded PCM cache with a caller-specified byte budget.</summary>
    public sealed class SteamAudioClipCache : IDisposable
    {
        internal sealed class Pcm
        {
            internal readonly float[] Samples;
            internal readonly int Frames, Channels, Rate;
            internal Pcm(AudioClip clip)
            {
                Frames = clip.samples; Channels = clip.channels; Rate = clip.frequency;
                Samples = new float[checked(Frames * Channels)];
                if (!clip.GetData(Samples, 0)) throw new ArgumentException("Clip PCM is unavailable; use loaded, non-streaming Decompress On Load clips.", nameof(clip));
                for (int i = 0; i < Samples.Length; i++)
                    if (float.IsNaN(Samples[i]) || float.IsInfinity(Samples[i])) throw new ArgumentException("Clip PCM must be finite.", nameof(clip));
            }
        }
        private readonly long _maximum;
        private readonly Dictionary<AudioClip, Pcm> _clips = new();
        private bool _disposed;

        /// <summary>Creates an empty cache. The byte budget counts managed float PCM, excluding Unity's own clip memory.</summary>
        public SteamAudioClipCache(long maximumDecodedBytes)
        {
            if (maximumDecodedBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumDecodedBytes));
            _maximum = maximumDecodedBytes;
        }
        /// <summary>Gets the committed managed PCM byte count.</summary>
        public long DecodedBytes { get; private set; }

        /// <summary>Gets whether a live clip has committed PCM in this cache without allocating.</summary>
        public bool IsPrepared(AudioClip clip) => TryGet(clip, out _);

        /// <summary>Gets whether every nonnull effective positional clip is prepared without allocating.</summary>
        public bool IsDefinitionPrepared(SoundDef definition)
        {
            if (_disposed || definition == null) return false;
            if (definition.EffectiveSpatial == SpatialMode.None) return true;
            var clips = definition.EffectiveClips;
            if (clips == null) return true;
            for (int i = 0; i < clips.Length; i++)
                if (clips[i] != null && !IsPrepared(clips[i])) return false;
            return true;
        }

        /// <summary>
        /// Prepares a definition's nonnull effective positional clips on the main-thread cold path.
        /// Nonpositional definitions bypass this cache. Failures leave all previously committed PCM unchanged.
        /// </summary>
        public void PrepareDefinition(SoundDef definition)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SteamAudioClipCache));
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (IsDefinitionPrepared(definition)) return;
            var clips = definition.EffectiveClips;
            var candidates = new List<AudioClip>(clips.Length);
            for (int i = 0; i < clips.Length; i++)
                if (clips[i] != null && !IsPrepared(clips[i])) candidates.Add(clips[i]);
            PrepareClips(candidates);
        }

        /// <summary>
        /// Copies loaded mono/stereo, non-streaming Decompress On Load clips on the main thread. Duplicate clips
        /// are counted once. Budget/format/read failures leave the previously committed cache unchanged.
        /// Stereo positional clips are later downmixed to one point source; nonpositional clips bypass this output.
        /// </summary>
        public void PrepareClips(IReadOnlyList<AudioClip> clips)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SteamAudioClipCache));
            if (clips == null) throw new ArgumentNullException(nameof(clips));
            var unique = CollectUnpreparedClips(clips, out long bytes);
            var prepared = DecodeClips(unique);
            foreach (var pair in prepared) _clips.Add(pair.Key, pair.Value);
            DecodedBytes = bytes;
        }

        private HashSet<AudioClip> CollectUnpreparedClips(IReadOnlyList<AudioClip> clips, out long bytes)
        {
            var unique = new HashSet<AudioClip>();
            bytes = DecodedBytes;
            for (int i = 0; i < clips.Count; i++)
            {
                var clip = clips[i];
                if (clip == null) throw new ArgumentException("A live clip is required.", nameof(clips));
                if (_clips.ContainsKey(clip) || !unique.Add(clip)) continue;
                if (clip.loadState != AudioDataLoadState.Loaded || clip.loadType != AudioClipLoadType.DecompressOnLoad)
                    throw new ArgumentException("Clips must be loaded, non-streaming Decompress On Load assets.", nameof(clips));
                if (clip.channels < 1 || clip.channels > 2 || clip.samples <= 0 || clip.frequency <= 0)
                    throw new ArgumentException("Only nonempty mono/stereo clips are supported.", nameof(clips));
                bytes = checked(bytes + (long)clip.samples * clip.channels * sizeof(float));
                if (bytes > _maximum) throw new InvalidOperationException("Prepared clip PCM exceeds the configured byte budget.");
            }
            return unique;
        }

        private static List<KeyValuePair<AudioClip, Pcm>> DecodeClips(HashSet<AudioClip> clips)
        {
            var prepared = new List<KeyValuePair<AudioClip, Pcm>>(clips.Count);
            foreach (var clip in clips) prepared.Add(new KeyValuePair<AudioClip, Pcm>(clip, new Pcm(clip)));
            return prepared;
        }

        internal bool TryGet(AudioClip clip, out Pcm pcm)
        {
            pcm = null;
            return !_disposed && clip != null && _clips.TryGetValue(clip, out pcm);
        }

        /// <summary>Releases cached references without destroying caller-owned clips. Active readers retain their immutable PCM.</summary>
        public void Dispose() { _disposed = true; _clips.Clear(); DecodedBytes = 0; }
    }
}
