#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bun3.Gameplay.TagSource.Tasks
{
    internal static class NativeTagMetadataValidator
    {
        internal static void Validate(Stream input)
        {
            if (input is null) throw new ArgumentNullException(nameof(input));

            try
            {
                using var textReader = new StreamReader(
                    input,
                    new UTF8Encoding(false, true),
                    false,
                    1024,
                    true);
                using var jsonReader = new JsonTextReader(textReader)
                {
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Decimal,
                    MaxDepth = 8,
                };
                var root = JObject.Load(jsonReader, new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                    LineInfoHandling = LineInfoHandling.Load,
                });
                if (jsonReader.Read()) throw new InvalidDataException("Native metadata contains trailing JSON content.");

                RequireProperties(root, "schemaVersion", "source", "tags", "redirects");
                RequireInteger(root["schemaVersion"], 1, "schemaVersion");
                ValidateSource(RequireObject(root["source"], "source"));
                ValidateTags(RequireArray(root["tags"], "tags"));
                if (RequireArray(root["redirects"], "redirects").Count != 0)
                {
                    throw new InvalidDataException("Native metadata redirects must be empty.");
                }
            }
            catch (Exception exception) when (exception is JsonException || exception is DecoderFallbackException)
            {
                throw new InvalidDataException("Native metadata readback failed.", exception);
            }
        }

        private static void ValidateSource(JObject source)
        {
            RequireProperties(source, "id", "displayName", "kind");
            var id = RequireString(source["id"], "source.id");
            if (!IsValidSourceId(id) || string.Equals(id, "game", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Native metadata source.id is invalid.");
            }

            if (string.IsNullOrWhiteSpace(RequireString(source["displayName"], "source.displayName")))
            {
                throw new InvalidDataException("Native metadata source.displayName is invalid.");
            }

            if (!string.Equals(RequireString(source["kind"], "source.kind"), "native", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Native metadata source.kind must be native.");
            }
        }

        private static void ValidateTags(JArray tags)
        {
            string? previous = null;
            foreach (var token in tags)
            {
                var tag = RequireObject(token, "tags[]");
                RequireProperties(tag, "name", "comment");
                var name = RequireString(tag["name"], "tags[].name");
                RequireString(tag["comment"], "tags[].comment");
                if (!IsCanonicalTagName(name))
                {
                    throw new InvalidDataException("Native metadata contains an invalid canonical tag name.");
                }

                if (previous is not null && StringComparer.Ordinal.Compare(previous, name) >= 0)
                {
                    throw new InvalidDataException("Native metadata tags must be unique and ordinally sorted.");
                }

                previous = name;
            }
        }

        private static void RequireProperties(JObject value, params string[] expected)
        {
            var actual = value.Properties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal);
            var orderedExpected = expected.OrderBy(name => name, StringComparer.Ordinal);
            if (!actual.SequenceEqual(orderedExpected, StringComparer.Ordinal))
            {
                throw new InvalidDataException("Native metadata contains missing or unknown properties.");
            }
        }

        private static JObject RequireObject(JToken? token, string name) =>
            token as JObject ?? throw new InvalidDataException("Native metadata " + name + " must be an object.");

        private static JArray RequireArray(JToken? token, string name) =>
            token as JArray ?? throw new InvalidDataException("Native metadata " + name + " must be an array.");

        private static string RequireString(JToken? token, string name)
        {
            if (token?.Type != JTokenType.String)
            {
                throw new InvalidDataException("Native metadata " + name + " must be a string.");
            }

            return token.Value<string>()!;
        }

        private static void RequireInteger(JToken? token, long expected, string name)
        {
            if (token?.Type != JTokenType.Integer || token.Value<long>() != expected)
            {
                throw new InvalidDataException("Native metadata " + name + " has an unsupported value.");
            }
        }

        private static bool IsValidSourceId(string value)
        {
            if (value.Length == 0) return false;
            var separator = true;
            foreach (var character in value)
            {
                if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9'))
                {
                    separator = false;
                }
                else if ((character == '.' || character == '-') && !separator)
                {
                    separator = true;
                }
                else
                {
                    return false;
                }
            }

            return !separator;
        }

        private static bool IsCanonicalTagName(string value)
        {
            if (value.Length == 0 || value.Length > 255) return false;
            var depth = 1;
            var segmentLength = 0;
            foreach (var character in value)
            {
                if (character == '.')
                {
                    if (segmentLength == 0 || ++depth > 16) return false;
                    segmentLength = 0;
                    continue;
                }

                if (!((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9')))
                {
                    return false;
                }

                segmentLength++;
            }

            return segmentLength != 0;
        }
    }
}
