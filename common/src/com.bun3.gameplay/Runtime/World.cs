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
    /// never mutate while a tick iterates it. Outside <see cref="Tick"/> only queueing (spawn, despawn,
    /// <c>Enqueue*</c> commands) and read-only queries are allowed. Composed of this file plus
    /// <c>World.Ability.cs</c> and <c>World.Commands.cs</c>.
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
        private readonly int _applyBudgetPerUnit;

        /// <summary>Creates a world over the given catalogs and random source.</summary>
        /// <param name="effects">Compiled effect catalog.</param>
        /// <param name="abilities">Compiled ability catalog.</param>
        /// <param name="rng">Seeded random source; shared with the pipeline.</param>
        /// <param name="applyBudgetPerUnit">Effect applications drained per tick for each registered unit.
        /// The budget is pooled: a tick drains up to this value times the unit count (at least one unit's
        /// worth), in queue order. It exists to bound runaway chains such as mutual reflection; the
        /// surplus carries over to the next tick.</param>
        public World(EffectCatalog effects, AbilityCatalog abilities, IRng rng, int applyBudgetPerUnit = 32)
        {
            _effects = effects ?? throw new ArgumentNullException(nameof(effects));
            _abilities = abilities ?? throw new ArgumentNullException(nameof(abilities));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            if (applyBudgetPerUnit <= 0)
                throw new ArgumentOutOfRangeException(nameof(applyBudgetPerUnit), "Apply budget per unit must be at least 1.");
            _applyBudgetPerUnit = applyBudgetPerUnit;
            Pipeline = new EffectPipeline(effects, this, rng, applyBudgetPerUnit);

            // AbilityCatalogBuilder.Build already proved every GrantedAbilityNames entry resolves
            // against abilities, so this cache is built over references already known to be valid.
            _grantedAbilityIds = new int[effects.Count][];
            for (var effectId = 0; effectId < effects.Count; effectId++)
            {
                var names = effects.GetEffect(effectId).GrantedAbilityNames;
                var ids = new int[names.Length];
                for (var i = 0; i < names.Length; i++)
                {
                    ids[i] = abilities.GetRequiredId(names[i]);
                }

                _grantedAbilityIds[effectId] = ids;
            }

            Pipeline.AbilityGrantHook = OnEffectAbilityGrant;
        }

        private void OnEffectAbilityGrant(EffectTarget target, EffectInstance instance, bool granting)
        {
            var ids = _grantedAbilityIds[instance.EffectId];
            if (ids.Length == 0) return;
            if (!TryGetUnit(target.Id, out var unit) || unit is null) return;

            var source = GrantSource.Effect(instance.Id);
            if (!granting)
            {
                unit.Abilities.Revoke(source);
                return;
            }

            for (var i = 0; i < ids.Length; i++)
            {
                unit.Abilities.Grant(ids[i], source, instance.Level);
            }
        }

        /// <summary>Effect pipeline this world drives. Read from it freely, but do not call its mutating
        /// members (<c>EnqueueApply</c>, <c>RemoveByTags</c>, <c>RemoveById</c>, <c>Tick</c>) on a world:
        /// they bypass the command queue and its call order, the events they raise between ticks are cleared
        /// when the next <see cref="Tick"/> starts, and the pipeline's own tick skips structural changes and
        /// commands. Use the world's <c>Enqueue*</c> commands and <see cref="Tick"/> instead.</summary>
        public EffectPipeline Pipeline { get; }

        /// <summary>Gameplay events raised by the last <see cref="Tick"/>, in raise order.</summary>
        public ReadOnlySpan<GameplayEvent> PendingGameplayEvents => Pipeline.PendingGameplayEvents;

        /// <summary>Ticks processed so far. The pipeline owns the counter; this world keeps none of its own.</summary>
        public long CurrentTick => Pipeline.CurrentTick;

        /// <summary>Number of registered units.</summary>
        public int UnitCount => _units.Count;

        /// <summary>Registered unit ids, ascending.</summary>
        public IReadOnlyList<TargetId> TargetIds => _unitIds;

        /// <summary>Effect target of the unit whose id is <c>TargetIds[index]</c>.</summary>
        /// <param name="index">Index in [0, <see cref="UnitCount"/>).</param>
        /// <returns>The unit's effect target.</returns>
        public EffectTarget TargetAt(int index) => _units[index].Effects;

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
        /// <exception cref="InvalidOperationException">Thrown when the id will already be registered at
        /// this point in the queue — registered and not queued for despawn, or already queued for
        /// spawn. The rejected call leaves the queue untouched.</exception>
        public void Spawn(Unit unit)
        {
            if (unit is null) throw new ArgumentNullException(nameof(unit));
            if (WillBeRegistered(unit.Id))
                throw new InvalidOperationException($"Unit id {unit.Id.Value} is already registered.");
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

        /// <summary>Applies queued structural changes, clears the observation buffers, processes queued
        /// commands in call order, then runs the pipeline phases. Read the buffers after this call — they
        /// hold exactly this tick's output.</summary>
        public void Tick()
        {
            FlushStructuralChanges();
            Pipeline.ClearObservationBuffers();
            _activationCount = 0;
            ProcessCommands();
            Pipeline.RunPhases();
        }

        /// <summary>Creates a deep-copy snapshot of every registered unit plus the world counters.</summary>
        /// <returns>Snapshot of the whole world.</returns>
        /// <exception cref="InvalidOperationException">Thrown when applications, structural changes or
        /// commands are still pending — all are outside snapshot scope, so snapshotting mid-drain would
        /// silently lose work or omit not-yet-flushed units.</exception>
        public WorldSnapshot CreateSnapshot()
        {
            if (Pipeline.PendingApplyCount > 0)
                throw new InvalidOperationException("Cannot snapshot a world with pending applications.");
            if (_pendingOps.Count > 0)
                throw new InvalidOperationException("Cannot snapshot a world with pending structural changes.");
            if (_commands.Count > 0)
                throw new InvalidOperationException("Cannot snapshot a world with pending commands.");

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

        /// <summary>Applies the pending queue in call order. <see cref="Spawn"/> already rejected every
        /// duplicate, so replaying the queue cannot fail.</summary>
        private void FlushStructuralChanges()
        {
            if (_pendingOps.Count == 0) return;

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
            Pipeline.ApplyBudgetPerTick = (int)Math.Min(int.MaxValue, (long)_applyBudgetPerUnit * Math.Max(1, _units.Count));
        }

        // ponytail: linear scan of the pending queue, O(M^2) for M spawns in one frame; add a pending-id
        // set if mass spawns show up in profiles.
        private bool WillBeRegistered(TargetId id)
        {
            for (var i = _pendingOps.Count - 1; i >= 0; i--)
            {
                if (_pendingOps[i].Id == id) return _pendingOps[i].Unit is not null;
            }

            return IndexOf(id) >= 0;
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
