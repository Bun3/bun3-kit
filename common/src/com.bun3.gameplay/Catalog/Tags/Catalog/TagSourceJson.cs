#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Bun3.Gameplay.Tags;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>
    /// Reads and writes strict tag source JSON documents.
    /// </summary>
    public static class TagSourceJson
    {
        private static readonly TagSourceDescriptor GameDescriptor = new TagSourceDescriptor("game", "Game", TagSourceKind.GameJson, false);

        /// <summary>Reads a game-owned tag source JSON document.</summary>
        public static TagSourceDocument LoadGame(Stream json, string origin) => Load(json, origin, false);

        /// <summary>Reads a package or native metadata tag source JSON document.</summary>
        public static TagSourceDocument LoadMetadata(Stream json, string origin) => Load(json, origin, true);

        /// <summary>Writes a game-owned tag source JSON document.</summary>
        public static void WriteGame(Stream destination, TagSourceDocument document)
        {
            if (document is null) throw new ArgumentNullException(nameof(document));
            if (document.Descriptor.Kind != TagSourceKind.GameJson) throw new ArgumentException("Only a GameJson source can be written as game JSON.", nameof(document));
            Write(destination, document, false);
        }

        /// <summary>Writes a package or native metadata tag source JSON document.</summary>
        public static void WriteMetadata(Stream destination, TagSourceDocument document)
        {
            if (document is null) throw new ArgumentNullException(nameof(document));
            if (document.Descriptor.Kind == TagSourceKind.GameJson) throw new ArgumentException("Only read-only sources can be written as metadata JSON.", nameof(document));
            Write(destination, document, true);
        }

        private static TagSourceDocument Load(Stream json, string origin, bool metadata)
        {
            if (json is null) throw new ArgumentNullException(nameof(json));
            if (origin is null) throw new ArgumentNullException(nameof(origin));

            string text;
            try
            {
                using var streamReader = new StreamReader(json, new UTF8Encoding(false, true), false, 1024, true);
                text = streamReader.ReadToEnd();
            }
            catch (DecoderFallbackException exception)
            {
                throw new TagCatalogException(exception.Message, origin, 1, 1);
            }

            TagCatalogJson.StrictJsonSyntax.Validate(text);
            try
            {
                using var stringReader = new StringReader(text);
                using var reader = new JsonTextReader(stringReader)
                {
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Decimal,
                    MaxDepth = 8,
                };
                var root = JObject.Load(reader, new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                    LineInfoHandling = LineInfoHandling.Load,
                });
                if (reader.Read()) throw Error("Unexpected token after the root JSON value.", root);

                return ReadDocument(root, origin, metadata);
            }
            catch (TagCatalogException)
            {
                throw;
            }
            catch (JsonReaderException exception)
            {
                throw new TagCatalogException(exception.Message, origin, Math.Max(1, exception.LineNumber), Math.Max(1, exception.LinePosition));
            }
            catch (ArgumentException exception)
            {
                throw new TagCatalogException(exception.Message, origin, 1, 1);
            }
        }

        private static TagSourceDocument ReadDocument(JObject root, string origin, bool metadata)
        {
            if (metadata)
            {
                RequireAllowedProperties(root, "schemaVersion", "source", "tags", "redirects");
            }
            else
            {
                RequireAllowedProperties(root, "schemaVersion", "tags", "redirects");
            }

            RequireSchemaVersion(root);
            var descriptor = metadata ? ReadDescriptor(RequireObject(root, "source")) : GameDescriptor;
            var tags = ReadTags(RequireArray(root, "tags"));
            var redirects = root.Property("redirects", StringComparison.Ordinal)?.Value;
            if (redirects is null && metadata) throw Error("Missing required field redirects.", root);
            return new TagSourceDocument(descriptor, origin, tags, ReadRedirects(redirects));
        }

        private static TagSourceDescriptor ReadDescriptor(JObject source)
        {
            RequireAllowedProperties(source, "id", "displayName", "kind");
            var sourceId = RequireString(source, "id");
            var displayName = RequireString(source, "displayName");
            var kindText = RequireString(source, "kind");
            var kind = kindText == "packageJson" ? TagSourceKind.PackageJson
                : kindText == "native" ? TagSourceKind.Native
                : throw Error("source.kind must be packageJson or native.", source);
            try
            {
                return new TagSourceDescriptor(sourceId, displayName, kind, true);
            }
            catch (ArgumentException exception)
            {
                throw Error(exception.Message, source);
            }
        }

        private static List<TagSourceTag> ReadTags(JArray tags)
        {
            var result = new List<TagSourceTag>(tags.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in tags)
            {
                if (item is not JObject tag) throw Error("tags items must be objects.", item);
                RequireAllowedProperties(tag, "name", "comment");
                var name = RequireString(tag, "name");
                var comment = RequireString(tag, "comment");
                var canonical = ValidateTagName(name, tag);
                if (!seen.Add(canonical)) throw Error("Duplicate tag name ignoring case.", tag);
                result.Add(new TagSourceTag(canonical, comment));
            }

            return result;
        }

        private static List<TagSourceRedirect> ReadRedirects(JToken? token)
        {
            if (token is null) return new List<TagSourceRedirect>();
            if (token is not JArray redirects) throw Error("redirects must be an array.", token);
            var result = new List<TagSourceRedirect>(redirects.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in redirects)
            {
                if (item is not JObject redirect) throw Error("redirects items must be objects.", item);
                RequireAllowedProperties(redirect, "from", "to");
                var from = ValidateTagName(RequireString(redirect, "from"), redirect);
                var to = ValidateTagName(RequireString(redirect, "to"), redirect);
                if (!seen.Add(from)) throw Error("Duplicate redirect origin ignoring case.", redirect);
                result.Add(new TagSourceRedirect(from, to));
            }

            return result;
        }

        private static string ValidateTagName(string value, JToken token)
        {
            var lineInfo = (IJsonLineInfo)token;
            return TagName.ValidateAndFold(value, token.Path, lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1, lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1);
        }

        private static void RequireSchemaVersion(JObject root)
        {
            var token = RequireProperty(root, "schemaVersion");
            if (token.Type != JTokenType.Integer || token is not JValue { Value: long value } || value != 1)
            {
                throw Error("schemaVersion must be the integer 1.", token);
            }
        }

        private static JArray RequireArray(JObject value, string propertyName)
        {
            var token = RequireProperty(value, propertyName);
            if (token is not JArray result) throw Error($"{propertyName} must be an array.", token);
            return result;
        }

        private static JObject RequireObject(JObject value, string propertyName)
        {
            var token = RequireProperty(value, propertyName);
            if (token is not JObject result) throw Error($"{propertyName} must be an object.", token);
            return result;
        }

        private static string RequireString(JObject value, string propertyName)
        {
            var token = RequireProperty(value, propertyName);
            if (token.Type != JTokenType.String) throw Error($"{propertyName} must be a string.", token);
            return token.Value<string>()!;
        }

        private static JToken RequireProperty(JObject value, string propertyName)
        {
            var property = value.Property(propertyName, StringComparison.Ordinal);
            if (property is null) throw Error($"Missing required field {propertyName}.", value);
            return property.Value;
        }

        private static void RequireAllowedProperties(JObject value, params string[] allowed)
        {
            foreach (var property in value.Properties())
            {
                var permitted = false;
                for (var i = 0; i < allowed.Length; i++)
                {
                    if (string.Equals(property.Name, allowed[i], StringComparison.Ordinal))
                    {
                        permitted = true;
                        break;
                    }
                }

                if (!permitted) throw Error($"Field not allowed: {property.Name}", property);
            }
        }

        private static TagCatalogException Error(string message, JToken token)
        {
            var lineInfo = (IJsonLineInfo)token;
            return new TagCatalogException(message, token.Path, lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1, lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1);
        }

        private static void Write(Stream destination, TagSourceDocument document, bool metadata)
        {
            if (destination is null) throw new ArgumentNullException(nameof(destination));
            using var streamWriter = new StreamWriter(destination, new UTF8Encoding(false), 1024, true);
            using var writer = new JsonTextWriter(streamWriter)
            {
                Formatting = Formatting.Indented,
                Indentation = 2,
                IndentChar = ' ',
            };
            writer.WriteStartObject();
            writer.WritePropertyName("schemaVersion");
            writer.WriteValue(1);
            if (metadata)
            {
                writer.WritePropertyName("source");
                writer.WriteStartObject();
                writer.WritePropertyName("id");
                writer.WriteValue(document.Descriptor.SourceId);
                writer.WritePropertyName("displayName");
                writer.WriteValue(document.Descriptor.DisplayName);
                writer.WritePropertyName("kind");
                writer.WriteValue(document.Descriptor.Kind == TagSourceKind.PackageJson ? "packageJson" : "native");
                writer.WriteEndObject();
            }

            writer.WritePropertyName("tags");
            writer.WriteStartArray();
            foreach (var tag in SortTags(document.Tags))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("name");
                writer.WriteValue(tag.Name);
                writer.WritePropertyName("comment");
                writer.WriteValue(tag.Comment);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("redirects");
            writer.WriteStartArray();
            foreach (var redirect in SortRedirects(document.Redirects))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("from");
                writer.WriteValue(redirect.From);
                writer.WritePropertyName("to");
                writer.WriteValue(redirect.To);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteWhitespace("\n");
            writer.Flush();
        }

        private static TagSourceTag[] SortTags(IReadOnlyList<TagSourceTag> tags)
        {
            var copy = new TagSourceTag[tags.Count];
            for (var i = 0; i < copy.Length; i++) copy[i] = tags[i];
            Array.Sort(copy, (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
            return copy;
        }

        private static TagSourceRedirect[] SortRedirects(IReadOnlyList<TagSourceRedirect> redirects)
        {
            var copy = new TagSourceRedirect[redirects.Count];
            for (var i = 0; i < copy.Length; i++) copy[i] = redirects[i];
            Array.Sort(copy, (left, right) => StringComparer.Ordinal.Compare(left.From, right.From));
            return copy;
        }
    }
}
