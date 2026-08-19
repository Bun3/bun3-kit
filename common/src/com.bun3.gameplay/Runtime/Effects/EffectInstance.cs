#nullable enable
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;

namespace Bun3.Gameplay.Effects
{
    /// <summary>
    /// Runtime instance of one effect applied to a target. The monotonically increasing
    /// <see cref="Id"/> issued by World is the basis of the canonical aggregation order.
    /// Creation and return go through an internal static pool.
    /// </summary>
    public sealed class EffectInstance : IAttributeModifierSource
    {
        // ponytail: process-global static pool — assumes one world per thread (unsafe if multiple
        // worlds share a thread concurrently or multiple threads touch the pool). Move the pool to
        // a world/pipeline-owned instance when needed.
        private static readonly Stack<EffectInstance> Pool = new Stack<EffectInstance>();

        private EffectInstance()
        {
        }

        /// <summary>Monotonically increasing id issued by World.</summary>
        public ulong Id { get; private set; }

        /// <summary>Effect spec id.</summary>
        public int SpecId { get; private set; }

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

        /// <summary>Rents an instance from the pool and initializes its fields.</summary>
        internal static EffectInstance Rent(
            ulong id, int specId, TargetId source, int level, int stack,
            int remainingTicks, int periodCountdown, long createdTick)
        {
            var instance = Pool.Count > 0 ? Pool.Pop() : new EffectInstance();
            instance.Id = id;
            instance.SpecId = specId;
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
        internal static void Return(EffectInstance instance)
        {
            instance.Enabled = false;
            Pool.Push(instance);
        }
    }
}
