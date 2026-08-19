#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Effects;

namespace Bun3.Gameplay
{
    /// <summary>
    /// Unit registry and tick driver for one simulation. Implements
    /// <see cref="IEffectTargetResolver"/> for the pipeline it owns, so effects address units by
    /// <see cref="TargetId"/> without knowing about units at all. Structural changes (spawn,
    /// despawn) are queued and applied only at a phase boundary — the registry must never mutate
    /// while a tick iterates it. Composed of this file plus <c>World.Ability.cs</c>.
    /// </summary>
    public sealed partial class World : IEffectTargetResolver
    {
        private readonly List<Unit> _units = new List<Unit>();
        private readonly List<TargetId> _unitIds = new List<TargetId>();
        private readonly List<Unit> _spawnQueue = new List<Unit>();
        private readonly List<TargetId> _despawnQueue = new List<TargetId>();
        private readonly EffectCatalog _effects;
        private readonly AbilityCatalog _abilities;
        private readonly IRng _rng;

        /// <summary>Creates a world over the given catalogs and random source.</summary>
        /// <param name="effects">Compiled effect catalog.</param>
        /// <param name="abilities">Compiled ability catalog.</param>
        /// <param name="rng">Seeded random source; shared with the pipeline.</param>
        /// <param name="applyBudgetPerTick">Maximum effect applications drained per tick.</param>
        public World(EffectCatalog effects, AbilityCatalog abilities, IRng rng, int applyBudgetPerTick = 64)
        {
            _effects = effects ?? throw new ArgumentNullException(nameof(effects));
            _abilities = abilities ?? throw new ArgumentNullException(nameof(abilities));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            Pipeline = new EffectPipeline(effects, this, rng, applyBudgetPerTick);
        }

        /// <summary>Effect pipeline this world drives.</summary>
        public EffectPipeline Pipeline { get; }

        /// <summary>Ticks processed so far. The pipeline owns the counter; this world keeps none of its own.</summary>
        public long CurrentTick => Pipeline.CurrentTick;

        /// <summary>Number of registered units.</summary>
        public int UnitCount => _units.Count;

        /// <summary>Registered unit ids, ascending.</summary>
        public IReadOnlyList<TargetId> TargetIds => _unitIds;

        /// <summary>Resolves a unit's effect target by id.</summary>
        /// <param name="id">Target id to resolve.</param>
        /// <param name="target">Resolved effect target.</param>
        /// <returns>True when a unit with that id is registered.</returns>
        public bool TryResolve(TargetId id, out EffectTarget? target)
        {
            if (TryGetUnit(id, out var unit))
            {
                target = unit!.Effects;
                return true;
            }

            target = null;
            return false;
        }

        /// <summary>Looks up a registered unit by id.</summary>
        /// <param name="id">Unit id.</param>
        /// <param name="unit">Resolved unit.</param>
        /// <returns>True when the unit is registered.</returns>
        public bool TryGetUnit(TargetId id, out Unit? unit)
        {
            var index = IndexOf(id);
            unit = index >= 0 ? _units[index] : null;
            return index >= 0;
        }

        /// <summary>Queues a unit for registration. It becomes visible at the start of the next
        /// <see cref="Tick"/>.</summary>
        /// <param name="unit">Unit to register.</param>
        public void Spawn(Unit unit)
        {
            if (unit is null) throw new ArgumentNullException(nameof(unit));
            _spawnQueue.Add(unit);
        }

        /// <summary>Queues a unit for removal. It disappears at the start of the next <see cref="Tick"/>.</summary>
        /// <param name="id">Unit id to remove.</param>
        /// <returns>True when a matching unit is currently registered.</returns>
        public bool Despawn(TargetId id)
        {
            _despawnQueue.Add(id);
            return IndexOf(id) >= 0;
        }

        /// <summary>Applies queued structural changes, then advances the pipeline by one tick.</summary>
        public void Tick()
        {
            FlushStructuralChanges();
            Pipeline.Tick();
        }

        /// <summary>Creates a deep-copy snapshot of every registered unit plus the world counters.</summary>
        /// <returns>Snapshot of the whole world.</returns>
        /// <exception cref="InvalidOperationException">Thrown when applications are still pending —
        /// the queue is outside snapshot scope, so snapshotting mid-drain would silently lose work.</exception>
        public WorldSnapshot CreateSnapshot()
        {
            if (Pipeline.PendingApplyCount > 0)
                throw new InvalidOperationException("Cannot snapshot a world with pending applications.");

            var units = new UnitSnapshot[_units.Count];
            for (var i = 0; i < _units.Count; i++) units[i] = _units[i].CreateSnapshot();
            return new WorldSnapshot(units, Pipeline.CurrentTick, Pipeline.NextInstanceId);
        }

        /// <summary>Restores every unit and the world counters from a snapshot.</summary>
        /// <param name="snapshot">Snapshot to restore.</param>
        /// <exception cref="ArgumentException">Thrown when the snapshot's unit set does not match the
        /// registry — spawn and despawn are not part of a snapshot.</exception>
        public void RestoreSnapshot(WorldSnapshot snapshot)
        {
            if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

            FlushStructuralChanges();
            if (snapshot.Units.Length != _units.Count)
                throw new ArgumentException("Snapshot unit count does not match the registry.", nameof(snapshot));

            for (var i = 0; i < _units.Count; i++)
            {
                _units[i].RestoreSnapshot(snapshot.Units[i], _effects);
            }

            Pipeline.CurrentTick = snapshot.CurrentTick;
            Pipeline.NextInstanceId = snapshot.NextInstanceId;
        }

        private void FlushStructuralChanges()
        {
            for (var i = 0; i < _despawnQueue.Count; i++)
            {
                var index = IndexOf(_despawnQueue[i]);
                if (index < 0) continue;
                _units.RemoveAt(index);
                _unitIds.RemoveAt(index);
            }

            _despawnQueue.Clear();

            for (var i = 0; i < _spawnQueue.Count; i++)
            {
                var unit = _spawnQueue[i];
                var index = IndexOf(unit.Id);
                if (index >= 0)
                {
                    _spawnQueue.Clear();
                    throw new InvalidOperationException($"Unit id {unit.Id.Value} is already registered.");
                }

                var position = InsertPosition(unit.Id);
                _units.Insert(position, unit);
                _unitIds.Insert(position, unit.Id);
            }

            _spawnQueue.Clear();
        }

        private int IndexOf(TargetId id)
        {
            var low = 0;
            var high = _unitIds.Count - 1;
            while (low <= high)
            {
                var mid = low + ((high - low) >> 1);
                var order = _unitIds[mid].CompareTo(id);
                if (order == 0) return mid;
                if (order < 0) low = mid + 1;
                else high = mid - 1;
            }

            return -1;
        }

        private int InsertPosition(TargetId id)
        {
            var low = 0;
            var high = _unitIds.Count;
            while (low < high)
            {
                var mid = low + ((high - low) >> 1);
                if (_unitIds[mid].CompareTo(id) < 0) low = mid + 1;
                else high = mid;
            }

            return low;
        }
    }
}
