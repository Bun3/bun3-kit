#nullable enable
namespace Bun3.Gameplay.Effects
{
    /// <summary>Kind of an effect-instance lifecycle event.</summary>
    public enum EffectLifecycleKind : byte
    {
        /// <summary>Newly applied.</summary>
        Applied = 0,
        /// <summary>Expired normally after its duration elapsed.</summary>
        Expired = 1,
        /// <summary>Removed early (e.g. by an ongoing condition).</summary>
        RemovedPrematurely = 2,
        /// <summary>Stack count changed.</summary>
        StackChanged = 3,
    }

    /// <summary>One lifecycle event of an effect instance.</summary>
    public readonly struct EffectLifecycleEvent
    {
        /// <summary>Creates an event.</summary>
        /// <param name="kind">Event kind.</param>
        /// <param name="instanceId">Id of the affected effect instance.</param>
        /// <param name="specId">Effect spec id.</param>
        /// <param name="stack">Stack count at the time of the event.</param>
        public EffectLifecycleEvent(EffectLifecycleKind kind, ulong instanceId, int specId, int stack)
        {
            Kind = kind;
            InstanceId = instanceId;
            SpecId = specId;
            Stack = stack;
        }

        /// <summary>Event kind.</summary>
        public EffectLifecycleKind Kind { get; }

        /// <summary>Id of the affected effect instance.</summary>
        public ulong InstanceId { get; }

        /// <summary>Effect spec id.</summary>
        public int SpecId { get; }

        /// <summary>Stack count at the time of the event.</summary>
        public int Stack { get; }
    }
}
