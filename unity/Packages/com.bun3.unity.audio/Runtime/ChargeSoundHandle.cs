namespace Bun3.Unity.Audio
{
    /// <summary>
    /// Reference to a charge sound started by <see cref="SoundSystem.PlayCharge(string, string)"/>.
    /// Valid while its loop plays and the charge has not been completed, cancelled or stopped. Once the
    /// loop is stolen by voice limits, ends, or its system is disposed, every member no-ops (or
    /// reports false) like a stale <see cref="SoundHandle"/>, and no completion sound plays.
    /// </summary>
    public readonly struct ChargeSoundHandle
    {
        internal readonly SoundHandle Loop;
        internal readonly SoundDef LoopDef;
        internal readonly SoundDef CompleteDef;
        internal readonly SoundDef CancelDef;
        internal readonly float BasePitch;
        internal readonly SpatialMode? Spatial;

        internal ChargeSoundHandle(SoundHandle loop, SoundDef loopDef, SoundDef completeDef, SoundDef cancelDef, float basePitch, SpatialMode? spatial)
        {
            Loop = loop;
            LoopDef = loopDef;
            CompleteDef = completeDef;
            CancelDef = cancelDef;
            BasePitch = basePitch;
            Spatial = spatial;
        }

        /// <summary>A handle that never refers to a charge.</summary>
        public static ChargeSoundHandle Invalid => default;

        /// <summary>True while the loop plays and the charge has not been completed, cancelled or stopped.</summary>
        public bool IsValid => Loop.Owner != null && Loop.Owner.IsChargeActive(this, out _);

        /// <summary>
        /// Applies the loop definition's <see cref="SoundDef.Charge"/> curves at <paramref name="progress"/>,
        /// clamped to 0..1 (NaN is 0). Takes effect immediately without smoothing; progress may decrease.
        /// </summary>
        public void SetProgress(float progress) => Loop.Owner?.SetChargeProgress(this, progress);

        /// <summary>
        /// Fades the loop out over <see cref="SoundChargeSettings.CompleteFadeOut"/> and plays the
        /// Complete sound where the loop is (tracking its transform while that is alive). Returns
        /// the Complete sound's handle, or an invalid handle when there is none or the charge is
        /// no longer valid. Ends the charge; later calls do nothing.
        /// </summary>
        public SoundHandle Complete() => Loop.Owner != null ? Loop.Owner.EndCharge(this, completed: true) : SoundHandle.Invalid;

        /// <summary>
        /// Fades the loop out over <see cref="SoundChargeSettings.CancelFadeOut"/> and plays the
        /// Cancel sound where the loop is (tracking its transform while that is alive). Returns
        /// the Cancel sound's handle, or an invalid handle when there is none or the charge is
        /// no longer valid. Ends the charge; later calls do nothing.
        /// </summary>
        public SoundHandle Cancel() => Loop.Owner != null ? Loop.Owner.EndCharge(this, completed: false) : SoundHandle.Invalid;

        /// <summary>
        /// Stops the loop at once without a Complete or Cancel sound, cutting short a fade already in progress.
        /// Ends the charge; later Complete and Cancel calls do nothing.
        /// </summary>
        public void Stop() => Loop.Stop();
    }
}
