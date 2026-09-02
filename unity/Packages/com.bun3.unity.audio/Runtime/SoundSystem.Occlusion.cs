// Occlusion integration: round-robin provider evaluation and LPF/volume application.
// Evaluation targets are written to VoiceTable slots; smoothing happens in VoiceTable.Tick.
using UnityEngine;

namespace Bun3.Unity.Audio
{
    public sealed partial class SoundSystem
    {
        private IOcclusionProvider _occlusionProvider;
        private AudioLowPassFilter[] _lowPassFilters;
        private Transform _listener;
        private float _nextListenerSearchTime;
        private int _occlusionCursor;

        private const float OpenCutoffHz = 22000f;

        private void InitializeOcclusion()
        {
            if (_config.OcclusionChecksPerFrame <= 0)
            {
                // Off switch: no filters attached, no provider constructed, EvaluateOcclusion
                // early-returns. Lets external spatializer adapters own low-pass/attenuation.
                return;
            }
            _occlusionProvider = _config.OcclusionProvider
                ?? new RaycastOcclusionProvider(_config.OcclusionMask);
            _lowPassFilters = new AudioLowPassFilter[_sources.Length];
            for (var i = 0; i < _sources.Length; i++)
            {
                _lowPassFilters[i] = _sources[i].gameObject.AddComponent<AudioLowPassFilter>();
                _lowPassFilters[i].cutoffFrequency = OpenCutoffHz;
                _lowPassFilters[i].enabled = false;
            }
        }

        /// <summary>
        /// Round-robin occlusion evaluation: up to OcclusionChecksPerFrame occlusion-enabled
        /// voices per call. Listener lookup on loss is the one sanctioned cold-path allocation.
        /// A source's transform position reflects last frame's mirrored Follow target when the
        /// voice is following: evaluating one frame stale self-corrects next frame, well within
        /// what OcclusionSmoothingSeconds already dwarfs.
        /// </summary>
        internal void EvaluateOcclusion()
        {
            var budget = _config.OcclusionChecksPerFrame;
            if (budget <= 0)
            {
                return;
            }
            var listener = ResolveListener();
            if (listener == null)
            {
                return;
            }
            var listenerPos = listener.position;
            var slots = Table.Slots;
            var checkedCount = 0;
            for (var step = 0; step < slots.Length && checkedCount < budget; step++)
            {
                var i = _occlusionCursor;
                _occlusionCursor = (_occlusionCursor + 1) % slots.Length;
                ref var s = ref slots[i];
                if (s.State == VoiceState.Idle || s.Def == null || !s.Def.Occlusion
                    || s.Def.Spatial == SpatialMode.None)
                {
                    continue;
                }
                checkedCount++;
                s.OcclusionTarget = _occlusionProvider.Evaluate(
                    listenerPos, _sources[i].transform.position);
            }
        }

        /// <summary>Volume multiplier for the slot's current occlusion (1 = open).</summary>
        internal float OcclusionVolumeMultiplier(int slot)
        {
            var occ = Table.Slots[slot].OcclusionCurrent;
            return occ <= 0f ? 1f : Mathf.Lerp(1f, _config.OcclusionVolumeAtFull, occ);
        }

        /// <summary>Mirrors the slot's occlusion onto its low-pass filter (enabled only when occluded).</summary>
        internal void ApplyOcclusionFilter(int slot)
        {
            if (_lowPassFilters == null)
            {
                return;
            }
            var occ = Table.Slots[slot].OcclusionCurrent;
            var filter = _lowPassFilters[slot];
            // 0.001 boundary needs no hysteresis: OcclusionCurrent is MoveTowards-smoothed and
            // terminates exactly at 0, and provider flicker is already rate-limited by that
            // same smoothing, so the filter can't chatter on/off from noise crossing this line.
            if (occ <= 0.001f)
            {
                if (filter.enabled)
                {
                    filter.cutoffFrequency = OpenCutoffHz;
                    filter.enabled = false;
                }
                return;
            }
            filter.enabled = true;
            filter.cutoffFrequency = Mathf.Lerp(OpenCutoffHz, _config.OcclusionMuffledCutoffHz, occ);
        }

        /// <summary>Clears a slot's low-pass filter back to fully open before a new voice starts playing on it.</summary>
        internal void ResetOcclusionFilter(int slot)
        {
            if (_lowPassFilters == null)
            {
                return;
            }
            var filter = _lowPassFilters[slot];
            if (filter.enabled)
            {
                filter.cutoffFrequency = OpenCutoffHz;
                filter.enabled = false;
            }
        }

        /// <summary>
        /// Resolves the occlusion listener transform. A found listener is cached and reused
        /// (re-resolved once it is destroyed); when none exists yet in the scene, a
        /// not-found search re-arms at most once per second — the sanctioned cold-path
        /// allocation, throttled so a listener spawned after construction is still picked
        /// up without paying <c>FindAnyObjectByType</c> every tick in the meantime. While no
        /// listener is found, EvaluateOcclusion returns early and every voice's occlusion
        /// value freezes at its last state (no drift back to open) until one reappears.
        /// </summary>
        private Transform ResolveListener()
        {
            if (_config.Listener != null)
            {
                return _config.Listener;
            }
            if (_listener != null)
            {
                return _listener;
            }
            if (Time.unscaledTime < _nextListenerSearchTime)
            {
                return null;
            }
            var found = UnityEngine.Object.FindAnyObjectByType<AudioListener>();
            _listener = found != null ? found.transform : null;
            if (_listener == null)
            {
                _nextListenerSearchTime = Time.unscaledTime + 1f;
            }
            return _listener;
        }
    }
}
