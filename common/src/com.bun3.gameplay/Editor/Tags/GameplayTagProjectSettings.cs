#nullable enable
using System;
using UnityEditor;
using UnityEngine;

namespace Bun3.Gameplay.Editor.Tags
{
    [FilePath("ProjectSettings/GameplayTagSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class GameplayTagProjectSettings : ScriptableSingleton<GameplayTagProjectSettings>
    {
        [SerializeField] private string _catalogId = string.Empty;

        internal static string? ReadConfiguredCatalogId()
        {
            var result = instance._catalogId ?? string.Empty;
            return result.Length == 0 ? null : result;
        }

        internal static string GetSuggestedCatalogId(string productName) =>
            GameplayTagCatalogId.Normalize(
                productName ?? throw new ArgumentNullException(nameof(productName)));

        internal static string ApplyCatalogId(string value, Action<string> persist)
        {
            if (persist is null) throw new ArgumentNullException(nameof(persist));
            var result = GameplayTagCatalogId.Require(value, nameof(value));
            persist(result);
            return result;
        }

        internal static string SaveCatalogId(string value) =>
            SaveCatalogId(value, () => instance.Save(true));

        internal static string SaveCatalogId(string value, Action persist)
        {
            if (persist is null) throw new ArgumentNullException(nameof(persist));
            return ApplyCatalogId(value, result =>
            {
                var previous = instance._catalogId;
                instance._catalogId = result;
                try
                {
                    persist();
                }
                catch
                {
                    instance._catalogId = previous;
                    throw;
                }
            });
        }
    }
}
