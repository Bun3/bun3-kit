// Charge sounds: a progress-driven loop voice composed with start/complete/cancel one-shots.
using System;
using UnityEngine;

namespace Bun3.Unity.Audio
{
    public sealed partial class SoundSystem
    {
        /// <summary>Starts a 2D (or loop-definition-default) charge sound. Returns an invalid handle when the loop cannot play.</summary>
        /// <param name="def">Charge definition; its Loop is required.</param>
        public ChargeSoundHandle PlayCharge(ChargeSoundDef def) => PlayChargeCore(def, Vector3.zero, null);

        /// <summary>Starts a charge sound at a fixed world position. Returns an invalid handle when the loop cannot play.</summary>
        /// <param name="def">Charge definition; its Loop is required.</param>
        /// <param name="position">Fixed world position shared by every part of the charge.</param>
        public ChargeSoundHandle PlayCharge(ChargeSoundDef def, Vector3 position) => PlayChargeCore(def, position, null);

        /// <summary>Starts a charge sound tracking a transform every frame. Returns an invalid handle when the loop cannot play.</summary>
        /// <param name="def">Charge definition; its Loop is required.</param>
        /// <param name="follow">Transform tracked by every part of the charge.</param>
        public ChargeSoundHandle PlayCharge(ChargeSoundDef def, Transform follow)
            => PlayChargeCore(def, follow != null ? follow.position : Vector3.zero, follow);

        private ChargeSoundHandle PlayChargeCore(ChargeSoundDef def, Vector3 position, Transform follow)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SoundSystem));
            }
            if (def == null || def.Loop == null)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.LogWarning("SoundSystem.PlayCharge: def or its Loop is null; returning an invalid handle.");
#endif
                return ChargeSoundHandle.Invalid;
            }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (!def.Loop.EffectiveLoop)
            {
                Debug.LogWarning("SoundSystem.PlayCharge: Loop definition does not loop; the charge ends with its clip.");
            }
#endif
            // Curve-zero volume goes in before the source starts; pitch follows in the same call.
            var loop = PlayCore(def.Loop, position, follow, 0f, null, def.VolumeAt(0f));
            if (!TryGetSlot(loop, out var slot))
            {
                return ChargeSoundHandle.Invalid;
            }
            var charge = new ChargeSoundHandle(loop, def, Table.Slots[slot].Pitch);
            ApplyChargePitch(slot, charge, 0f);
            if (def.Start != null)
            {
                PlayCore(def.Start, position, follow, 0f);
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
            Table.Slots[slot].VolumeScale = charge.Def.VolumeAt(progress);
            ApplyChargePitch(slot, charge, progress);
        }

        private void ApplyChargePitch(int slot, in ChargeSoundHandle charge, float progress)
        {
            var pitch = Mathf.Max(0f, charge.BasePitch * charge.Def.PitchScaleAt(progress));
            Table.Slots[slot].Pitch = pitch;
            SetSourcePitch(slot, pitch);
        }
    }
}
