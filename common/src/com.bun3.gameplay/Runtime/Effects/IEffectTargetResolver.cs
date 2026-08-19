#nullable enable
using System.Collections.Generic;

namespace Bun3.Gameplay.Effects
{
    /// <summary>
    /// Contract for resolving an <see cref="EffectTarget"/> from a <see cref="TargetId"/>.
    /// <see cref="TargetIds"/> must always stay in ascending order.
    /// </summary>
    public interface IEffectTargetResolver
    {
        /// <summary>Resolves an <see cref="EffectTarget"/> by target id.</summary>
        /// <param name="id">Target id to resolve.</param>
        /// <param name="target">Resolved target.</param>
        /// <returns>True if the target was found.</returns>
        bool TryResolve(TargetId id, out EffectTarget? target);

        /// <summary>All target ids known to this resolver. Ascending order is a contract.</summary>
        IReadOnlyList<TargetId> TargetIds { get; }
    }
}
