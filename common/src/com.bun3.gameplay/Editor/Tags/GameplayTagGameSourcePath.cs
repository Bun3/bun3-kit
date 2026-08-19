#nullable enable
using System;
using System.IO;

namespace Bun3.Gameplay.Editor.Tags
{
    /// <summary>Computes the fixed game source path of a Unity project.</summary>
    public static class GameplayTagGameSourcePath
    {
        /// <summary>Derives the ProjectSettings game source path from Unity's absolute Assets path.</summary>
        /// <param name="dataPath">Unity <c>Application.dataPath</c> value.</param>
        /// <returns>Absolute path of the project's <c>ProjectSettings/GameplayTags.json</c>.</returns>
        /// <exception cref="ArgumentException"><paramref name="dataPath"/> is empty.</exception>
        public static string Get(string dataPath)
        {
            if (string.IsNullOrWhiteSpace(dataPath))
            {
                throw new ArgumentException("Unity data path cannot be empty.", nameof(dataPath));
            }

            return Path.GetFullPath(Path.Combine(
                dataPath,
                "..",
                "ProjectSettings",
                "GameplayTags.json"));
        }
    }
}
