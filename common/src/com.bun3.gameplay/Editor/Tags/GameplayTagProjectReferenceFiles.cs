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
    /// <summary>Project text file enumeration result with diagnostics for inaccessible directories.</summary>
    internal readonly struct GameplayTagReferenceFileSet
    {
        internal GameplayTagReferenceFileSet(
            IReadOnlyList<GameplayTagReferenceFile> files,
            IReadOnlyList<string> errors)
        {
            Files = files ?? throw new ArgumentNullException(nameof(files));
            Errors = errors ?? throw new ArgumentNullException(nameof(errors));
        }

        /// <summary>Project-owned text files that were scanned successfully.</summary>
        internal IReadOnlyList<GameplayTagReferenceFile> Files { get; }

        /// <summary>Diagnostics for directories missing from the evidence due to permissions or locks.</summary>
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

        /// <summary>Collects text files owned by the current Unity project plus enumeration failure diagnostics.</summary>
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

        /// <summary>Scans project-owned text files and records inaccessible directories as diagnostics.</summary>
        /// <param name="projectRoot">Project root containing Assets and ProjectSettings.</param>
        /// <param name="localPackagePaths">Additional embedded/local package paths to scan.</param>
        /// <param name="getFiles">Directory file enumeration seam; defaults to <see cref="Directory.GetFiles(string)"/>.</param>
        /// <param name="getDirectories">Subdirectory enumeration seam; defaults to <see cref="Directory.GetDirectories(string)"/>.</param>
        /// <returns>Scanned files and diagnostics for missing directories.</returns>
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
                    // Skip only the directory that lacks permission or vanished mid-scan,
                    // but record the missing evidence so callers cannot conclude cleanup is safe.
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

                    // Following symlinks/junctions could rescan the same tree forever.
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    pending.Push(child);
                }
            }
        }

        private static string Describe(string directory, string projectRoot, Exception exception) =>
            ToDisplayPath(Path.GetFullPath(directory), projectRoot) + ": " + exception.Message;

        // DirectoryNotFoundException and PathTooLongException derive from IOException, so this catches them too.
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
