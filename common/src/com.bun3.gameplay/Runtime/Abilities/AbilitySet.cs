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

        private GrantEntry[] _grants = Array.Empty<GrantEntry>();
        private int _count;
        private bool _sharedGrants;   // _grants belongs to an archetype until the first change copies it

        /// <summary>Creates an empty ability set.</summary>
        public AbilitySet()
        {
        }

        // Starts from an archetype's starting grants without copying them; the first change copies.
        internal AbilitySet(AbilitySet archetypeGrants)
        {
            _grants = archetypeGrants._grants;
            _count = archetypeGrants._count;
            Version = archetypeGrants.Version;
            _sharedGrants = true;
        }

        /// <summary>Builds the starting grants of an archetype, granted in order from the archetype origin.</summary>
        internal static AbilitySet ForArchetype(int[] abilityIds, int[] levels)
        {
            var grants = new AbilitySet();
            for (var i = 0; i < abilityIds.Length; i++) grants.Grant(abilityIds[i], GrantSource.Archetype(), levels[i]);
            return grants;
        }

        /// <summary>Number of (ability, origin) grants held.</summary>
        public int GrantCount => _count;

        /// <summary>Incremented whenever the grant list changes — a grant, a level change, a revoke that
        /// removed something, or a restore. Redraw an ability bar only when it changes.</summary>
        public int Version { get; private set; }

        /// <summary>Returns the grant at <paramref name="index"/> in canonical order (ability id ascending,
        /// then origin). An ability granted by several origins appears once per origin, adjacently.</summary>
        /// <param name="index">Index in [0, <see cref="GrantCount"/>).</param>
        /// <returns>Value copy of the grant.</returns>
        public AbilityGrantInfo GetGrant(int index)
        {
            if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
            var grant = _grants[index];
            return new AbilityGrantInfo(grant.AbilityId, grant.Source, grant.Level);
        }

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
                Own(0);
                _grants[position].Level = level;
                Version++;
                return;
            }

            Own(1);
            Array.Copy(_grants, position, _grants, position + 1, _count - position);
            _grants[position] = new GrantEntry { AbilityId = abilityId, Source = source, Level = level };
            _count++;
            Version++;
        }

        /// <summary>Revokes every grant made by one origin.</summary>
        /// <param name="source">Origin whose grants are reclaimed.</param>
        /// <returns>Number of grants removed.</returns>
        public int Revoke(GrantSource source)
        {
            var removed = 0;
            for (var i = 0; i < _count; i++)
            {
                if (_grants[i].Source == source) removed++;
            }

            if (removed == 0) return 0;

            Own(0);
            var kept = 0;
            for (var i = 0; i < _count; i++)
            {
                if (_grants[i].Source != source) _grants[kept++] = _grants[i];
            }

            _count = kept;
            Version++;
            return removed;
        }

        /// <summary>Returns whether any origin currently grants the ability.</summary>
        /// <param name="abilityId">Compiled ability id.</param>
        /// <returns>True when at least one grant exists.</returns>
        public bool IsGranted(int abilityId)
        {
            for (var i = 0; i < _count; i++)
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
            for (var i = 0; i < _count; i++)
            {
                if (_grants[i].AbilityId == abilityId && _grants[i].Level > level) level = _grants[i].Level;
            }

            return level;
        }

        /// <summary>Creates a deep copy of the current grants.</summary>
        /// <returns>Snapshot in canonical order.</returns>
        public AbilitySetSnapshot CreateSnapshot()
        {
            var rows = new AbilitySetSnapshot.GrantRow[_count];
            for (var i = 0; i < _count; i++)
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

            var rows = snapshot.Grants;
            _grants = new GrantEntry[rows.Length];
            _sharedGrants = false;
            for (var i = 0; i < rows.Length; i++)
            {
                _grants[i] = new GrantEntry { AbilityId = rows[i].AbilityId, Source = rows[i].Source, Level = rows[i].Level };
            }

            _count = rows.Length;
            Version++;
        }

        // Linear scan is intentional — grant counts are archetype-scale (single digits to low tens)
        // and the list must stay in canonical order for snapshot determinism.
        private int FindInsertPosition(int abilityId, GrantSource source, out bool exists)
        {
            for (var i = 0; i < _count; i++)
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
            return _count;
        }

        // Makes _grants this set's own array with room for extra more entries.
        private void Own(int extra)
        {
            var needed = _count + extra;
            if (!_sharedGrants && needed <= _grants.Length) return;
            var capacity = Math.Max(4, _sharedGrants ? needed : Math.Max(needed, _grants.Length * 2));
            var owned = new GrantEntry[capacity];
            Array.Copy(_grants, owned, _count);
            _grants = owned;
            _sharedGrants = false;
        }
    }

    /// <summary>One (ability, origin) grant of an <see cref="AbilitySet"/>.</summary>
    public readonly struct AbilityGrantInfo
    {
        internal AbilityGrantInfo(int abilityId, GrantSource source, int level)
        {
            AbilityId = abilityId;
            Source = source;
            Level = level;
        }

        /// <summary>Compiled ability id.</summary>
        public int AbilityId { get; }

        /// <summary>Origin that granted the ability.</summary>
        public GrantSource Source { get; }

        /// <summary>Level this origin grants.</summary>
        public int Level { get; }
    }
}
