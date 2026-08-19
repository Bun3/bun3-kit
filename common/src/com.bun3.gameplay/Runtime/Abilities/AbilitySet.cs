#nullable enable
using System;
using System.Collections.Generic;

namespace Bun3.Gameplay.Abilities
{
    /// <summary>
    /// Abilities granted to one unit, with the origin that granted each. The same ability may be
    /// granted by several origins; it stays available until the last origin is revoked, and its
    /// level is the maximum across the granting origins. Grants are held in canonical order
    /// (ability id ascending, then origin ascending) — snapshot and level resolution both depend
    /// on that order being deterministic.
    /// </summary>
    public sealed class AbilitySet
    {
        private struct GrantEntry
        {
            internal int AbilityId;
            internal GrantSource Source;
            internal int Level;
        }

        private readonly List<GrantEntry> _grants = new List<GrantEntry>();

        /// <summary>Number of (ability, origin) grants held.</summary>
        public int GrantCount => _grants.Count;

        /// <summary>Grants an ability from one origin. Regranting the same pair updates the level only.</summary>
        /// <param name="abilityId">Compiled ability id.</param>
        /// <param name="source">Origin granting the ability.</param>
        /// <param name="level">Ability level this origin grants; must be at least 1.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the level is below 1.</exception>
        public void Grant(int abilityId, GrantSource source, int level)
        {
            if (level < 1) throw new ArgumentOutOfRangeException(nameof(level), "Ability level must be at least 1.");

            var position = FindInsertPosition(abilityId, source, out var exists);
            if (exists)
            {
                var existing = _grants[position];
                existing.Level = level;
                _grants[position] = existing;
                return;
            }

            _grants.Insert(position, new GrantEntry { AbilityId = abilityId, Source = source, Level = level });
        }

        /// <summary>Revokes every grant made by one origin.</summary>
        /// <param name="source">Origin whose grants are reclaimed.</param>
        /// <returns>Number of grants removed.</returns>
        public int Revoke(GrantSource source)
        {
            var removed = 0;
            for (var i = _grants.Count - 1; i >= 0; i--)
            {
                if (_grants[i].Source != source) continue;
                _grants.RemoveAt(i);
                removed++;
            }

            return removed;
        }

        /// <summary>Returns whether any origin currently grants the ability.</summary>
        /// <param name="abilityId">Compiled ability id.</param>
        /// <returns>True when at least one grant exists.</returns>
        public bool IsGranted(int abilityId)
        {
            for (var i = 0; i < _grants.Count; i++)
            {
                if (_grants[i].AbilityId == abilityId) return true;
            }

            return false;
        }

        /// <summary>Returns the effective level — the maximum across granting origins, or 0 when ungranted.</summary>
        /// <param name="abilityId">Compiled ability id.</param>
        /// <returns>Effective ability level, or 0.</returns>
        public int GetLevel(int abilityId)
        {
            var level = 0;
            for (var i = 0; i < _grants.Count; i++)
            {
                if (_grants[i].AbilityId == abilityId && _grants[i].Level > level) level = _grants[i].Level;
            }

            return level;
        }

        /// <summary>Creates a deep copy of the current grants.</summary>
        /// <returns>Snapshot in canonical order.</returns>
        public AbilitySetSnapshot CreateSnapshot()
        {
            var rows = new AbilitySetSnapshot.GrantRow[_grants.Count];
            for (var i = 0; i < _grants.Count; i++)
            {
                rows[i] = new AbilitySetSnapshot.GrantRow(_grants[i].AbilityId, _grants[i].Source, _grants[i].Level);
            }

            return new AbilitySetSnapshot(rows);
        }

        /// <summary>Replaces all grants with the snapshot's.</summary>
        /// <param name="snapshot">Snapshot to restore.</param>
        public void RestoreSnapshot(AbilitySetSnapshot snapshot)
        {
            if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

            _grants.Clear();
            var rows = snapshot.Grants;
            for (var i = 0; i < rows.Length; i++)
            {
                _grants.Add(new GrantEntry { AbilityId = rows[i].AbilityId, Source = rows[i].Source, Level = rows[i].Level });
            }
        }

        // Linear scan is intentional — grant counts are archetype-scale (single digits to low tens)
        // and the list must stay in canonical order for snapshot determinism.
        private int FindInsertPosition(int abilityId, GrantSource source, out bool exists)
        {
            for (var i = 0; i < _grants.Count; i++)
            {
                var current = _grants[i];
                if (current.AbilityId < abilityId) continue;
                if (current.AbilityId > abilityId)
                {
                    exists = false;
                    return i;
                }

                var order = current.Source.CompareTo(source);
                if (order < 0) continue;
                exists = order == 0;
                return i;
            }

            exists = false;
            return _grants.Count;
        }
    }
}
