#nullable enable
using System;
using System.IO;

namespace Bun3.Gameplay.Tags
{
    /// <summary>Computes the OS-shared cache path of the local development GameplayTag catalog.</summary>
    public static class TagCatalogDevelopmentPath
    {
        /// <summary>Returns the development <c>GameplayTags.catalog</c> path for the given catalog ID.</summary>
        /// <param name="catalogId">Stable lowercase catalog ID representing the game product.</param>
        /// <param name="localApplicationDataOverride">LocalApplicationData override supplied by tests or the host.</param>
        /// <returns>Absolute path of the development catalog file.</returns>
        /// <exception cref="ArgumentException">The catalog ID or cache root is invalid.</exception>
        public static string Get(string catalogId, string? localApplicationDataOverride = null)
        {
            if (!IsValidId(catalogId)) throw new ArgumentException("Catalog ID must be lowercase alphanumeric segments joined by dots or hyphens.", nameof(catalogId));
            var root = localApplicationDataOverride
                ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Cannot resolve the LocalApplicationData path.", nameof(localApplicationDataOverride));
            return Path.Combine(Path.GetFullPath(root), "Bun3", "GameplayTags", catalogId, "dev", "GameplayTags.catalog");
        }

        private static bool IsValidId(string? value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            var separator = true;
            foreach (var character in value!)
            {
                if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9')) separator = false;
                else if ((character == '.' || character == '-') && !separator) separator = true;
                else return false;
            }

            return !separator;
        }
    }
}
