#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace Bun3.Gameplay.Editor.Tags
{
    internal static class GameplayTagBuildContextProviderDiscovery
    {
        private const string NUnitAssemblyName = "nunit.framework";

        internal static IReadOnlyList<Type> Discover()
        {
            var providerTypes = new List<Type>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<IGameplayTagBuildContextProvider>())
            {
                if (!ReferencesNUnit(type.Assembly)) providerTypes.Add(type);
            }

            return providerTypes;
        }

        internal static List<Type> SelectCandidates(IReadOnlyList<Type> providerTypes)
        {
            if (providerTypes is null) throw new ArgumentNullException(nameof(providerTypes));
            var candidates = new List<Type>();
            for (var index = 0; index < providerTypes.Count; index++)
            {
                var type = providerTypes[index]
                    ?? throw new ArgumentNullException(nameof(providerTypes));
                if (type.IsAbstract || type.ContainsGenericParameters
                    || !typeof(IGameplayTagBuildContextProvider).IsAssignableFrom(type)
                    || FindParameterlessConstructor(type) is null)
                {
                    continue;
                }

                candidates.Add(type);
            }

            return candidates;
        }

        internal static string FormatCandidateCount(IReadOnlyList<Type> candidates)
        {
            if (candidates is null) throw new ArgumentNullException(nameof(candidates));
            if (candidates.Count == 0) return "0.";

            var names = new string[candidates.Count];
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index]
                    ?? throw new ArgumentNullException(nameof(candidates));
                names[index] = candidate.FullName ?? candidate.Name;
            }

            Array.Sort(names, StringComparer.Ordinal);
            return candidates.Count + ". Candidates: " + string.Join(", ", names) + ".";
        }

        private static bool ReferencesNUnit(Assembly assembly)
        {
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (string.Equals(reference.Name, NUnitAssemblyName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static ConstructorInfo? FindParameterlessConstructor(Type type) =>
            type.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null);
    }
}
