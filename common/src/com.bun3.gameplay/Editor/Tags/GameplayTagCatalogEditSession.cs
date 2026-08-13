#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Bun3.Gameplay.Tags;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bun3.Gameplay.Editor.Tags
{
    internal readonly struct EditableTagRow
    {
        internal EditableTagRow(string name, string comment)
        {
            Name = name;
            Comment = comment;
        }

        internal string Name { get; }
        internal string Comment { get; }
    }

    internal readonly struct EditableRedirectRow
    {
        internal EditableRedirectRow(string from, string to)
        {
            From = from;
            To = to;
        }

        internal string From { get; }
        internal string To { get; }
    }

    internal sealed class GameplayTagCatalogEditSession
    {
        private IReadOnlyList<EditableTagRow> _tags;
        private IReadOnlyList<EditableRedirectRow> _redirects;
        private string _serialized;

        private GameplayTagCatalogEditSession(
            IReadOnlyList<EditableTagRow> tags,
            IReadOnlyList<EditableRedirectRow> redirects,
            string serialized)
        {
            _tags = tags;
            _redirects = redirects;
            _serialized = serialized;
        }

        internal IReadOnlyList<EditableTagRow> Tags => _tags;

        internal IReadOnlyList<EditableRedirectRow> Redirects => _redirects;

        internal static GameplayTagCatalogEditSession Open(string json)
        {
            if (json is null) throw new ArgumentNullException(nameof(json));

            Validate(json);
            var root = JObject.Parse(json);
            var tags = new List<EditableTagRow>();
            foreach (var token in (JArray)root["tags"]!)
            {
                var row = (JObject)token;
                tags.Add(new EditableTagRow(
                    row.Value<string>("name")!,
                    row.Value<string>("comment") ?? string.Empty));
            }

            var redirects = new List<EditableRedirectRow>();
            var redirectTokens = root["redirects"] as JArray;
            if (redirectTokens is not null)
            {
                foreach (var token in redirectTokens)
                {
                    var row = (JObject)token;
                    redirects.Add(new EditableRedirectRow(
                        row.Value<string>("from")!,
                        row.Value<string>("to")!));
                }
            }

            return Create(tags, redirects);
        }

        internal void Add(string path, string comment = "")
        {
            Apply((tags, _) =>
            {
                var canonical = RequireCanonical(path, nameof(path));
                for (var i = 0; i < tags.Count; i++)
                {
                    if (Fold(tags[i].Name) == canonical)
                    {
                        throw new InvalidOperationException("The tag already has an authoring row.");
                    }
                }

                tags.Add(new EditableTagRow(path, comment ?? string.Empty));
            });
        }

        internal void SetComment(string path, string comment)
        {
            Apply((tags, _) =>
            {
                var canonical = RequireCanonical(path, nameof(path));
                for (var i = 0; i < tags.Count; i++)
                {
                    if (Fold(tags[i].Name) == canonical)
                    {
                        tags[i] = new EditableTagRow(tags[i].Name, comment ?? string.Empty);
                        return;
                    }
                }

                var catalog = EnsureActive(path, canonical, out var tag);
                tags.Add(new EditableTagRow(catalog.GetDisplayName(tag), comment ?? string.Empty));
            });
        }

        internal string RenameSubtree(string path, string newSegment)
        {
            var renamedPath = string.Empty;
            Apply((tags, redirects) =>
            {
                if (newSegment is null || newSegment.IndexOf('.') >= 0)
                {
                    throw new ArgumentException(
                        "The new name must be one gameplay tag segment.", nameof(newSegment));
                }

                _ = RequireCanonical(newSegment, nameof(newSegment));

                var oldCanonical = RequireCanonical(path, nameof(path));
                var catalog = EnsureActive(path, oldCanonical, out var tag);
                var oldDisplayPath = catalog.GetDisplayName(tag);
                var separator = oldDisplayPath.LastIndexOf('.');
                renamedPath = separator < 0
                    ? newSegment
                    : oldDisplayPath.Substring(0, separator + 1) + newSegment;
                var newCanonical = RequireCanonical(renamedPath, nameof(newSegment));
                var activePaths = GetActiveSubtreePaths(catalog, oldCanonical);

                if (oldCanonical != newCanonical
                    && catalog.TryGet(renamedPath, out var existingTag)
                    && Fold(catalog.GetDisplayName(existingTag)) == newCanonical)
                {
                    throw new InvalidOperationException("The destination path is already active.");
                }

                RenameTagRows(tags, oldCanonical, oldDisplayPath, renamedPath);
                if (oldCanonical == newCanonical)
                {
                    return;
                }

                for (var i = 0; i < redirects.Count; i++)
                {
                    var redirect = redirects[i];
                    redirects[i] = new EditableRedirectRow(
                        redirect.From,
                        RewritePrefix(redirect.To, oldCanonical, renamedPath));
                }

                for (var i = 0; i < activePaths.Count; i++)
                {
                    var activePath = activePaths[i];
                    redirects.Add(new EditableRedirectRow(
                        activePath,
                        RewritePrefix(activePath, oldCanonical, renamedPath)));
                }
            });
            return renamedPath;
        }

        internal int RemoveRedirects(IReadOnlyCollection<string> sources)
        {
            if (sources is null) throw new ArgumentNullException(nameof(sources));
            if (sources.Count == 0) return 0;

            var requested = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in sources)
            {
                requested.Add(RequireCanonical(source, nameof(sources)));
            }

            var removed = 0;
            Apply((_, redirects) =>
            {
                for (var i = redirects.Count - 1; i >= 0; i--)
                {
                    if (!requested.Contains(Fold(redirects[i].From))) continue;
                    redirects.RemoveAt(i);
                    removed++;
                }

                if (removed != requested.Count)
                {
                    throw new InvalidOperationException("A redirect source is no longer present.");
                }
            });
            return removed;
        }

        internal void Delete(string path, bool includeDescendants)
        {
            Apply((tags, redirects) =>
            {
                var canonical = RequireCanonical(path, nameof(path));
                var catalog = EnsureActive(path, canonical, out _);
                var activePaths = GetActiveSubtreePaths(catalog, canonical);
                if (!includeDescendants && activePaths.Count > 1)
                {
                    throw new InvalidOperationException("The tag has descendants.");
                }

                for (var i = tags.Count - 1; i >= 0; i--)
                {
                    if (HasPrefix(Fold(tags[i].Name), canonical))
                    {
                        tags.RemoveAt(i);
                    }
                }

                // 삭제로 암시 부모까지 사라질 수 있으므로 남은 활성 경로를 다시 만들어
                // target이 더 이상 활성이 아닌 redirect를 모두 같은 트랜잭션에서 지운다.
                var survivors = CollectActivePaths(tags);
                for (var i = redirects.Count - 1; i >= 0; i--)
                {
                    if (!survivors.Contains(Fold(redirects[i].To)))
                    {
                        redirects.RemoveAt(i);
                    }
                }
            });
        }

        /// <summary>남은 작성 행에서 암시 부모를 포함한 활성 canonical 경로를 모읍니다.</summary>
        private static HashSet<string> CollectActivePaths(List<EditableTagRow> tags)
        {
            var active = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < tags.Count; i++)
            {
                var canonical = Fold(tags[i].Name);
                active.Add(canonical);
                for (var separator = canonical.LastIndexOf('.');
                    separator > 0;
                    separator = canonical.LastIndexOf('.', separator - 1))
                {
                    active.Add(canonical.Substring(0, separator));
                }
            }

            return active;
        }

        internal string Serialize() => _serialized;

        private void Apply(Action<List<EditableTagRow>, List<EditableRedirectRow>> mutation)
        {
            var tags = new List<EditableTagRow>(_tags);
            var redirects = new List<EditableRedirectRow>(_redirects);
            mutation(tags, redirects);

            GameplayTagCatalogEditSession candidate;
            try
            {
                candidate = Create(tags, redirects);
            }
            catch (TagCatalogException exception)
            {
                throw new InvalidOperationException("The edit would create an invalid tag catalog.", exception);
            }

            _tags = candidate._tags;
            _redirects = candidate._redirects;
            _serialized = candidate._serialized;
        }

        private static GameplayTagCatalogEditSession Create(
            List<EditableTagRow> tags,
            List<EditableRedirectRow> redirects)
        {
            var serialized = Serialize(tags, redirects);
            Validate(serialized);
            return new GameplayTagCatalogEditSession(
                Array.AsReadOnly(tags.ToArray()),
                Array.AsReadOnly(redirects.ToArray()),
                serialized);
        }

        private static void Validate(string json)
        {
            using var stream = new MemoryStream(new UTF8Encoding(false, true).GetBytes(json));
            TagCatalog.Load(stream);
        }

        private TagCatalog EnsureActive(string path, string canonical, out GameplayTag tag)
        {
            using var stream = new MemoryStream(new UTF8Encoding(false, true).GetBytes(_serialized));
            var catalog = TagCatalog.Load(stream);
            if (!catalog.TryGet(path, out tag)
                || Fold(catalog.GetDisplayName(tag)) != canonical)
            {
                throw new InvalidOperationException("The path does not name an active tag.");
            }

            return catalog;
        }

        private static List<string> GetActiveSubtreePaths(TagCatalog catalog, string canonical)
        {
            var paths = new List<string>();
            for (var index = 1; index <= catalog.Count; index++)
            {
                var path = catalog.GetDisplayName(catalog.GetRequiredByIndex(checked((ushort)index)));
                if (HasPrefix(Fold(path), canonical))
                {
                    paths.Add(path);
                }
            }

            return paths;
        }

        private static void RenameTagRows(
            List<EditableTagRow> tags,
            string oldCanonical,
            string oldPath,
            string newPath)
        {
            for (var i = 0; i < tags.Count; i++)
            {
                var tag = tags[i];
                if (HasPrefix(Fold(tag.Name), oldCanonical))
                {
                    tags[i] = new EditableTagRow(
                        newPath + tag.Name.Substring(oldPath.Length),
                        tag.Comment);
                }
            }
        }

        private static string RewritePrefix(string path, string oldCanonical, string newPath)
        {
            return HasPrefix(Fold(path), oldCanonical)
                ? newPath + path.Substring(oldCanonical.Length)
                : path;
        }

        private static bool HasPrefix(string value, string prefix)
        {
            return value == prefix
                || (value.Length > prefix.Length
                    && value.StartsWith(prefix, StringComparison.Ordinal)
                    && value[prefix.Length] == '.');
        }

        private static string RequireCanonical(string path, string parameterName)
        {
            if (path is null || !TryFold(path, out var canonical))
            {
                throw new ArgumentException("The path must be a valid gameplay tag.", parameterName);
            }

            return canonical;
        }

        private static string Fold(string value)
        {
            if (!TryFold(value, out var canonical))
            {
                throw new InvalidOperationException("An authoring row contains an invalid gameplay tag.");
            }

            return canonical;
        }

        private static bool TryFold(string value, out string canonical)
        {
            canonical = string.Empty;
            if (value.Length == 0 || value.Length > 255) return false;

            var chars = value.ToCharArray();
            var depth = 1;
            var segmentLength = 0;
            for (var i = 0; i < chars.Length; i++)
            {
                var character = chars[i];
                if (character == '.')
                {
                    if (segmentLength == 0 || ++depth > 16) return false;
                    segmentLength = 0;
                    continue;
                }

                if (!((character >= 'a' && character <= 'z')
                    || (character >= 'A' && character <= 'Z')
                    || (character >= '0' && character <= '9')))
                {
                    return false;
                }

                if (character >= 'A' && character <= 'Z')
                {
                    chars[i] = (char)(character + ('a' - 'A'));
                }

                segmentLength++;
            }

            if (segmentLength == 0) return false;
            canonical = new string(chars);
            return true;
        }

        private static string Serialize(
            List<EditableTagRow> tags,
            List<EditableRedirectRow> redirects)
        {
            tags.Sort((left, right) => StringComparer.Ordinal.Compare(Fold(left.Name), Fold(right.Name)));
            redirects.Sort((left, right) => StringComparer.Ordinal.Compare(Fold(left.From), Fold(right.From)));

            using var stringWriter = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
            using (var writer = new JsonTextWriter(stringWriter)
            {
                Formatting = Formatting.Indented,
                Indentation = 2,
                IndentChar = ' ',
            })
            {
                writer.WriteStartObject();
                writer.WritePropertyName("schemaVersion");
                writer.WriteValue(1);
                writer.WritePropertyName("tags");
                writer.WriteStartArray();
                for (var i = 0; i < tags.Count; i++)
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("name");
                    writer.WriteValue(tags[i].Name);
                    writer.WritePropertyName("comment");
                    writer.WriteValue(tags[i].Comment);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WritePropertyName("redirects");
                writer.WriteStartArray();
                for (var i = 0; i < redirects.Count; i++)
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("from");
                    writer.WriteValue(redirects[i].From);
                    writer.WritePropertyName("to");
                    writer.WriteValue(redirects[i].To);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            return stringWriter.ToString() + "\n";
        }
    }
}
