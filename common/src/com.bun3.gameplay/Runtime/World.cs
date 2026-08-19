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
    /// despawn) are queued in call order and applied only at a phase boundary — the registry must
    /// never mutate while a tick iterates it. Composed of this file plus <c>World.Ability.cs</c>.
    /// </summary>
    public sealed partial class World : IEffectTargetResolver
    {
        private readonly List<Unit> _units = new List<Unit>();
        private readonly List<TargetId> _unitIds = new List<TargetId>();
        private readonly List<(Unit? Unit, TargetId Id)> _pendingOps = new List<(Unit? Unit, TargetId Id)>();
        private readonly EffectCatalog _effects;
        private readonly AbilityCatalog _abilities;
        private readonly IRng _rng;
        private readonly int[][] _grantedAbilityIds;

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

            // AbilityCatalogBuilder.Build already proved every GrantedAbilityNames entry resolves
            // against abilities, so this cache is built over references already known to be valid.
            _grantedAbilityIds = new int[effects.Count][];
            for (var specId = 0; specId < effects.Count; specId++)
            {
                var names = effects.GetSpec(specId).GrantedAbilityNames;
                var ids = new int[names.Length];
                for (var i = 0; i < names.Length; i++)
                {
                    ids[i] = abilities.GetRequiredId(names[i]);
                }

                _grantedAbilityIds[specId] = ids;
            }

            Pipeline.AbilityGrantHook = OnEffectAbilityGrant;
        }

        private void OnEffectAbilityGrant(EffectTarget target, EffectInstance instance, bool granting)
        {
            if (!TryGetUnit(target.Id, out var unit) || unit is null) return;

            var source = GrantSource.Effect(instance.Id);
            if (!granting)
            {
                unit.Abilities.Revoke(source);
                return;
            }

            var ids = _grantedAbilityIds[instance.SpecId];
            for (var i = 0; i < ids.Length; i++)
            {
                unit.Abilities.Grant(ids[i], source, instance.Level);
            }
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
        /// <see cref="Tick"/>, in the order this call takes relative to other queued spawns and
        /// despawns.</summary>
        /// <param name="unit">Unit to register.</param>
        public void Spawn(Unit unit)
        {
            if (unit is null) throw new ArgumentNullException(nameof(unit));
            _pendingOps.Add((unit, unit.Id));
        }

        /// <summary>Queues a unit for removal. The removal takes effect at the start of the next
        /// <see cref="Tick"/>, in the order this call takes relative to other queued spawns and
        /// despawns — a despawn queued after a spawn of the same id cancels it out.</summary>
        /// <param name="id">Unit id to remove.</param>
        public void Despawn(TargetId id)
        {
            _pendingOps.Add((null, id));
        }

        /// <summary>Applies queued structural changes, then advances the pipeline by one tick.</summary>
        public void Tick()
        {
            FlushStructuralChanges();
            Pipeline.Tick();
        }

        /// <summary>Creates a deep-copy snapshot of every registered unit plus the world counters.</summary>
        /// <returns>Snapshot of the whole world.</returns>
        /// <exception cref="InvalidOperationException">Thrown when applications or structural changes
        /// are still pending — both are outside snapshot scope, so snapshotting mid-drain would
        /// silently lose work or omit not-yet-flushed units.</exception>
        public WorldSnapshot CreateSnapshot()
        {
            if (Pipeline.PendingApplyCount > 0)
                throw new InvalidOperationException("Cannot snapshot a world with pending applications.");
            if (_pendingOps.Count > 0)
                throw new InvalidOperationException("Cannot snapshot a world with pending structural changes.");

            var units = new UnitSnapshot[_units.Count];
            for (var i = 0; i < _units.Count; i++) units[i] = _units[i].CreateSnapshot();
            return new WorldSnapshot(units, Pipeline.CurrentTick, Pipeline.NextInstanceId);
        }

        /// <summary>Restores every unit and the world counters from a snapshot.</summary>
        /// <param name="snapshot">Snapshot to restore.</param>
        /// <exception cref="ArgumentException">Thrown when the snapshot's unit set does not match the
        /// registry — spawn and despawn are not part of a snapshot. Validated against every unit id
        /// before any unit is restored, so a mismatch leaves the whole registry untouched.</exception>
        public void RestoreSnapshot(WorldSnapshot snapshot)
        {
            if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

            FlushStructuralChanges();
            if (snapshot.Units.Length != _units.Count)
                throw new ArgumentException("Snapshot unit count does not match the registry.", nameof(snapshot));

            for (var i = 0; i < _units.Count; i++)
            {
                if (snapshot.Units[i].UnitId != _units[i].Id)
                {
                    throw new ArgumentException(
                        $"Snapshot unit at index {i} has id {snapshot.Units[i].UnitId.Value}, "
                        + $"but the registry has id {_units[i].Id.Value} at that index.",
                        nameof(snapshot));
                }
            }

            for (var i = 0; i < _units.Count; i++)
            {
                _units[i].RestoreSnapshot(snapshot.Units[i], _effects);
            }

            Pipeline.CurrentTick = snapshot.CurrentTick;
            Pipeline.NextInstanceId = snapshot.NextInstanceId;
        }

        /// <summary>
        /// Applies the pending queue in call order. Validates the whole queue against a scratch id
        /// set first — spawning an id that is already present at that point in the sequence throws
        /// without mutating the registry and without clearing the queue, so the caller can inspect
        /// and retry. Only once validation passes is the queue replayed against the real registry.
        /// </summary>
        private void FlushStructuralChanges()
        {
            if (_pendingOps.Count == 0) return;

            ValidatePendingOps();

            for (var i = 0; i < _pendingOps.Count; i++)
            {
                var op = _pendingOps[i];
                if (op.Unit is null)
                {
                    var index = IndexOf(op.Id);
                    if (index < 0) continue;
                    _units.RemoveAt(index);
                    _unitIds.RemoveAt(index);
                }
                else
                {
                    var position = InsertPosition(op.Unit.Id);
                    _units.Insert(position, op.Unit);
                    _unitIds.Insert(position, op.Unit.Id);
                }
            }

            _pendingOps.Clear();
        }

        private void ValidatePendingOps()
        {
            var scratch = new HashSet<ulong>();
            for (var i = 0; i < _unitIds.Count; i++) scratch.Add(_unitIds[i].Value);

            for (var i = 0; i < _pendingOps.Count; i++)
            {
                var op = _pendingOps[i];
                if (op.Unit is null)
                {
                    scratch.Remove(op.Id.Value);
                }
                else if (!scratch.Add(op.Unit.Id.Value))
                {
                    throw new InvalidOperationException($"Unit id {op.Unit.Id.Value} is already registered.");
                }
            }
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
