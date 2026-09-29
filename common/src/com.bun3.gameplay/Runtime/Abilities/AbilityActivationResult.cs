#nullable enable
using Bun3.Gameplay.Effects;

namespace Bun3.Gameplay.Abilities
{
    /// <summary>Outcome of an ability activation attempt. Gate failures are results, not exceptions.</summary>
    public enum AbilityActivationResult : byte
    {
        /// <summary>Gates passed; costs were paid and the ability's effects were enqueued.</summary>
        Success = 0,
        /// <summary>The unit id did not resolve.</summary>
        UnknownUnit = 1,
        /// <summary>The ability id is outside the catalog.</summary>
        UnknownAbility = 2,
        /// <summary>No origin currently grants this ability to the unit.</summary>
        NotGranted = 3,
        /// <summary>The unit lacks a tag the ability requires.</summary>
        MissingTag = 4,
        /// <summary>The unit owns a tag the ability blocks on.</summary>
        BlockedByTag = 5,
        /// <summary>An activation condition did not hold.</summary>
        ConditionFailed = 6,
        /// <summary>At least one cost exceeded the unit's Base value of that attribute, or the unit does not
        /// declare the attribute.</summary>
        CostInsufficient = 7,
        /// <summary>The unit owns a tag granted by the ability's cooldown effect — including one granted by
        /// another effect sharing that tag.</summary>
        OnCooldown = 8,
    }

    /// <summary>Result of one queued ability activation, readable after the tick that processed it.</summary>
    public readonly struct AbilityActivation
    {
        internal AbilityActivation(TargetId unitId, int abilityId, AbilityActivationResult result)
        {
            UnitId = unitId;
            AbilityId = abilityId;
            Result = result;
        }

        /// <summary>Unit that requested the activation.</summary>
        public TargetId UnitId { get; }

        /// <summary>Requested ability id.</summary>
        public int AbilityId { get; }

        /// <summary>Gate outcome; <see cref="AbilityActivationResult.Success"/> means costs were paid and
        /// effects enqueued.</summary>
        public AbilityActivationResult Result { get; }
    }
}
