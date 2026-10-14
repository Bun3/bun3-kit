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
    }
}
