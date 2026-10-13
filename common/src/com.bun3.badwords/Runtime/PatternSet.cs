using System;
using System.Collections.Generic;

namespace Bun3.BadWords
{
    /// <summary>Collects normalized patterns, merging the flags of duplicate keys, and builds an automaton.</summary>
    internal sealed class PatternSet
    {
        const PatternFlags boundaries = PatternFlags.BoundaryStart | PatternFlags.BoundaryEnd;

        readonly Dictionary<string, PatternFlags> patterns = new Dictionary<string, PatternFlags>(StringComparer.Ordinal);

        internal void Add(string key, PatternFlags flags)
        {
            if (!patterns.TryGetValue(key, out PatternFlags existing))
            {
                patterns.Add(key, flags);
                return;
            }
            patterns[key] = ((existing | flags) & ~boundaries) | (existing & flags & boundaries);
        }

        internal AhoCorasick Build()
        {
            var keys = new List<string>(patterns.Count);
            var flags = new List<PatternFlags>(patterns.Count);
            foreach (var pair in patterns)
            {
                keys.Add(pair.Key);
                flags.Add(pair.Value);
            }
            return new AhoCorasick(keys, flags);
        }
    }
}
