#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Bun3.Gameplay.Editor.Tags
{
    internal static class GameplayTagProjectReferenceFiles
    {
        private static readonly HashSet<string> TextExtensions = new HashSet<string>(
            new[]
            {
                ".anim", ".asmdef", ".asmref", ".asset", ".compute", ".controller",
                ".cs", ".json", ".overrideController", ".playable", ".prefab", ".shader",
                ".txt", ".unity", ".uss", ".uxml", ".yaml", ".yml"
            },
            StringComparer.OrdinalIgnoreCase);

        internal static StringComparison PathComparison =>
            Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        internal static StringComparer PathComparer =>
            Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        internal static IReadOnlyList<GameplayTagReferenceFile> Enumerate()
        {
            var assets = Path.GetFullPath(Application.dataPath);
            var projectRoot = Path.GetDirectoryName(assets) ?? assets;

            var localPackagePaths = new List<string>();
            foreach (var package in PackageInfo.GetAllRegisteredPackages())
            {
                if (package.source == PackageSource.Embedded || package.source == PackageSource.Local)
                {
                    localPackagePaths.Add(package.resolvedPath);
                }
            }

            return EnumerateOwnedTextFiles(projectRoot, localPackagePaths);
        }

        /// <param name="getFiles">디렉터리 파일 열거 seam. 기본값은 <see cref="Directory.GetFiles(string)"/>다.</param>
        /// <param name="getDirectories">하위 디렉터리 열거 seam. 기본값은 <see cref="Directory.GetDirectories(string)"/>다.</param>
        internal static IReadOnlyList<GameplayTagReferenceFile> EnumerateOwnedTextFiles(
            string projectRoot,
            IReadOnlyList<string> localPackagePaths,
            Func<string, string[]>? getFiles = null,
            Func<string, string[]>? getDirectories = null)
        {
            if (projectRoot is null) throw new ArgumentNullException(nameof(projectRoot));
            if (localPackagePaths is null) throw new ArgumentNullException(nameof(localPackagePaths));

            var root = Path.GetFullPath(projectRoot);
            var roots = new List<string>
            {
                Path.Combine(root, "Assets"),
                Path.Combine(root, "ProjectSettings")
            };
            for (var index = 0; index < localPackagePaths.Count; index++)
            {
                var path = localPackagePaths[index];
                if (!string.IsNullOrEmpty(path)) roots.Add(Path.GetFullPath(path));
            }

            var comparer = PathComparer;
            var seen = new HashSet<string>(comparer);
            var files = new List<GameplayTagReferenceFile>();
            var readFiles = getFiles ?? new Func<string, string[]>(Directory.GetFiles);
            var readDirectories = getDirectories ?? new Func<string, string[]>(Directory.GetDirectories);
            for (var index = 0; index < roots.Count; index++)
            {
                Collect(roots[index], root, seen, files, readFiles, readDirectories);
            }

            files.Sort((left, right) => comparer.Compare(left.AbsolutePath, right.AbsolutePath));
            return files;
        }

        private static void Collect(
            string directory,
            string projectRoot,
            HashSet<string> seen,
            List<GameplayTagReferenceFile> files,
            Func<string, string[]> getFiles,
            Func<string, string[]> getDirectories)
        {
            if (!Directory.Exists(directory)) return;

            var pending = new Stack<string>();
            pending.Push(directory);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                string[] found;
                try
                {
                    found = getFiles(current);
                }
                catch (Exception exception) when (IsInaccessible(exception))
                {
                    // 권한이 없거나 스캔 도중 사라진 디렉터리는 그 디렉터리만 건너뛴다.
                    found = Array.Empty<string>();
                }

                foreach (var file in found)
                {
                    if (!TextExtensions.Contains(Path.GetExtension(file))) continue;

                    var absolutePath = Path.GetFullPath(file);
                    if (!seen.Add(absolutePath)) continue;

                    files.Add(new GameplayTagReferenceFile(
                        absolutePath, ToDisplayPath(absolutePath, projectRoot)));
                }

                string[] children;
                try
                {
                    children = getDirectories(current);
                }
                catch (Exception exception) when (IsInaccessible(exception))
                {
                    continue;
                }

                foreach (var child in children)
                {
                    FileAttributes attributes;
                    try
                    {
                        attributes = new DirectoryInfo(child).Attributes;
                    }
                    catch (Exception exception) when (IsInaccessible(exception))
                    {
                        continue;
                    }

                    // symlink/junction을 따라가면 같은 트리를 무한히 다시 훑을 수 있다.
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    pending.Push(child);
                }
            }
        }

        // DirectoryNotFoundException·PathTooLongException은 IOException 파생이라 함께 걸린다.
        private static bool IsInaccessible(Exception exception) =>
            exception is UnauthorizedAccessException ||
            exception is IOException ||
            exception is SecurityException;

        private static string ToDisplayPath(string absolutePath, string projectRoot)
        {
            var prefix = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var relative = absolutePath.Length > prefix.Length &&
                absolutePath.StartsWith(prefix, PathComparison)
                    ? absolutePath.Substring(prefix.Length)
                    : absolutePath;
            return relative
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
        }
    }
}
