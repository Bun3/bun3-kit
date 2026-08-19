#nullable enable
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Effects
{
    /// <summary>
    /// One gameplay event raised by an effect application. <see cref="Magnitude"/> is the signed
    /// Base change that actually landed after clamping; <see cref="NominalMagnitude"/> is the
    /// amount before clamping. Damage reads as a negative magnitude, healing as positive.
    /// </summary>
    public readonly struct GameplayEvent
    {
        /// <summary>Creates a gameplay event.</summary>
        /// <param name="tag">Event tag.</param>
        /// <param name="source">Caster of the effect that raised the event.</param>
        /// <param name="target">Target the effect was applied to.</param>
        /// <param name="magnitude">Signed Base change after clamping.</param>
        /// <param name="nominalMagnitude">Signed Base change before clamping.</param>
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

        /// <summary>Signed Base change after clamping.</summary>
        public BigNum Magnitude { get; }

        /// <summary>Signed Base change before clamping.</summary>
        public BigNum NominalMagnitude { get; }
    }
}
