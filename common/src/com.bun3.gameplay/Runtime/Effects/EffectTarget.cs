#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Effects
{
    /// <summary>
    /// One target effects can be applied to. Owns its attribute set, owned tags, and active effect list.
    /// </summary>
    public sealed class EffectTarget
    {
        private readonly List<EffectInstance> _activeEffects = new List<EffectInstance>();
        private EffectLifecycleEvent[] _events = new EffectLifecycleEvent[4];
        private int _eventCount;
        private DrHistoryEntry[] _drHistory = Array.Empty<DrHistoryEntry>();
        private int _drHistoryCount;

        /// <summary>One application-history row for a DR (diminishing returns) category tag.</summary>
        internal struct DrHistoryEntry
        {
            /// <summary>Catalog index of the tag identifying the DR category.</summary>
            internal ushort CategoryTagIndex;

            /// <summary>Application count accumulated within the reset window (including applications voided by immunity).</summary>
            internal int AppliedCount;

            /// <summary>Pipeline tick of this category's last application (voided ones included).</summary>
            internal long LastAppliedTick;
        }

        /// <summary>Target id.</summary>
        public TargetId Id { get; }

        /// <summary>Attribute set of this target.</summary>
        public AttributeSet Attributes { get; }

        /// <summary>Tags this target owns, with their counts.</summary>
        public TagCountContainer Tags { get; }

        /// <summary>Number of currently active effect instances.</summary>
        public int ActiveEffectCount => _activeEffects.Count;

        /// <summary>Active effect instances, kept in ascending Id order.</summary>
        internal List<EffectInstance> ActiveEffects => _activeEffects;

        /// <summary>Effect lifecycle events not yet consumed.</summary>
        public ReadOnlySpan<EffectLifecycleEvent> PendingEffectEvents => _events.AsSpan(0, _eventCount);

        /// <summary>Clears the effect lifecycle event buffer.</summary>
        public void ClearEffectEvents() => _eventCount = 0;

        /// <summary>Creates a target from an id, attribute registry, archetype-declared attributes, and tag catalog.</summary>
        /// <param name="id">Target id.</param>
        /// <param name="registry">Registry holding the attribute definitions.</param>
        /// <param name="attributeIds">Attribute ids this target declares.</param>
        /// <param name="tagCatalog">Tag catalog used for owned-tag counting.</param>
        public EffectTarget(TargetId id, AttributeRegistry registry, ReadOnlySpan<ushort> attributeIds, TagCatalog tagCatalog)
        {
            if (tagCatalog is null) throw new ArgumentNullException(nameof(tagCatalog));
            Id = id;
            Attributes = new AttributeSet(registry, attributeIds);
            Tags = tagCatalog.CreateCountContainer();
        }

        /// <summary>Inserts an active effect instance at its ascending-Id position.</summary>
        internal void InsertActive(EffectInstance instance)
        {
            var position = _activeEffects.Count;
            while (position > 0 && _activeEffects[position - 1].Id > instance.Id) position--;
            _activeEffects.Insert(position, instance);
        }

        /// <summary>Removes an instance from the active effect list.</summary>
        internal void RemoveActive(EffectInstance instance) => _activeEffects.Remove(instance);

        /// <summary>Appends an effect lifecycle event to the buffer.</summary>
        internal void RaiseEffectEvent(EffectLifecycleEvent evt)
        {
            if (_eventCount == _events.Length) Array.Resize(ref _events, _events.Length * 2);
            _events[_eventCount++] = evt;
        }

        /// <summary>Finds the history slot for a DR category tag, or creates one (count 0) and returns its index.</summary>
        internal int FindOrCreateDrHistory(ushort categoryTagIndex)
        {
            for (var i = 0; i < _drHistoryCount; i++)
            {
                if (_drHistory[i].CategoryTagIndex == categoryTagIndex) return i;
            }

            if (_drHistoryCount == _drHistory.Length)
            {
                Array.Resize(ref _drHistory, _drHistory.Length == 0 ? 4 : _drHistory.Length * 2);
            }

            _drHistory[_drHistoryCount] = new DrHistoryEntry
            {
                CategoryTagIndex = categoryTagIndex, AppliedCount = 0, LastAppliedTick = long.MinValue,
            };
            return _drHistoryCount++;
        }

        /// <summary>Returns a direct (mutable) reference to a DR history slot. Must only be called with
        /// an index returned by <see cref="FindOrCreateDrHistory"/>.</summary>
        internal ref DrHistoryEntry DrHistoryAt(int index) => ref _drHistory[index];

        /// <summary>
        /// Creates a deep-copy snapshot of this target's deterministic state (attribute Base values,
        /// active effect instances). Current, owned tags, pending application queue, and pipeline tick
        /// counter are not included — see <see cref="EffectTargetSnapshot"/>.
        /// </summary>
        public EffectTargetSnapshot CreateSnapshot()
        {
            var declaredCount = Attributes.DeclaredCount;
            var bases = new BigNum[declaredCount];
            for (var i = 0; i < declaredCount; i++) bases[i] = Attributes.DeclaredBaseAt(i);

            var instances = new EffectTargetSnapshot.InstanceRow[_activeEffects.Count];
            var scratch = new List<AttributeSet.ModifierSnapshotRow>(4);
            for (var i = 0; i < _activeEffects.Count; i++)
            {
                var instance = _activeEffects[i];
                scratch.Clear();
                Attributes.CollectModifiers(instance, scratch);
                var modifiers = new EffectTargetSnapshot.ModifierRow[scratch.Count];
                for (var m = 0; m < scratch.Count; m++)
                {
                    var row = scratch[m];
                    modifiers[m] = new EffectTargetSnapshot.ModifierRow(
                        row.AttributeId, row.RowIndex, row.Op, row.Magnitude, row.ScaleWithStack);
                }

                instances[i] = new EffectTargetSnapshot.InstanceRow(
                    instance.Id, instance.SpecId, instance.Source, instance.Level, instance.Stack,
                    instance.RemainingTicks, instance.PeriodCountdown, instance.Enabled, instance.CreatedTick,
                    modifiers);
            }

            var drHistory = new EffectTargetSnapshot.DrHistoryRow[_drHistoryCount];
            for (var i = 0; i < _drHistoryCount; i++)
            {
                var entry = _drHistory[i];
                drHistory[i] = new EffectTargetSnapshot.DrHistoryRow(
                    entry.CategoryTagIndex, entry.AppliedCount, entry.LastAppliedTick);
            }

            return new EffectTargetSnapshot(Id, bases, instances, drHistory);
        }

        /// <summary>
        /// Restores this target from a snapshot. All current active instances are detached and
        /// reclaimed without events (a restore is unobservable), then the snapshot's instances are
        /// restored raw in stored order, including modifier reattachment. Base values are also
        /// written raw, without clamping. After the whole raw restore, <see cref="AttributeSet.RebuildDirty"/>
        /// is called exactly once; that single recompute rebuilds Current in the registry's
        /// topological (EvaluationOrder) order — that determinism guarantees bit-identical results.
        /// Pending application queue, pipeline tick counter, and next issued id must be restored
        /// separately by the caller.
        /// </summary>
        /// <param name="snapshot">Snapshot to restore.</param>
        /// <param name="catalog">Effect catalog used to reclaim/regrant GrantedTags (must be the same catalog as at snapshot time).</param>
        public void RestoreSnapshot(EffectTargetSnapshot snapshot, EffectCatalog catalog)
        {
            if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
            if (catalog is null) throw new ArgumentNullException(nameof(catalog));
            if (snapshot.TargetId != Id)
                throw new ArgumentException("Cannot restore a snapshot taken from a different target.", nameof(snapshot));

            for (var i = 0; i < _activeEffects.Count; i++)
            {
                var instance = _activeEffects[i];
                Attributes.DetachModifiers(instance);
                if (instance.Enabled)   // Disabled instances hold no tags — removing would double-decrement.
                {
                    var grantedTags = catalog.GetSpec(instance.SpecId).GrantedTags;
                    for (var g = 0; g < grantedTags.Length; g++) Tags.Remove(grantedTags[g]);
                }

                EffectInstance.Return(instance);
            }

            _activeEffects.Clear();

            var bases = snapshot.AttributeBases;
            for (var i = 0; i < bases.Length; i++) Attributes.RestoreDeclaredBase(i, bases[i]);

            var rows = snapshot.Instances;
            for (var i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                var instance = EffectInstance.Rent(
                    row.Id, row.SpecId, row.Source, row.Level, row.Stack,
                    row.RemainingTicks, row.PeriodCountdown, row.CreatedTick);
                instance.Enabled = row.Enabled;
                InsertActive(instance);

                var modifiers = row.Modifiers;
                for (var m = 0; m < modifiers.Length; m++)
                {
                    var modifier = modifiers[m];
                    Attributes.AttachModifier(
                        instance, modifier.RowIndex, modifier.AttributeId, modifier.Op,
                        modifier.Magnitude, modifier.ScaleWithStack);
                }

                if (row.Enabled)   // Disabled instances grant no tags — adding would leak permanently.
                {
                    var grantedTags = catalog.GetSpec(row.SpecId).GrantedTags;
                    for (var g = 0; g < grantedTags.Length; g++) Tags.Add(grantedTags[g]);
                }
            }

            Attributes.RebuildDirty();

            // Restore DR history to the snapshot state — deterministic replay requires this history,
            // which feeds duration calculation. Existing slots are cleared logically only (count 0);
            // the array is reused.
            _drHistoryCount = 0;
            var drRows = snapshot.DrHistory;
            for (var i = 0; i < drRows.Length; i++)
            {
                var row = drRows[i];
                var index = FindOrCreateDrHistory(row.CategoryTagIndex);
                ref var entry = ref DrHistoryAt(index);
                entry.AppliedCount = row.AppliedCount;
                entry.LastAppliedTick = row.LastAppliedTick;
            }
        }
    }
}
