namespace Bun3.Unity.Audio
{
    /// <summary>
    /// Reference to a charge sound started by <see cref="SoundSystem.PlayCharge(ChargeSoundDef)"/>.
    /// Valid while its loop plays and the charge has not been completed or cancelled. Once the
    /// loop is stolen by voice limits, ends, or its system is disposed, every member no-ops (or
    /// reports false) like a stale <see cref="SoundHandle"/>, and no completion sound plays.
    /// </summary>
    public readonly struct ChargeSoundHandle
    {
        internal readonly SoundHandle Loop;
        internal readonly ChargeSoundDef Def;
        internal readonly float BasePitch;

        internal ChargeSoundHandle(SoundHandle loop, ChargeSoundDef def, float basePitch)
        {
            Loop = loop;
            Def = def;
            BasePitch = basePitch;
        }

        /// <summary>A handle that never refers to a charge.</summary>
        public static ChargeSoundHandle Invalid => default;

        /// <summary>True while the loop plays and the charge has not been completed or cancelled.</summary>
        public bool IsValid => Loop.Owner != null && Loop.Owner.IsChargeActive(this, out _);

        /// <summary>
        /// Applies the definition's curves at <paramref name="progress"/>, clamped to 0..1 (NaN is 0).
        /// Takes effect immediately without smoothing; progress may decrease.
        /// </summary>
        public void SetProgress(float progress) => Loop.Owner?.SetChargeProgress(this, progress);

        /// <summary>
        /// Fades the loop out over <see cref="ChargeSoundDef.CompleteFadeOut"/> and plays the
        /// Complete sound where the loop is (tracking its transform while that is alive). Returns
        /// the Complete sound's handle, or an invalid handle when the definition has none or the
        /// charge is no longer valid. Ends the charge; later calls do nothing.
        /// </summary>
        public SoundHandle Complete() => Loop.Owner != null ? Loop.Owner.EndCharge(this, completed: true) : SoundHandle.Invalid;

        /// <summary>
        /// Fades the loop out over <see cref="ChargeSoundDef.CancelFadeOut"/> and plays the
        /// Cancel sound where the loop is (tracking its transform while that is alive). Returns
        /// the Cancel sound's handle, or an invalid handle when the definition has none or the
        /// charge is no longer valid. Ends the charge; later calls do nothing.
        /// </summary>
        public SoundHandle Cancel() => Loop.Owner != null ? Loop.Owner.EndCharge(this, completed: false) : SoundHandle.Invalid;
    }
}
