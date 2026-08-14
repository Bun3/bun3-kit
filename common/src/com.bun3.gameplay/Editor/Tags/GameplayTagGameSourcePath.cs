#nullable enable
using System;
using System.IO;

namespace Bun3.Gameplay.Editor.Tags
{
    /// <summary>Unity 프로젝트의 고정된 Game Source 경로를 계산합니다.</summary>
    public static class GameplayTagGameSourcePath
    {
        /// <summary>Unity의 Assets 절대 경로에서 ProjectSettings의 Game Source 경로를 구합니다.</summary>
        /// <param name="dataPath">Unity <c>Application.dataPath</c> 값입니다.</param>
        /// <returns>프로젝트의 <c>ProjectSettings/GameplayTags.json</c> 절대 경로입니다.</returns>
        /// <exception cref="ArgumentException"><paramref name="dataPath"/>가 비어 있는 경우입니다.</exception>
        public static string Get(string dataPath)
        {
            if (string.IsNullOrWhiteSpace(dataPath))
            {
                throw new ArgumentException("Unity data path는 비어 있을 수 없습니다.", nameof(dataPath));
            }

            return Path.GetFullPath(Path.Combine(
                dataPath,
                "..",
                "ProjectSettings",
                "GameplayTags.json"));
        }
    }
}
