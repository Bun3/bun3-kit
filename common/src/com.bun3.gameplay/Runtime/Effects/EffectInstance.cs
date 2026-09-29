#nullable enable
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;

namespace Bun3.Gameplay.Effects
{
    /// <summary>
    /// Runtime instance of one effect applied to a target. The monotonically increasing
    /// <see cref="Id"/> issued by World is the basis of the canonical aggregation order.
    /// Instances are pooled per <see cref="EffectPipeline"/>; one never moves between pipelines.
    /// </summary>
    public sealed class EffectInstance : IAttributeModifierSource
    {
        private EffectInstance()
        {
        }

        /// <summary>Monotonically increasing id issued by World.</summary>
        public ulong Id { get; private set; }

        /// <summary>Effect id.</summary>
        public int EffectId { get; private set; }

        /// <summary>Remaining ticks of a Duration effect. Meaningless for Infinite/Instant.</summary>
        public int RemainingTicks { get; internal set; }

        /// <summary>Ticks until the next periodic execution. -1 when there is no periodic execution.</summary>
        public int PeriodCountdown { get; internal set; }

        /// <summary>Current stack count.</summary>
        public int Stack { get; internal set; }

        /// <summary>Effect level. The internal setter is reserved for LevelFromStack merge sync.</summary>
        public int Level { get; internal set; }

        /// <summary>Caster that applied this effect.</summary>
        public TargetId Source { get; private set; }

        /// <summary>Whether this instance participates in aggregation (ongoing conditions may exclude it).
        /// When false, aggregation skips it.</summary>
        public bool Enabled { get; internal set; }

        /// <summary>
        /// Pipeline tick this instance was created on. Lifetime (RemainingTicks) and period
        /// (PeriodCountdown) do not advance on the creation tick; they advance from the next tick.
        /// </summary>
        public long CreatedTick { get; private set; }

        /// <summary>Rents an instance from the pool (or creates one when the pool is absent or empty)
        /// and initializes its fields.</summary>
        internal static EffectInstance Rent(
            Stack<EffectInstance>? pool, ulong id, int effectId, TargetId source, int level, int stack,
            int remainingTicks, int periodCountdown, long createdTick)
        {
            var instance = pool != null && pool.Count > 0 ? pool.Pop() : new EffectInstance();
            instance.Id = id;
            instance.EffectId = effectId;
            instance.Source = source;
            instance.Level = level;
            instance.Stack = stack;
            instance.RemainingTicks = remainingTicks;
            instance.PeriodCountdown = periodCountdown;
            instance.CreatedTick = createdTick;
            instance.Enabled = true;
            return instance;
        }

        /// <summary>Disables the instance and returns it to the pool.</summary>
        internal static void Return(Stack<EffectInstance> pool, EffectInstance instance)
        {
            instance.Enabled = false;
            pool.Push(instance);
        }
    }
}
