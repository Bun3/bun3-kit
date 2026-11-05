// Charge sounds: a progress-driven loop voice composed with start/complete/cancel one-shots.
using System;
using UnityEngine;

namespace Bun3.Unity.Audio
{
    public sealed partial class SoundSystem
    {
        // spatial: None forces 2D, Positional maps like the static PlayAt, null keeps each part's own mode.
        internal ChargeSoundHandle PlayChargeCore(SoundDef loop, SoundDef start, SoundDef complete, SoundDef cancel,
            Vector3 position, Transform follow, SpatialMode? spatial = null)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SoundSystem));
            }
            if (loop == null)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.LogWarning("SoundSystem.PlayCharge: loop definition is null; returning an invalid handle.");
#endif
                return ChargeSoundHandle.Invalid;
            }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (!loop.EffectiveLoop)
            {
                Debug.LogWarning("SoundSystem.PlayCharge: Loop definition does not loop; the charge ends with its clip.");
            }
#endif
            // Curve-zero volume goes in before the source starts; pitch follows in the same call.
            var handle = PlayCore(loop, position, follow, 0f, PartSpatial(loop, spatial), loop.Charge.VolumeAt(0f));
            if (!TryGetSlot(handle, out var slot))
            {
                return ChargeSoundHandle.Invalid;
            }
            var charge = new ChargeSoundHandle(handle, loop, complete, cancel, Table.Slots[slot].Pitch, spatial);
            ApplyChargePitch(slot, charge, 0f);
            if (start != null)
            {
                PlayCore(start, position, follow, 0f, PartSpatial(start, spatial));
            }
            return charge;
        }

        internal bool IsChargeActive(in ChargeSoundHandle charge, out int slot)
            => TryGetSlot(charge.Loop, out slot) && Table.Slots[slot].State != VoiceState.FadingOut;

        internal void SetChargeProgress(in ChargeSoundHandle charge, float progress)
        {
            if (!IsChargeActive(charge, out var slot))
            {
                return;
            }
            Table.Slots[slot].VolumeScale = charge.LoopDef.Charge.VolumeAt(progress);
            ApplyChargePitch(slot, charge, progress);
        }

        internal SoundHandle EndCharge(in ChargeSoundHandle charge, bool completed)
        {
            if (!IsChargeActive(charge, out var slot))
            {
                return SoundHandle.Invalid;
            }
            var settings = charge.LoopDef.Charge;
            var follow = Table.Slots[slot].Follow;
            var position = _sources[slot].transform.position;
            // FadingOut is what makes IsChargeActive false; Stop's fade restart never runs twice.
            Table.BeginFadeOut(slot, completed ? settings.CompleteFadeOut : settings.CancelFadeOut);
            var stinger = completed ? charge.CompleteDef : charge.CancelDef;
            if (stinger == null)
            {
                return SoundHandle.Invalid;
            }
            var spatial = PartSpatial(stinger, charge.Spatial);
            return follow != null
                ? PlayCore(stinger, follow.position, follow, 0f, spatial)
                : PlayCore(stinger, position, null, 0f, spatial);
        }

        private void ApplyChargePitch(int slot, in ChargeSoundHandle charge, float progress)
        {
            var pitch = Mathf.Max(0f, charge.BasePitch * charge.LoopDef.Charge.PitchScaleAt(progress));
            Table.Slots[slot].Pitch = pitch;
            SetSourcePitch(slot, pitch);
        }

        private static SpatialMode? PartSpatial(SoundDef def, SpatialMode? spatial)
            => spatial == SpatialMode.Positional && def.EffectiveSpatial == SpatialMode.None ? SpatialMode.None : spatial;
    }
}
