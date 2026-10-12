#nullable enable
using System.Text;

namespace Bun3.Gameplay.Abilities
{
    /// <summary>
    /// Immutable deep copy of one ability set's grants, created by <see cref="AbilitySet.CreateSnapshot"/>.
    /// Rows are kept in canonical order (ability id ascending, then grant origin ascending) so
    /// restore is deterministic. Opaque token — callers hold and pass instances without inspecting them.
    /// </summary>
    public sealed class AbilitySetSnapshot
    {
        internal AbilitySetSnapshot(GrantRow[] grants) => Grants = grants;

        /// <summary>Grant rows in canonical order.</summary>
        internal GrantRow[] Grants { get; }

        /// <summary>One granted (ability, origin) pair with its level.</summary>
        internal readonly struct GrantRow
        {
            internal GrantRow(int abilityId, GrantSource source, int level)
            {
                AbilityId = abilityId;
                Source = source;
                Level = level;
            }

            internal int AbilityId { get; }

            internal GrantSource Source { get; }

            internal int Level { get; }
        }

        /// <summary>Renders the canonical grant order as text. Diagnostics and tests only.</summary>
        /// <returns>Space-separated <c>abilityId/Kind:Key</c> entries in stored order.</returns>
        internal string DebugOrder()
        {
            var builder = new StringBuilder();
            for (var i = 0; i < Grants.Length; i++)
            {
                if (i > 0) builder.Append(' ');
                builder.Append(Grants[i].AbilityId).Append('/')
                    .Append(Grants[i].Source.Kind).Append(':').Append(Grants[i].Source.Key);
            }

            return builder.ToString();
        }
    }
}
