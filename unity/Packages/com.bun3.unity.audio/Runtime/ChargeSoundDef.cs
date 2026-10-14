using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>
    /// Progress-driven charge sound: a looping layer whose volume and pitch follow a 0..1
    /// progress value, plus optional start, complete and cancel one-shots. Each part is a
    /// regular <see cref="SoundDef"/> and keeps its own clips, variation, routing, spatial
    /// mode and profiles. Played with <see cref="SoundSystem.PlayCharge(ChargeSoundDef)"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Bun3/Audio/Charge Sound Def", fileName = "ChargeSoundDef")]
    public sealed class ChargeSoundDef : ScriptableObject
    {
        /// <summary>Required looping layer. Its definition should enable Loop.</summary>
        public SoundDef Loop;

        /// <summary>Optional one-shot played when the charge starts.</summary>
        public SoundDef Start;

        /// <summary>Optional one-shot played by <see cref="ChargeSoundHandle.Complete"/>.</summary>
        public SoundDef Complete;

        /// <summary>Optional one-shot played by <see cref="ChargeSoundHandle.Cancel"/>.</summary>
        public SoundDef Cancel;

        /// <summary>Loop volume multiplier (Y) by progress (X, 0..1). Null or empty is a constant 1.</summary>
        public AnimationCurve VolumeByProgress = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        /// <summary>Multiplier (Y) on the loop's rolled pitch by progress (X, 0..1). Null or empty is a constant 1.</summary>
        public AnimationCurve PitchByProgress = AnimationCurve.Linear(0f, 0.667f, 1f, 1f);

        /// <summary>Seconds the loop fades out over when the charge completes.</summary>
        public float CompleteFadeOut = 0.05f;

        /// <summary>Seconds the loop fades out over when the charge is cancelled.</summary>
        public float CancelFadeOut = 0.15f;

        internal float VolumeAt(float progress) => Mathf.Max(0f, Evaluate(VolumeByProgress, progress));

        internal float PitchScaleAt(float progress) => Evaluate(PitchByProgress, progress);

        private static float Evaluate(AnimationCurve curve, float progress)
        {
            if (curve == null || curve.length == 0)
            {
                return 1f;
            }
            return curve.Evaluate(float.IsNaN(progress) ? 0f : Mathf.Clamp01(progress));
        }
    }
}
