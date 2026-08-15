#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace Bun3.Gameplay.Editor.Tags
{
    internal readonly struct GameplayTagReferenceFile
    {
        internal GameplayTagReferenceFile(string absolutePath, string displayPath)
        {
            AbsolutePath = absolutePath ?? throw new ArgumentNullException(nameof(absolutePath));
            DisplayPath = displayPath ?? throw new ArgumentNullException(nameof(displayPath));
        }

        internal string AbsolutePath { get; }

        internal string DisplayPath { get; }
    }

    internal readonly struct GameplayTagReferenceMatch
    {
        internal GameplayTagReferenceMatch(
            string redirectSource, string absolutePath, string displayPath, int lineNumber, string preview)
        {
            RedirectSource = redirectSource ?? throw new ArgumentNullException(nameof(redirectSource));
            AbsolutePath = absolutePath ?? throw new ArgumentNullException(nameof(absolutePath));
            DisplayPath = displayPath ?? throw new ArgumentNullException(nameof(displayPath));
            LineNumber = lineNumber;
            Preview = preview ?? throw new ArgumentNullException(nameof(preview));
        }

        internal string RedirectSource { get; }

        internal string AbsolutePath { get; }

        internal string DisplayPath { get; }

        internal int LineNumber { get; }

        internal string Preview { get; }
    }

    internal readonly struct GameplayTagReferenceProgress
    {
        internal GameplayTagReferenceProgress(string displayPath, float fraction)
        {
            DisplayPath = displayPath ?? throw new ArgumentNullException(nameof(displayPath));
            Fraction = fraction;
        }

        internal string DisplayPath { get; }

        internal float Fraction { get; }
    }

    internal readonly struct GameplayTagReferenceSearchResult
    {
        private GameplayTagReferenceSearchResult(
            bool isComplete,
            bool isCancelled,
            IReadOnlyList<GameplayTagReferenceMatch> matches,
            IReadOnlyList<string> errors)
        {
            IsComplete = isComplete;
            IsCancelled = isCancelled;
            Matches = matches;
            Errors = errors;
        }

        internal bool IsComplete { get; }

        internal bool IsCancelled { get; }

        internal IReadOnlyList<GameplayTagReferenceMatch> Matches { get; }

        internal IReadOnlyList<string> Errors { get; }

        internal static GameplayTagReferenceSearchResult Complete(
            IReadOnlyList<GameplayTagReferenceMatch> matches) =>
            new GameplayTagReferenceSearchResult(
                true,
                false,
                matches ?? throw new ArgumentNullException(nameof(matches)),
                Array.Empty<string>());

        internal static GameplayTagReferenceSearchResult Incomplete(
            bool cancelled,
            IReadOnlyList<string> errors) =>
            new GameplayTagReferenceSearchResult(
                false,
                cancelled,
                Array.Empty<GameplayTagReferenceMatch>(),
                errors ?? throw new ArgumentNullException(nameof(errors)));

        /// <summary>파일 열거 실패를 합쳐 불완전한 증거로 정리 판정을 내리지 못하게 합니다.</summary>
        /// <param name="enumerationErrors">접근할 수 없어 훑지 못한 디렉터리 진단입니다.</param>
        /// <returns>열거 실패가 없으면 이 결과 그대로, 있으면 불완전 결과입니다.</returns>
        internal GameplayTagReferenceSearchResult WithEnumerationErrors(
            IReadOnlyList<string> enumerationErrors)
        {
            if (enumerationErrors is null) throw new ArgumentNullException(nameof(enumerationErrors));
            if (enumerationErrors.Count == 0) return this;

            var errors = new List<string>(enumerationErrors.Count + Errors.Count);
            errors.AddRange(enumerationErrors);
            errors.AddRange(Errors);
            return Incomplete(IsCancelled, errors);
        }
    }

    internal sealed class GameplayTagTextReferenceScanner
    {
        private const int PreviewLength = 200;

        private readonly Func<string, TextReader> _openText;

        internal GameplayTagTextReferenceScanner(Func<string, TextReader> openText)
        {
            _openText = openText ?? throw new ArgumentNullException(nameof(openText));
        }

        internal GameplayTagReferenceSearchResult Search(
            IReadOnlyList<GameplayTagReferenceFile> files,
            IReadOnlyList<string> redirectSources,
            string excludedCatalogPath,
            Func<GameplayTagReferenceProgress, bool>? isCancelled)
        {
            if (files is null) throw new ArgumentNullException(nameof(files));
            if (redirectSources is null) throw new ArgumentNullException(nameof(redirectSources));
            if (excludedCatalogPath is null) throw new ArgumentNullException(nameof(excludedCatalogPath));

            var sources = new Dictionary<string, string>(
                redirectSources.Count, StringComparer.OrdinalIgnoreCase);
            var shortestSource = int.MaxValue;
            var longestSource = 0;
            for (var index = 0; index < redirectSources.Count; index++)
            {
                var source = redirectSources[index];
                if (string.IsNullOrEmpty(source)) continue;
                sources[source] = source;
                if (source.Length < shortestSource) shortestSource = source.Length;
                if (source.Length > longestSource) longestSource = source.Length;
            }

            if (sources.Count == 0)
            {
                return GameplayTagReferenceSearchResult.Complete(Array.Empty<GameplayTagReferenceMatch>());
            }

            var excluded = excludedCatalogPath.Length == 0
                ? null
                : Path.GetFullPath(excludedCatalogPath);
            var matches = new List<GameplayTagReferenceMatch>();
            var errors = new List<string>();
            var cancelled = false;

            for (var index = 0; index < files.Count; index++)
            {
                var file = files[index];
                try
                {
                    if (excluded != null &&
                        string.Equals(
                            Path.GetFullPath(file.AbsolutePath),
                            excluded,
                            GameplayTagProjectReferenceFiles.PathComparison))
                    {
                        continue;
                    }

                    if (isCancelled != null && isCancelled(
                            new GameplayTagReferenceProgress(file.DisplayPath, (float)index / files.Count)))
                    {
                        cancelled = true;
                        break;
                    }

                    using var reader = _openText(file.AbsolutePath);
                    Scan(reader, file, sources, shortestSource, longestSource, matches);
                }
                catch (Exception exception)
                {
                    // 임의의 프로젝트 파일을 읽으므로 잠금·권한·경로 예외를 모두 부분 실패로 기록한다.
                    errors.Add(file.DisplayPath + ": " + exception.Message);
                }
            }

            return cancelled || errors.Count > 0
                ? GameplayTagReferenceSearchResult.Incomplete(cancelled, errors)
                : GameplayTagReferenceSearchResult.Complete(matches);
        }

        /// <summary>하나의 canonical 태그 경로와 정확히 같은 토큰만 검색합니다.</summary>
        internal GameplayTagReferenceSearchResult SearchExact(
            IReadOnlyList<GameplayTagReferenceFile> files,
            string canonicalPath,
            string excludedCatalogPath,
            Func<GameplayTagReferenceProgress, bool>? isCancelled)
        {
            if (canonicalPath is null) throw new ArgumentNullException(nameof(canonicalPath));
            var canonical = GameplayTagCatalogEditSession.Canonicalize(
                canonicalPath,
                nameof(canonicalPath));
            return Search(files, new[] { canonical }, excludedCatalogPath, isCancelled);
        }

        private static void Scan(
            TextReader reader,
            GameplayTagReferenceFile file,
            Dictionary<string, string> sources,
            int shortestSource,
            int longestSource,
            List<GameplayTagReferenceMatch> matches)
        {
            var lineNumber = 0;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                if (lineNumber == 1 && line.IndexOf('\0') >= 0) return;

                string? preview = null;
                var index = 0;
                while (index < line.Length)
                {
                    if (!IsTokenCharacter(line[index]))
                    {
                        index++;
                        continue;
                    }

                    var start = index;
                    while (index < line.Length && IsTokenCharacter(line[index])) index++;

                    var length = index - start;
                    if (length < shortestSource || length > longestSource) continue;
                    if (!sources.TryGetValue(line.Substring(start, length), out var source)) continue;

                    preview ??= Preview(line);
                    matches.Add(new GameplayTagReferenceMatch(
                        source, file.AbsolutePath, file.DisplayPath, lineNumber, preview));
                }
            }
        }

        private static bool IsTokenCharacter(char value) =>
            (value >= 'A' && value <= 'Z') ||
            (value >= 'a' && value <= 'z') ||
            (value >= '0' && value <= '9') ||
            value == '.';

        private static string Preview(string line)
        {
            var trimmed = line.Trim();
            return trimmed.Length <= PreviewLength ? trimmed : trimmed.Substring(0, PreviewLength);
        }
    }
}
