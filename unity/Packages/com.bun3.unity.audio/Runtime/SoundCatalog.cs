using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>An ordinal string key mapped to an authored sound definition.</summary>
    [Serializable]
    public sealed class SoundCatalogEntry
    {
        [SerializeField] private string key;
        [SerializeField] private SoundDef definition;
        /// <summary>The stable, case-sensitive authoring key.</summary>
        public string Key => key;
        /// <summary>The shared playback definition.</summary>
        public SoundDef Definition => definition;
        /// <summary>Creates a mapping. The catalog validates keys and references.</summary>
        public SoundCatalogEntry(string key, SoundDef definition)
        { this.key = key; this.definition = definition; }
    }

    /// <summary>Reusable string-to-definition catalog. Lookup allocates only when rebuilding its index.</summary>
    [CreateAssetMenu(menuName = "Bun3/Audio/Sound Catalog", fileName = "SoundCatalog")]
    public sealed class SoundCatalog : ScriptableObject
    {
        /// <summary>Key suffix of the one-shot played when the charge loop with the key before it starts.</summary>
        public const string ChargeStartSuffix = ".start";
        /// <summary>Key suffix of the one-shot played when the charge loop with the key before it completes.</summary>
        public const string ChargeCompleteSuffix = ".complete";
        /// <summary>Key suffix of the one-shot played when the charge loop with the key before it is cancelled.</summary>
        public const string ChargeCancelSuffix = ".cancel";

        private static readonly string[] ChargeSuffixes = { ChargeStartSuffix, ChargeCompleteSuffix, ChargeCancelSuffix };

        [SerializeField] private SoundCatalogEntry[] entries = Array.Empty<SoundCatalogEntry>();
        private Dictionary<string, SoundDef> lookup;
        private Dictionary<string, SoundDef[]> chargeParts; // loop key -> parts in ChargeSuffixes order
        /// <summary>The authored mappings, in authoring order.</summary>
        public IReadOnlyList<SoundCatalogEntry> Entries => entries;
        /// <summary>Replaces mappings after validating them, preserving the previous catalog on failure.</summary>
        public void SetEntries(SoundCatalogEntry[] value)
        {
            var copy = value == null ? Array.Empty<SoundCatalogEntry>() : (SoundCatalogEntry[])value.Clone();
            var (index, parts) = BuildIndex(copy);
            entries = copy;
            (lookup, chargeParts) = (index, parts);
        }
        /// <summary>Validates all keys and references and prepares allocation-free lookups.</summary>
        public void ValidateOrThrow() => (lookup, chargeParts) = BuildIndex(entries);
        /// <summary>Finds a definition; null and unknown keys return false.</summary>
        public bool TryGet(string key, out SoundDef definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(key)) return false;
            EnsureIndex();
            return lookup.TryGetValue(key, out definition) && definition != null;
        }
        /// <summary>Finds a definition, or returns null for an unknown key.</summary>
        public SoundDef Get(string key) => TryGet(key, out var definition) ? definition : null;

        // Parts are found by key alone, whether or not loopKey itself is in the catalog.
        internal void GetChargeParts(string loopKey, out SoundDef start, out SoundDef complete, out SoundDef cancel)
        {
            start = complete = cancel = null;
            if (string.IsNullOrWhiteSpace(loopKey)) return;
            EnsureIndex();
            if (!chargeParts.TryGetValue(loopKey, out var parts)) return;
            (start, complete, cancel) = (parts[0], parts[1], parts[2]);
        }

        private void OnValidate() => lookup = null;
        private void OnEnable() => lookup = null;
        private void EnsureIndex()
        {
            if (lookup == null) (lookup, chargeParts) = BuildIndex(entries);
        }

        private static (Dictionary<string, SoundDef>, Dictionary<string, SoundDef[]>) BuildIndex(SoundCatalogEntry[] source)
        {
            source ??= Array.Empty<SoundCatalogEntry>();
            var result = new Dictionary<string, SoundDef>(source.Length, StringComparer.Ordinal);
            var parts = new Dictionary<string, SoundDef[]>(StringComparer.Ordinal);
            for (int i = 0; i < source.Length; i++)
            {
                var entry = source[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Key))
                    throw new ArgumentException("Sound catalog entries require nonblank keys.");
                if (entry.Definition == null)
                    throw new ArgumentException("Sound catalog entries require definitions.");
                if (!result.TryAdd(entry.Key, entry.Definition))
                    throw new ArgumentException("Sound catalog keys must be unique (ordinal, case-sensitive).");
                for (int s = 0; s < ChargeSuffixes.Length; s++)
                {
                    if (!entry.Key.EndsWith(ChargeSuffixes[s], StringComparison.Ordinal)) continue;
                    var loopKey = entry.Key.Substring(0, entry.Key.Length - ChargeSuffixes[s].Length);
                    if (!parts.TryGetValue(loopKey, out var defs)) parts.Add(loopKey, defs = new SoundDef[ChargeSuffixes.Length]);
                    defs[s] = entry.Definition;
                }
            }
            return (result, parts);
        }
    }
}
