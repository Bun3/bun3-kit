#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;
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
        private readonly Func<TagSourceDocument, TagCatalogCompilation> _compileCandidate;
        private TagSourceDocument _gameSource;
        private IReadOnlyList<EditableTagRow> _tags;
        private IReadOnlyList<EditableRedirectRow> _redirects;
        private string _serialized;

        private GameplayTagCatalogEditSession(
            TagSourceDocument gameSource,
            Func<TagSourceDocument, TagCatalogCompilation> compileCandidate)
        {
            _gameSource = gameSource;
            _compileCandidate = compileCandidate;
            _tags = CreateTagRows(gameSource.Tags);
            _redirects = CreateRedirectRows(gameSource.Redirects);
            _serialized = Serialize(gameSource);
        }

        internal IReadOnlyList<EditableTagRow> Tags => _tags;

        internal IReadOnlyList<EditableRedirectRow> Redirects => _redirects;

        internal TagSourceDocument GameSource => _gameSource;

        internal TagCatalogCompilation? LastCompilation { get; private set; }

        internal static GameplayTagCatalogEditSession Open(
            TagSourceDocument gameSource,
            Func<TagSourceDocument, TagCatalogCompilation> compileCandidate)
        {
            if (gameSource is null) throw new ArgumentNullException(nameof(gameSource));
            if (compileCandidate is null) throw new ArgumentNullException(nameof(compileCandidate));
            if (gameSource.Descriptor.Kind != TagSourceKind.GameJson
                || gameSource.Descriptor.IsReadOnly)
            {
                throw new ArgumentException("The edit session requires the writable Game Source.", nameof(gameSource));
            }

            return new GameplayTagCatalogEditSession(gameSource, compileCandidate);
        }

        internal static GameplayTagCatalogEditSession Open(string json)
        {
            if (json is null) throw new ArgumentNullException(nameof(json));
            var root = JObject.Parse(json);
            if (root["redirects"] is null) root["redirects"] = new JArray();
            if (root["tags"] is JArray tags)
            {
                for (var index = 0; index < tags.Count; index++)
                {
                    if (tags[index] is JObject tag && tag["comment"] is null)
                    {
                        tag["comment"] = string.Empty;
                    }
                }
            }

            using var stream = new MemoryStream(
                new UTF8Encoding(false, true).GetBytes(root.ToString()));
            var gameSource = TagSourceJson.LoadGame(stream, string.Empty);
            return Open(
                gameSource,
                candidate => TagCatalogCompiler.Compile(
                    new[] { candidate },
                    new TagCatalogIdentity("game", "0.0.0-dev")));
        }

        internal void Add(string path, string comment = "")
        {
            var canonical = RequireCanonical(path, nameof(path));
            Apply((tags, _) =>
            {
                for (var index = 0; index < tags.Count; index++)
                {
                    if (tags[index].Name == canonical)
                    {
                        throw new InvalidOperationException("The tag already has an authoring row.");
                    }
                }

                tags.Add(new TagSourceTag(canonical, comment ?? string.Empty));
            });
        }

        internal void SetComment(string path, string comment)
        {
            var canonical = RequireCanonical(path, nameof(path));
            EnsureLocallyActive(canonical);
            Apply((tags, _) =>
            {
                for (var index = 0; index < tags.Count; index++)
                {
                    if (tags[index].Name != canonical) continue;
                    tags[index] = new TagSourceTag(canonical, comment ?? string.Empty);
                    return;
                }

                tags.Add(new TagSourceTag(canonical, comment ?? string.Empty));
            });
        }

        internal GameplayTagRenameResult RenameSubtree(string path, string newSegment)
        {
            if (newSegment is null || newSegment.IndexOf('.') >= 0)
            {
                throw new ArgumentException(
                    "The new name must be one gameplay tag segment.", nameof(newSegment));
            }

            var canonicalSegment = RequireCanonical(newSegment, nameof(newSegment));
            var oldCanonical = RequireCanonical(path, nameof(path));
            var activePaths = CollectActivePaths(_gameSource.Tags);
            if (!activePaths.Contains(oldCanonical))
            {
                throw new InvalidOperationException("The path is not active in the Game Source.");
            }

            var separator = oldCanonical.LastIndexOf('.');
            var newCanonical = separator < 0
                ? canonicalSegment
                : oldCanonical.Substring(0, separator + 1) + canonicalSegment;
            if (oldCanonical == newCanonical)
            {
                return new GameplayTagRenameResult(newCanonical, Array.Empty<string>());
            }

            if (activePaths.Contains(newCanonical))
            {
                throw new InvalidOperationException("The destination path is already active in the Game Source.");
            }

            var renamedActivePaths = new List<string>();
            foreach (var activePath in activePaths)
            {
                if (HasPrefix(activePath, oldCanonical)) renamedActivePaths.Add(activePath);
            }

            renamedActivePaths.Sort(StringComparer.Ordinal);
            var renamedActivePathSet = new HashSet<string>(
                renamedActivePaths,
                StringComparer.Ordinal);
            var compilation = Apply((tags, redirects) =>
            {
                for (var index = 0; index < tags.Count; index++)
                {
                    var tag = tags[index];
                    if (!HasPrefix(tag.Name, oldCanonical)) continue;
                    tags[index] = new TagSourceTag(
                        RewritePrefix(tag.Name, oldCanonical, newCanonical),
                        tag.Comment);
                }

                for (var index = 0; index < redirects.Count; index++)
                {
                    var redirect = redirects[index];
                    if (!renamedActivePathSet.Contains(redirect.To)) continue;
                    redirects[index] = new TagSourceRedirect(
                        redirect.From,
                        RewritePrefix(redirect.To, oldCanonical, newCanonical));
                }

                UpsertRenameRedirects(redirects, renamedActivePaths, oldCanonical, newCanonical);
            });

            var warningPaths = new List<string>();
            for (var diagnosticIndex = 0;
                diagnosticIndex < compilation.Diagnostics.Count;
                diagnosticIndex++)
            {
                var diagnostic = compilation.Diagnostics[diagnosticIndex];
                if (diagnostic.Severity != TagCatalogDiagnosticSeverity.Warning
                    || diagnostic.Code != "B3TAG2001")
                {
                    continue;
                }

                warningPaths.Add(diagnostic.CanonicalPath);
            }

            var shadowed = ExtractShadowedOldPaths(renamedActivePaths, warningPaths);
            return new GameplayTagRenameResult(newCanonical, shadowed);
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
                for (var index = redirects.Count - 1; index >= 0; index--)
                {
                    if (!requested.Contains(redirects[index].From)) continue;
                    redirects.RemoveAt(index);
                    removed++;
                }

                if (removed != requested.Count)
                {
                    throw new InvalidOperationException("A redirect source is no longer present.");
                }
            });
            return removed;
        }

        internal void DeleteExact(string path)
        {
            var canonical = RequireCanonical(path, nameof(path));
            Apply((tags, _) =>
            {
                for (var index = 0; index < tags.Count; index++)
                {
                    if (tags[index].Name != canonical) continue;
                    tags.RemoveAt(index);
                    return;
                }

                throw new InvalidOperationException(
                    "The path is not an explicit tag in the Game Source.");
            });
        }

        internal void Delete(string path, bool includeDescendants)
        {
            if (includeDescendants)
            {
                throw new InvalidOperationException("Subtree deletion is not supported; delete one explicit tag.");
            }

            DeleteExact(path);
        }

        internal string Serialize() => _serialized;

        internal GameplayTagCatalogEditSession Restore(TagSourceDocument gameSource) =>
            Open(gameSource, _compileCandidate);

        internal static string Canonicalize(string path, string parameterName) =>
            RequireCanonical(path, parameterName);

        internal static IReadOnlyList<string> ExtractShadowedOldPaths(
            IReadOnlyCollection<string> renamedActivePaths,
            IReadOnlyList<string> warningPaths)
        {
            if (renamedActivePaths is null) throw new ArgumentNullException(nameof(renamedActivePaths));
            if (warningPaths is null) throw new ArgumentNullException(nameof(warningPaths));
            var renamed = new HashSet<string>(renamedActivePaths, StringComparer.Ordinal);
            var shadowed = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < warningPaths.Count; index++)
            {
                var path = warningPaths[index];
                if (renamed.Contains(path)) shadowed.Add(path);
            }

            var result = new string[shadowed.Count];
            shadowed.CopyTo(result);
            Array.Sort(result, StringComparer.Ordinal);
            return Array.AsReadOnly(result);
        }

        private TagCatalogCompilation Apply(
            Action<List<TagSourceTag>, List<TagSourceRedirect>> mutation)
        {
            var tags = new List<TagSourceTag>(_gameSource.Tags.Count);
            for (var index = 0; index < _gameSource.Tags.Count; index++)
            {
                var tag = _gameSource.Tags[index];
                tags.Add(new TagSourceTag(tag.Name, tag.Comment));
            }

            var redirects = new List<TagSourceRedirect>(_gameSource.Redirects.Count);
            for (var index = 0; index < _gameSource.Redirects.Count; index++)
            {
                var redirect = _gameSource.Redirects[index];
                redirects.Add(new TagSourceRedirect(redirect.From, redirect.To));
            }

            mutation(tags, redirects);
            var candidate = new TagSourceDocument(
                _gameSource.Descriptor,
                _gameSource.Origin,
                tags,
                redirects);
            var compilation = _compileCandidate(candidate)
                ?? throw new InvalidOperationException("The Workspace compiler returned no result.");
            if (!compilation.Succeeded)
            {
                throw CreateCompilationException(compilation.Diagnostics);
            }

            _gameSource = candidate;
            _tags = CreateTagRows(candidate.Tags);
            _redirects = CreateRedirectRows(candidate.Redirects);
            _serialized = Serialize(candidate);
            LastCompilation = compilation;
            return compilation;
        }

        private void EnsureLocallyActive(string canonical)
        {
            if (!CollectActivePaths(_gameSource.Tags).Contains(canonical))
            {
                throw new InvalidOperationException("The path is not active in the Game Source.");
            }
        }

        private static void UpsertRenameRedirects(
            List<TagSourceRedirect> redirects,
            List<string> activePaths,
            string oldPrefix,
            string newPrefix)
        {
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var index = 0; index < redirects.Count; index++)
            {
                indices[redirects[index].From] = index;
            }

            for (var index = 0; index < activePaths.Count; index++)
            {
                var from = activePaths[index];
                var replacement = new TagSourceRedirect(
                    from,
                    RewritePrefix(from, oldPrefix, newPrefix));
                if (indices.TryGetValue(from, out var existingIndex))
                {
                    redirects[existingIndex] = replacement;
                }
                else
                {
                    indices.Add(from, redirects.Count);
                    redirects.Add(replacement);
                }
            }
        }

        private static HashSet<string> CollectActivePaths(IReadOnlyList<TagSourceTag> tags)
        {
            var active = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < tags.Count; index++)
            {
                var canonical = tags[index].Name;
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

        private static IReadOnlyList<EditableTagRow> CreateTagRows(
            IReadOnlyList<TagSourceTag> tags)
        {
            var rows = new EditableTagRow[tags.Count];
            for (var index = 0; index < rows.Length; index++)
            {
                rows[index] = new EditableTagRow(tags[index].Name, tags[index].Comment);
            }

            Array.Sort(rows, (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
            return Array.AsReadOnly(rows);
        }

        private static IReadOnlyList<EditableRedirectRow> CreateRedirectRows(
            IReadOnlyList<TagSourceRedirect> redirects)
        {
            var rows = new EditableRedirectRow[redirects.Count];
            for (var index = 0; index < rows.Length; index++)
            {
                rows[index] = new EditableRedirectRow(redirects[index].From, redirects[index].To);
            }

            Array.Sort(rows, (left, right) => StringComparer.Ordinal.Compare(left.From, right.From));
            return Array.AsReadOnly(rows);
        }

        private static InvalidOperationException CreateCompilationException(
            IReadOnlyList<TagCatalogDiagnostic> diagnostics)
        {
            var message = new StringBuilder("The edit would create an invalid tag catalog.");
            for (var index = 0; index < diagnostics.Count; index++)
            {
                var diagnostic = diagnostics[index];
                message.Append(Environment.NewLine);
                message.Append(diagnostic.Code);
                message.Append(": ");
                message.Append(diagnostic.Message);
                if (diagnostic.SourceId.Length > 0)
                {
                    message.Append(" [");
                    message.Append(diagnostic.SourceId);
                    message.Append(']');
                }

                if (diagnostic.CanonicalPath.Length > 0)
                {
                    message.Append(" (");
                    message.Append(diagnostic.CanonicalPath);
                    message.Append(')');
                }
            }

            return new InvalidOperationException(message.ToString());
        }

        private static string RewritePrefix(string path, string oldPrefix, string newPrefix) =>
            HasPrefix(path, oldPrefix)
                ? newPrefix + path.Substring(oldPrefix.Length)
                : path;

        private static bool HasPrefix(string value, string prefix) =>
            value == prefix
            || (value.Length > prefix.Length
                && value.StartsWith(prefix, StringComparison.Ordinal)
                && value[prefix.Length] == '.');

        private static string RequireCanonical(string path, string parameterName)
        {
            if (path is null)
            {
                throw new ArgumentException("The path must be a valid gameplay tag.", parameterName);
            }

            try
            {
                return new TagSourceTag(path, string.Empty).Name;
            }
            catch (ArgumentException exception)
            {
                throw new ArgumentException(
                    "The path must be a valid gameplay tag.", parameterName, exception);
            }
        }

        private static string Serialize(TagSourceDocument gameSource)
        {
            using var stream = new MemoryStream();
            TagSourceJson.WriteGame(stream, gameSource);
            return new UTF8Encoding(false, true).GetString(stream.ToArray());
        }
    }
}
