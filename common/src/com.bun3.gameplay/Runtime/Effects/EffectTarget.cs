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
        private readonly List<EffectInstance> _timedEffects = new List<EffectInstance>();
        private readonly List<EffectInstance> _ongoingEffects = new List<EffectInstance>();

        /// <summary>Marks a time baseline that is not known yet; the next catch-up adopts the owner's clock.</summary>
        internal const long UnknownTick = long.MinValue;
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

        /// <summary>Active instances that advance with time (Duration or periodic), ascending Id.</summary>
        internal List<EffectInstance> TimedEffects => _timedEffects;

        /// <summary>Active instances whose effect has Ongoing conditions, ascending Id.</summary>
        internal List<EffectInstance> OngoingEffects => _ongoingEffects;

        /// <summary>Pipeline that advances this target's time and records its attribute changes.</summary>
        internal EffectPipeline? Owner { get; set; }

        /// <summary>Last tick whose time step is reflected in the timed instances' counters.</summary>
        internal long AdvancedThrough { get; set; } = UnknownTick;

        /// <summary>Earliest tick on which a timed instance fires or expires; never later than the truth.</summary>
        internal long NextDueTick { get; set; } = long.MinValue;

        /// <summary>Returns a value copy of the active instance at <paramref name="index"/> (ascending instance id).</summary>
        /// <param name="index">Index in [0, <see cref="ActiveEffectCount"/>).</param>
        /// <returns>Copy of the instance's observable state.</returns>
        public ActiveEffectInfo GetActiveEffect(int index)
        {
            Owner?.CatchUp(this);
            return new ActiveEffectInfo(_activeEffects[index]);
        }

        /// <summary>Finds an active instance by id.</summary>
        /// <param name="instanceId">Instance id.</param>
        /// <param name="info">Value copy when found.</param>
        /// <returns>True when the instance is active on this target.</returns>
        public bool TryGetActiveEffect(ulong instanceId, out ActiveEffectInfo info)
        {
            Owner?.CatchUp(this);
            for (var i = 0; i < _activeEffects.Count; i++)
            {
                if (_activeEffects[i].Id != instanceId) continue;
                info = new ActiveEffectInfo(_activeEffects[i]);
                return true;
            }

            info = default;
            return false;
        }

        /// <summary>Active instances whose effect has OnEvent chains. Zero lets event dispatch skip the scan.</summary>
        internal int EventListenerCount { get; private set; }

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

        internal EffectTarget(TargetId id, AttributeLayout layout, TagCatalog tagCatalog)
        {
            Id = id;
            Attributes = new AttributeSet(layout);
            Tags = tagCatalog.CreateCountContainer();
        }

        /// <summary>Inserts an active effect instance at its ascending-Id position, in the active list and
        /// in each phase list its effect belongs to.</summary>
        internal void InsertActive(EffectInstance instance, CompiledEffect effect)
        {
            InsertById(_activeEffects, instance);
            if (effect.HasEventChains) EventListenerCount++;
            if (AdvancesWithTime(effect))
            {
                InsertById(_timedEffects, instance);
                NextDueTick = long.MinValue;
            }

            if (effect.OngoingConditions.Length > 0) InsertById(_ongoingEffects, instance);
        }

        /// <summary>Removes an instance from the active list and the phase lists.</summary>
        internal void RemoveActive(EffectInstance instance, CompiledEffect effect)
        {
            if (!_activeEffects.Remove(instance)) return;
            if (effect.HasEventChains) EventListenerCount--;
            if (AdvancesWithTime(effect)) _timedEffects.Remove(instance);
            if (effect.OngoingConditions.Length > 0) _ongoingEffects.Remove(instance);
        }

        private static bool AdvancesWithTime(CompiledEffect effect) =>
            effect.DurationType == EffectDurationType.Duration || effect.PeriodTicks > 0;

        private static void InsertById(List<EffectInstance> list, EffectInstance instance)
        {
            var position = list.Count;
            while (position > 0 && list[position - 1].Id > instance.Id) position--;
            list.Insert(position, instance);
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
            Owner?.CatchUp(this);
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
                    instance.Id, instance.EffectId, instance.Source, instance.Level, instance.Stack,
                    instance.RemainingTicks, instance.DurationTicks, instance.PeriodCountdown, instance.Enabled,
                    instance.CreatedTick, modifiers);
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
                    var grantedTags = catalog.GetEffect(instance.EffectId).GrantedTags;
                    for (var g = 0; g < grantedTags.Length; g++) Tags.Remove(grantedTags[g]);
                }

                instance.Enabled = false;   // Dropped, not pooled: a restore is rare and owns no pipeline pool.
            }

            _activeEffects.Clear();
            _timedEffects.Clear();
            _ongoingEffects.Clear();
            EventListenerCount = 0;
            AdvancedThrough = UnknownTick;
            NextDueTick = long.MinValue;

            var bases = snapshot.AttributeBases;
            for (var i = 0; i < bases.Length; i++) Attributes.RestoreDeclaredBase(i, bases[i]);

            var rows = snapshot.Instances;
            for (var i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                var instance = EffectInstance.Rent(
                    null, row.Id, row.EffectId, row.Source, row.Level, row.Stack,
                    row.RemainingTicks, row.PeriodCountdown, row.CreatedTick);
                instance.Enabled = row.Enabled;
                instance.DurationTicks = row.DurationTicks;
                InsertActive(instance, catalog.GetEffect(row.EffectId));

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
                    var grantedTags = catalog.GetEffect(row.EffectId).GrantedTags;
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
