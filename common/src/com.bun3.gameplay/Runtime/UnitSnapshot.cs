#nullable enable
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Effects;

namespace Bun3.Gameplay
{
    /// <summary>
    /// Immutable deep copy of one unit's deterministic state, created by <see cref="Unit.CreateSnapshot"/>.
    /// Composes the effect-target snapshot with the ability grants and the owner id — the same
    /// composition the replication schema uses. Opaque token: callers hold and pass instances
    /// without inspecting them.
    /// </summary>
    public sealed class UnitSnapshot
    {
        internal UnitSnapshot(TargetId unitId, EffectTargetSnapshot effects, AbilitySetSnapshot abilities, ulong ownerId)
        {
            UnitId = unitId;
            Effects = effects;
            Abilities = abilities;
            OwnerId = ownerId;
        }

        /// <summary>Unit this snapshot belongs to. Used to reject restoring onto another unit.</summary>
        internal TargetId UnitId { get; }

        /// <summary>Attribute and active-effect state.</summary>
        internal EffectTargetSnapshot Effects { get; }

        /// <summary>Ability grants.</summary>
        internal AbilitySetSnapshot Abilities { get; }

        /// <summary>Owner identity, 0 when unowned.</summary>
        internal ulong OwnerId { get; }
    }

    /// <summary>
    /// Immutable deep copy of an entire world's deterministic state — every unit plus the counters
    /// that live outside a unit (pipeline tick, next issued instance id, RNG state). The pending apply queue is
    /// deliberately excluded: a restore starts from a drained queue, which is why
    /// <see cref="World.CreateSnapshot"/> requires one.
    /// </summary>
    public sealed class WorldSnapshot
    {
        internal WorldSnapshot(UnitSnapshot[] units, long currentTick, ulong nextInstanceId, byte[] rngState)
        {
            Units = units;
            CurrentTick = currentTick;
            NextInstanceId = nextInstanceId;
            RngState = rngState;
        }

        internal UnitSnapshot[] Units { get; }

        internal long CurrentTick { get; }

        internal ulong NextInstanceId { get; }

        internal byte[] RngState { get; }
    }
}
