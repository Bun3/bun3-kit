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
}
