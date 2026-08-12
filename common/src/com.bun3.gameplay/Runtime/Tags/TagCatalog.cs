#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace Bun3.Gameplay.Tags
{
    /// <summary>
    /// 엄격한 JSON에서 한 번 만들어진 뒤 변경되지 않는 게임플레이 태그 카탈로그입니다.
    /// </summary>
    public sealed partial class TagCatalog
    {
        private readonly Dictionary<string, ushort> _byCanonicalName;
        private readonly Dictionary<string, ushort> _redirects;
        private readonly string[] _displayNames;
        private readonly ushort[] _parents;
        private readonly ushort[] _subtreeEnds;
        private readonly byte[] _fingerprint;

        private TagCatalog(
            Dictionary<string, ushort> byCanonicalName,
            string[] displayNames,
            ushort[] parents,
            ushort[] subtreeEnds)
            : this(
                byCanonicalName,
                new Dictionary<string, ushort>(StringComparer.Ordinal),
                displayNames,
                parents,
                subtreeEnds,
                ComputeFingerprint(1, CreateCanonicalNames(byCanonicalName, displayNames.Length), Array.Empty<RedirectEntry>()))
        {
        }

        private TagCatalog(
            Dictionary<string, ushort> byCanonicalName,
            Dictionary<string, ushort> redirects,
            string[] displayNames,
            ushort[] parents,
            ushort[] subtreeEnds,
            byte[] fingerprint)
        {
            _byCanonicalName = byCanonicalName;
            _redirects = redirects;
            _displayNames = displayNames;
            _parents = parents;
            _subtreeEnds = subtreeEnds;
            _fingerprint = fingerprint;
            Count = displayNames.Length - 1;
        }

        private static TagCatalog Create(List<ExplicitTag> explicitTags, List<RedirectDefinition> definitions)
        {
            var catalog = Build(explicitTags);
            var redirects = BuildRedirects(definitions, catalog._byCanonicalName, out var fingerprintRedirects);
            var canonicalNames = CreateCanonicalNames(catalog._byCanonicalName, catalog._displayNames.Length);
            return new TagCatalog(
                catalog._byCanonicalName,
                redirects,
                catalog._displayNames,
                catalog._parents,
                catalog._subtreeEnds,
                ComputeFingerprint(1, canonicalNames, fingerprintRedirects));
        }

        private static Dictionary<string, ushort> BuildRedirects(
            List<RedirectDefinition> definitions,
            Dictionary<string, ushort> byCanonicalName,
            out RedirectEntry[] fingerprintRedirects)
        {
            var redirects = new Dictionary<string, ushort>(definitions.Count, StringComparer.Ordinal);
            var entries = new List<RedirectEntry>(definitions.Count);
            foreach (var definition in definitions)
            {
                if (byCanonicalName.ContainsKey(definition.From))
                {
                    throw new TagCatalogException("redirect source는 활성 태그와 겹칠 수 없습니다.", definition.JsonPath, definition.LineNumber, definition.LinePosition);
                }

                if (!byCanonicalName.TryGetValue(definition.To, out var target))
                {
                    throw new TagCatalogException("redirect target은 활성 태그여야 합니다.", definition.JsonPath, definition.LineNumber, definition.LinePosition);
                }

                if (!redirects.TryAdd(definition.From, target))
                {
                    throw new TagCatalogException("대소문자를 제외하고 중복된 redirect source입니다.", definition.JsonPath, definition.LineNumber, definition.LinePosition);
                }

                entries.Add(new RedirectEntry(definition.From, definition.To));
            }

            entries.Sort((left, right) => StringComparer.Ordinal.Compare(left.From, right.From));
            fingerprintRedirects = entries.ToArray();
            return redirects;
        }

        private static string[] CreateCanonicalNames(Dictionary<string, ushort> byCanonicalName, int length)
        {
            var canonicalNames = new string[length];
            foreach (var pair in byCanonicalName)
            {
                canonicalNames[pair.Value] = pair.Key;
            }

            return canonicalNames;
        }

        /// <summary>카탈로그에 있는 태그 수이며 None은 포함하지 않습니다.</summary>
        public int Count { get; }

        /// <summary>Creates an empty container bound to this catalog.</summary>
        /// <param name="expectedExactKinds">The expected number of explicit tag kinds, from 0 through 64.</param>
        /// <returns>An empty tag container.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="expectedExactKinds"/> is outside 0 through 64.</exception>
        public TagContainer CreateContainer(int expectedExactKinds = 0) => new TagContainer(this, expectedExactKinds);

        /// <summary>카탈로그의 canonical SHA-256 fingerprint를 가져옵니다.</summary>
        public ReadOnlySpan<byte> Fingerprint => _fingerprint;

        /// <summary>입력 fingerprint가 이 카탈로그의 fingerprint와 같은지 검사합니다.</summary>
        /// <param name="other">비교할 fingerprint bytes입니다.</param>
        /// <returns>두 fingerprint가 같으면 true입니다.</returns>
        public bool MatchesFingerprint(ReadOnlySpan<byte> other) => other.SequenceEqual(_fingerprint);

        /// <summary>
        /// UTF-8 JSON 스트림의 현재 위치부터 끝까지 읽어 불변 카탈로그를 만듭니다.
        /// </summary>
        /// <param name="utf8Json">읽을 수 있는 UTF-8 JSON 스트림입니다.</param>
        /// <returns>검증되고 색인화된 카탈로그입니다.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="utf8Json"/>이 null인 경우입니다.</exception>
        /// <exception cref="ArgumentException">스트림을 읽을 수 없는 경우입니다.</exception>
        /// <exception cref="TagCatalogException">JSON 또는 카탈로그가 유효하지 않은 경우입니다.</exception>
        public static TagCatalog Load(Stream utf8Json)
        {
            if (utf8Json is null) throw new ArgumentNullException(nameof(utf8Json));
            if (!utf8Json.CanRead) throw new ArgumentException("읽을 수 있는 스트림이 필요합니다.", nameof(utf8Json));
            return Loader.Load(utf8Json);
        }

        /// <summary>경로에 해당하는 등록 태그를 찾습니다.</summary>
        /// <param name="path">ASCII 영숫자 세그먼트 경로입니다.</param>
        /// <param name="tag">찾은 태그 또는 None입니다.</param>
        /// <returns>문법상 유효한 경로가 등록되어 있으면 true입니다.</returns>
        /// <exception cref="ArgumentException"><paramref name="path"/> 문법이 올바르지 않은 경우입니다.</exception>
        public bool TryGet(string path, out GameplayTag tag)
        {
            if (!TagName.TryFold(path, out var canonical))
            {
                throw new ArgumentException("태그 경로 문법이 올바르지 않습니다.", nameof(path));
            }

            if (_byCanonicalName.TryGetValue(canonical, out var index))
            {
                tag = new GameplayTag(index);
                return true;
            }

            if (_redirects.TryGetValue(canonical, out index))
            {
                tag = new GameplayTag(index);
                return true;
            }

            tag = GameplayTag.None;
            return false;
        }

        /// <summary>경로에 해당하는 등록 태그를 찾거나 없으면 예외를 던집니다.</summary>
        /// <param name="path">ASCII 영숫자 세그먼트 경로입니다.</param>
        /// <returns>찾은 태그입니다.</returns>
        /// <exception cref="ArgumentException"><paramref name="path"/> 문법이 올바르지 않은 경우입니다.</exception>
        /// <exception cref="KeyNotFoundException">유효한 경로가 등록되어 있지 않은 경우입니다.</exception>
        public GameplayTag GetRequired(string path)
        {
            if (TryGet(path, out var tag))
            {
                return tag;
            }

            throw new KeyNotFoundException($"등록되지 않은 태그 경로입니다: {path}");
        }

        /// <summary>카탈로그 범위 안의 wire index를 태그로 복원합니다.</summary>
        /// <param name="index">복원할 wire index입니다.</param>
        /// <param name="tag">복원한 태그 또는 None입니다.</param>
        /// <returns>index가 카탈로그 범위 안이면 true입니다.</returns>
        public bool TryGetByIndex(ushort index, out GameplayTag tag)
        {
            if (index <= Count)
            {
                tag = new GameplayTag(index);
                return true;
            }

            tag = GameplayTag.None;
            return false;
        }

        /// <summary>카탈로그 범위 안의 wire index를 태그로 복원합니다.</summary>
        /// <param name="index">복원할 wire index입니다.</param>
        /// <returns>복원한 태그입니다.</returns>
        /// <exception cref="ArgumentOutOfRangeException">index가 카탈로그 범위를 벗어난 경우입니다.</exception>
        public GameplayTag GetRequiredByIndex(ushort index)
        {
            if (TryGetByIndex(index, out var tag))
            {
                return tag;
            }

            throw new ArgumentOutOfRangeException(nameof(index));
        }

        /// <summary>태그의 표시용 대소문자 보존 이름을 가져옵니다.</summary>
        /// <param name="tag">조회할 태그입니다.</param>
        /// <returns>등록된 표시 이름 또는 빈 문자열입니다.</returns>
        public string GetDisplayName(GameplayTag tag) =>
            tag.IsValid && tag.Index <= Count ? _displayNames[tag.Index] : string.Empty;

        /// <summary>태그의 직접 부모를 가져오며 루트 또는 잘못된 태그에는 None을 반환합니다.</summary>
        /// <param name="tag">조회할 태그입니다.</param>
        /// <returns>직접 부모 또는 None입니다.</returns>
        public GameplayTag GetParent(GameplayTag tag) =>
            tag.IsValid && tag.Index <= Count ? new GameplayTag(_parents[tag.Index]) : GameplayTag.None;

        /// <summary>ancestor가 tag 자신 또는 조상인지 검사합니다.</summary>
        /// <param name="ancestor">후보 조상 태그입니다.</param>
        /// <param name="tag">후손 후보 태그입니다.</param>
        /// <returns>ancestor가 tag 자신 또는 조상이면 true입니다.</returns>
        public bool IsAncestorOrSelf(GameplayTag ancestor, GameplayTag tag)
        {
            if (!ancestor.IsValid || !tag.IsValid || ancestor.Index > Count || tag.Index > Count)
            {
                return false;
            }

            return ancestor.Index <= tag.Index && tag.Index <= _subtreeEnds[ancestor.Index];
        }

        internal ushort GetSubtreeEnd(GameplayTag tag) =>
            tag.IsValid && tag.Index <= Count ? _subtreeEnds[tag.Index] : (ushort)0;

        private readonly struct RedirectDefinition
        {
            internal RedirectDefinition(string from, string to, string jsonPath, int lineNumber, int linePosition)
            {
                From = from;
                To = to;
                JsonPath = jsonPath;
                LineNumber = lineNumber;
                LinePosition = linePosition;
            }

            internal string From { get; }
            internal string To { get; }
            internal string JsonPath { get; }
            internal int LineNumber { get; }
            internal int LinePosition { get; }
        }

        private readonly struct RedirectEntry
        {
            internal RedirectEntry(string from, string to)
            {
                From = from;
                To = to;
            }

            internal string From { get; }
            internal string To { get; }
        }
    }
}
