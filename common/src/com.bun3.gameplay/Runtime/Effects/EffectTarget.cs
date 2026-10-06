#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Effects
{
    /// <summary>
    /// One target effects can be applied to. Owns its attribute set, owned tags, and active effect list.
    /// Composed of this file plus <c>EffectTarget.Snapshot.cs</c> (snapshot save and restore).
    /// </summary>
    public sealed partial class EffectTarget
    {
        /// <summary>Marks a time baseline that is not known yet; the next catch-up adopts the owner's clock.</summary>
        internal const long UnknownTick = long.MinValue;

        /// <summary>A due tick that makes the next time step process the target.</summary>
        internal const long DueNow = long.MinValue;

        private readonly List<EffectInstance> _activeEffects = new List<EffectInstance>();
        private readonly List<EffectInstance> _timedEffects = new List<EffectInstance>();
        private readonly List<EffectInstance> _ongoingEffects = new List<EffectInstance>();
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

        /// <summary>Active instances whose effect has OnEvent chains. Zero lets event dispatch skip the scan.</summary>
        internal int EventListenerCount { get; private set; }

        /// <summary>Pipeline that advances this target's time and records its attribute changes.</summary>
        internal EffectPipeline? Owner { get; set; }

        /// <summary>Last tick whose time step is reflected in the timed instances' counters.</summary>
        internal long AdvancedThrough { get; set; } = UnknownTick;

        /// <summary>Earliest tick on which a timed instance fires or expires; never later than the truth.</summary>
        internal long NextDueTick { get; set; } = DueNow;

        /// <summary>Applies the time steps owed to this target by its owner, if any, before counters are read.</summary>
        internal void SyncTime() => Owner?.CatchUp(this);

        /// <summary>Forgets the time baseline: the next catch-up starts from the owner's clock and the next time step processes the target.</summary>
        internal void ResetTimeBaseline()
        {
            AdvancedThrough = UnknownTick;
            NextDueTick = DueNow;
        }

        internal void GrantTags(GameplayTag[] tags)
        {
            for (var i = 0; i < tags.Length; i++) Tags.Add(tags[i]);
        }

        internal void RevokeTags(GameplayTag[] tags)
        {
            for (var i = 0; i < tags.Length; i++) Tags.Remove(tags[i]);
        }

        /// <summary>Returns a value copy of the active instance at <paramref name="index"/> (ascending instance id).</summary>
        /// <param name="index">Index in [0, <see cref="ActiveEffectCount"/>).</param>
        /// <returns>Copy of the instance's observable state.</returns>
        public ActiveEffectInfo GetActiveEffect(int index)
        {
            SyncTime();
            return new ActiveEffectInfo(_activeEffects[index]);
        }

        /// <summary>Finds an active instance by id.</summary>
        /// <param name="instanceId">Instance id.</param>
        /// <param name="info">Value copy when found.</param>
        /// <returns>True when the instance is active on this target.</returns>
        public bool TryGetActiveEffect(ulong instanceId, out ActiveEffectInfo info)
        {
            SyncTime();
            for (var i = 0; i < _activeEffects.Count; i++)
            {
                if (_activeEffects[i].Id != instanceId) continue;
                info = new ActiveEffectInfo(_activeEffects[i]);
                return true;
            }

            info = default;
            return false;
        }

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
                NextDueTick = DueNow;
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
    }
}
