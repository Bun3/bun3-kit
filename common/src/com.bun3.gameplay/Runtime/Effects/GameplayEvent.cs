#nullable enable
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Effects
{
    /// <summary>
    /// One gameplay event raised by an effect application. <see cref="Magnitude"/> and
    /// <see cref="NominalMagnitude"/> are measured differently and can disagree — see each
    /// property's remarks. Damage reads as a negative magnitude, healing as positive.
    /// </summary>
    public readonly struct GameplayEvent
    {
        /// <summary>Creates a gameplay event.</summary>
        /// <param name="tag">Event tag.</param>
        /// <param name="source">Caster of the effect that raised the event.</param>
        /// <param name="target">Target the effect was applied to.</param>
        /// <param name="magnitude">Signed Base change after clamping, across the whole application.</param>
        /// <param name="nominalMagnitude">Signed sum of the modifier magnitudes targeting this attribute, before clamping.</param>
        public GameplayEvent(
            GameplayTag tag, TargetId source, TargetId target, BigNum magnitude, BigNum nominalMagnitude)
        {
            Tag = tag;
            Source = source;
            Target = target;
            Magnitude = magnitude;
            NominalMagnitude = nominalMagnitude;
        }

        /// <summary>Event tag.</summary>
        public GameplayTag Tag { get; }

        /// <summary>Caster of the effect that raised the event.</summary>
        public TargetId Source { get; }

        /// <summary>Target the effect was applied to.</summary>
        public TargetId Target { get; }

        /// <summary>
        /// Signed Base change that actually landed, after clamping, measured across the whole effect
        /// application — the attribute's Base before minus its Base after. This includes any change made
        /// by an <see cref="IExecutionCalc"/> execution, not only by <see cref="CompiledModifier"/>
        /// modifiers. For an effect with both modifiers and executions touching the same attribute,
        /// <see cref="Magnitude"/> reflects the combined effect and can differ from
        /// <see cref="NominalMagnitude"/>, which counts modifiers only.
        /// </summary>
        public BigNum Magnitude { get; }

        /// <summary>
        /// Signed sum of the modifier magnitudes targeting this attribute, before clamping. Unlike
        /// <see cref="Magnitude"/>, this does not include any change made by an execution
        /// (<see cref="IExecutionCalc"/>) — an effect whose modifiers evaluate to zero net magnitude
        /// reports zero here even if an execution on the same effect changed the attribute's Base.
        /// </summary>
        public BigNum NominalMagnitude { get; }
    }
}
