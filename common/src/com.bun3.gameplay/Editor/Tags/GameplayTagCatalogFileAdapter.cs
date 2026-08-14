#nullable enable
using System;
using System.IO;
using System.Text;
using Bun3.Gameplay.Tags;
using UnityEditor;
using UnityEngine;

namespace Bun3.Gameplay.Editor.Tags
{
    internal static class GameplayTagCatalogFileAdapter
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static GameplayTagCatalogEditSession Load(string absolutePath)
        {
            if (absolutePath is null) throw new ArgumentNullException(nameof(absolutePath));

            var bytes = File.ReadAllBytes(absolutePath);
            Validate(bytes);
            return GameplayTagCatalogEditSession.Open(StrictUtf8.GetString(bytes));
        }

        internal static void Save(string absolutePath, GameplayTagCatalogEditSession session)
        {
            if (session is null) throw new ArgumentNullException(nameof(session));
            SaveJson(absolutePath, session.Serialize());
        }

        internal static void SaveJson(string absolutePath, string json)
        {
            if (absolutePath is null) throw new ArgumentNullException(nameof(absolutePath));
            if (json is null) throw new ArgumentNullException(nameof(json));

            var bytes = StrictUtf8.GetBytes(json);
            Validate(bytes);

            var fullPath = Path.GetFullPath(absolutePath);
            var directory = Path.GetDirectoryName(fullPath)
                ?? throw new ArgumentException("The destination directory is missing.", nameof(absolutePath));
            Directory.CreateDirectory(directory);

            var temporaryPath = Path.Combine(
                directory,
                "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(fullPath))
                {
                    File.Replace(temporaryPath, fullPath, null);
                }
                else
                {
                    File.Move(temporaryPath, fullPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            if (TryToAssetPath(fullPath, out var assetPath))
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            }
        }

        internal static bool TryToAssetPath(string absolutePath, out string assetPath)
        {
            var comparison = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return TryToAssetPath(absolutePath, Application.dataPath, comparison, out assetPath);
        }

        internal static bool TryToAssetPath(
            string absolutePath,
            string assetsDirectory,
            StringComparison comparison,
            out string assetPath)
        {
            assetPath = string.Empty;
            if (absolutePath is null || assetsDirectory is null) return false;

            var fullAssetsDirectory = Path.GetFullPath(assetsDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var path = Path.GetFullPath(absolutePath);
            var assetsPrefix = fullAssetsDirectory + Path.DirectorySeparatorChar;
            if (!path.StartsWith(assetsPrefix, comparison))
            {
                return false;
            }

            var relativePath = path.Substring(assetsPrefix.Length);
            if (relativePath.Length == 0) return false;

            assetPath = "Assets/" + relativePath.Replace(Path.DirectorySeparatorChar, '/');
            if (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar)
            {
                assetPath = assetPath.Replace(Path.AltDirectorySeparatorChar, '/');
            }

            return true;
        }

        private static void Validate(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes, false);
#pragma warning disable CS0618 // Editor authoring JSON adapter의 호환 경로입니다.
            _ = TagCatalog.Load(stream);
#pragma warning restore CS0618
        }
    }
}
