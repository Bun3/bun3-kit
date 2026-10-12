// EffectTarget partial — snapshot save and restore of the deterministic state.
#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;

namespace Bun3.Gameplay.Effects
{
    public sealed partial class EffectTarget
    {
        /// <summary>
        /// Creates a deep-copy snapshot of this target's deterministic state (attribute Base values,
        /// active effect instances). Current, owned tags, pending application queue, and pipeline tick
        /// counter are not included — see <see cref="EffectTargetSnapshot"/>.
        /// </summary>
        public EffectTargetSnapshot CreateSnapshot()
        {
            SyncTime();
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
                    RevokeTags(catalog.GetEffect(instance.EffectId).GrantedTags);
                }

                instance.Enabled = false;   // Dropped, not pooled: a restore is rare and owns no pipeline pool.
            }

            _activeEffects.Clear();
            _timedEffects.Clear();
            _ongoingEffects.Clear();
            EventListenerCount = 0;
            ResetTimeBaseline();

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
                    GrantTags(catalog.GetEffect(row.EffectId).GrantedTags);
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
