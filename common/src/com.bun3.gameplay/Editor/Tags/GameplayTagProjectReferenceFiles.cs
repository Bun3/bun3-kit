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
    /// <summary>프로젝트 텍스트 파일 열거 결과와 접근하지 못한 디렉터리 진단입니다.</summary>
    internal readonly struct GameplayTagReferenceFileSet
    {
        internal GameplayTagReferenceFileSet(
            IReadOnlyList<GameplayTagReferenceFile> files,
            IReadOnlyList<string> errors)
        {
            Files = files ?? throw new ArgumentNullException(nameof(files));
            Errors = errors ?? throw new ArgumentNullException(nameof(errors));
        }

        /// <summary>훑는 데 성공한 프로젝트 소유 텍스트 파일입니다.</summary>
        internal IReadOnlyList<GameplayTagReferenceFile> Files { get; }

        /// <summary>권한·잠금 때문에 훑지 못해 증거가 빠진 디렉터리 진단입니다.</summary>
        internal IReadOnlyList<string> Errors { get; }
    }

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

        /// <summary>현재 Unity 프로젝트가 소유한 텍스트 파일과 열거 실패 진단을 모읍니다.</summary>
        internal static GameplayTagReferenceFileSet Enumerate()
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

        /// <summary>프로젝트 소유 텍스트 파일을 훑고 접근하지 못한 디렉터리를 진단으로 남깁니다.</summary>
        /// <param name="projectRoot">Assets와 ProjectSettings를 담은 프로젝트 루트입니다.</param>
        /// <param name="localPackagePaths">추가로 훑을 embedded/local 패키지 경로입니다.</param>
        /// <param name="getFiles">디렉터리 파일 열거 seam. 기본값은 <see cref="Directory.GetFiles(string)"/>다.</param>
        /// <param name="getDirectories">하위 디렉터리 열거 seam. 기본값은 <see cref="Directory.GetDirectories(string)"/>다.</param>
        /// <returns>훑은 파일과 빠진 디렉터리 진단입니다.</returns>
        internal static GameplayTagReferenceFileSet EnumerateOwnedTextFiles(
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
            var errors = new List<string>();
            var readFiles = getFiles ?? new Func<string, string[]>(Directory.GetFiles);
            var readDirectories = getDirectories ?? new Func<string, string[]>(Directory.GetDirectories);
            for (var index = 0; index < roots.Count; index++)
            {
                Collect(roots[index], root, seen, files, errors, readFiles, readDirectories);
            }

            files.Sort((left, right) => comparer.Compare(left.AbsolutePath, right.AbsolutePath));
            return new GameplayTagReferenceFileSet(files, errors);
        }

        private static void Collect(
            string directory,
            string projectRoot,
            HashSet<string> seen,
            List<GameplayTagReferenceFile> files,
            List<string> errors,
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
                    // 권한이 없거나 스캔 도중 사라진 디렉터리는 그 디렉터리만 건너뛰되
                    // 증거가 빠졌음을 남겨 호출자가 정리 판정을 내리지 못하게 한다.
                    errors.Add(Describe(current, projectRoot, exception));
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
                    errors.Add(Describe(current, projectRoot, exception));
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
                        errors.Add(Describe(child, projectRoot, exception));
                        continue;
                    }

                    // symlink/junction을 따라가면 같은 트리를 무한히 다시 훑을 수 있다.
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    pending.Push(child);
                }
            }
        }

        private static string Describe(string directory, string projectRoot, Exception exception) =>
            ToDisplayPath(Path.GetFullPath(directory), projectRoot) + ": " + exception.Message;

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
