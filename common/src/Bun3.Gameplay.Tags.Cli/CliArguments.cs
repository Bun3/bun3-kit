#nullable enable
using System;
using System.Collections.Generic;

namespace Bun3.Gameplay.Tags.Cli
{
    internal sealed class CliArguments
    {
        private readonly Dictionary<string, string> _singletons = new(StringComparer.Ordinal);
        private readonly HashSet<string> _flags = new(StringComparer.Ordinal);
        private readonly List<string> _sources = new();
        private readonly List<string> _positionals = new();

        internal IReadOnlyList<string> Sources => _sources;
        internal IReadOnlyList<string> Positionals => _positionals;
        internal bool HasFlag(string name) => _flags.Contains(name);
        internal string? Get(string name) => _singletons.TryGetValue(name, out var value) ? value : null;

        internal static bool TryParse(
            string[] args,
            int start,
            IReadOnlyCollection<string> allowedFlags,
            IReadOnlyCollection<string> allowedSingletons,
            bool allowSources,
            bool allowPositionals,
            out CliArguments parsed)
        {
            parsed = new CliArguments();
            for (var index = start; index < args.Length; index++)
            {
                var token = args[index];
                if (!token.StartsWith("--", StringComparison.Ordinal))
                {
                    if (!allowPositionals) return false;
                    parsed._positionals.Add(token);
                    continue;
                }

                if (Contains(allowedFlags, token))
                {
                    if (!parsed._flags.Add(token)) return false;
                    continue;
                }

                if (allowSources && string.Equals(token, "--source", StringComparison.Ordinal))
                {
                    if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal)) return false;
                    parsed._sources.Add(args[index]);
                    continue;
                }

                if (!Contains(allowedSingletons, token) || parsed._singletons.ContainsKey(token)) return false;
                if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal)) return false;
                parsed._singletons.Add(token, args[index]);
            }

            return true;
        }

        private static bool Contains(IReadOnlyCollection<string> values, string token)
        {
            foreach (var value in values)
            {
                if (string.Equals(value, token, StringComparison.Ordinal)) return true;
            }

            return false;
        }
    }
}
