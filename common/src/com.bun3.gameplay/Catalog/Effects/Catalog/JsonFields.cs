// Shared by the effect and ability authoring loaders: strict document loading and primitive field readers.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bun3.Gameplay.Effects.Catalog
{
    /// <summary>Strict JSON document loading and field readers shared by <see cref="EffectDefJson"/> and the
    /// ability loader. Documents have the shape <c>{ "schemaVersion": 1, "&lt;array&gt;": [ ... ] }</c>.</summary>
    internal static class JsonFields
    {
        internal static List<T> LoadDocument<T>(
            Stream utf8Json, IReadOnlyDictionary<string, ushort> attributeNames, string arrayName,
            Func<JObject, IReadOnlyDictionary<string, ushort>, T> readItem)
        {
            if (utf8Json is null) throw new ArgumentNullException(nameof(utf8Json));
            if (!utf8Json.CanRead) throw new ArgumentException("A readable stream is required.", nameof(utf8Json));
            if (attributeNames is null) throw new ArgumentNullException(nameof(attributeNames));

            string text;
            try
            {
                using var streamReader = new StreamReader(utf8Json, new UTF8Encoding(false, true), false, 1024, true);
                text = streamReader.ReadToEnd();
            }
            catch (DecoderFallbackException exception)
            {
                throw new TagCatalogException(exception.Message, string.Empty, 1, 1);
            }

            TagCatalogJson.StrictJsonSyntax.Validate(text);

            try
            {
                using var stringReader = new StringReader(text);
                using var reader = new JsonTextReader(stringReader)
                {
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Decimal,
                    MaxDepth = 16,
                };
                var root = JObject.Load(reader, new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                    LineInfoHandling = LineInfoHandling.Load,
                });
                if (reader.Read())
                {
                    throw Error("Unexpected token after the root JSON value.", root);
                }

                return ReadRoot(root, attributeNames, arrayName, readItem);
            }
            catch (TagCatalogException)
            {
                throw;
            }
            catch (JsonReaderException exception)
            {
                throw new TagCatalogException(
                    exception.Message, string.Empty,
                    Math.Max(1, exception.LineNumber), Math.Max(1, exception.LinePosition));
            }
        }

        private static List<T> ReadRoot<T>(
            JObject root, IReadOnlyDictionary<string, ushort> attributeNames, string arrayName,
            Func<JObject, IReadOnlyDictionary<string, ushort>, T> readItem)
        {
            RequireAllowedProperties(root, "schemaVersion", arrayName);
            RequireSchemaVersion(root);

            var items = RequireArray(root, arrayName);
            var result = new List<T>(items.Count);
            foreach (var item in items)
            {
                result.Add(readItem(AsObject(item, $"{arrayName} items must be objects."), attributeNames));
            }

            return result;
        }

        private static void RequireSchemaVersion(JObject root)
        {
            var token = RequireProperty(root, "schemaVersion");
            if (token.Type != JTokenType.Integer || token is not JValue { Value: long value } || value != 1)
            {
                throw Error("schemaVersion must be the integer 1.", token);
            }
        }

        internal static JToken RequireProperty(JObject value, string propertyName)
        {
            var property = value.Property(propertyName, StringComparison.Ordinal);
            if (property is null)
            {
                throw Error($"Missing required field {propertyName}.", value);
            }

            return property.Value;
        }

        internal static void RequireAllowedProperties(JObject value, params string[] allowed)
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

                if (!permitted)
                {
                    throw Error($"Field not allowed: {property.Name}", property);
                }
            }
        }

        internal static JObject RequireObject(JObject value, string propertyName) =>
            AsObject(RequireProperty(value, propertyName), $"{propertyName} must be an object.");

        internal static JArray RequireArray(JObject value, string propertyName) =>
            AsArray(RequireProperty(value, propertyName), $"{propertyName} must be an array.");

        internal static string RequireString(JObject value, string propertyName) =>
            RequireStringValue(RequireProperty(value, propertyName), propertyName);

        internal static string RequireStringValue(JToken token, string propertyName)
        {
            if (token.Type != JTokenType.String)
            {
                throw Error($"{propertyName} must be a string.", token);
            }

            return token.Value<string>()!;
        }

        internal static int RequireInt(JObject value, string propertyName)
        {
            var token = RequireProperty(value, propertyName);
            if (token.Type != JTokenType.Integer || token is not JValue { Value: long longValue }
                || longValue < int.MinValue || longValue > int.MaxValue)
            {
                throw Error($"{propertyName} must be an integer.", token);
            }

            return (int)longValue;
        }

        internal static int ReadOptionalInt(JObject value, string propertyName, int fallback) =>
            value.Property(propertyName, StringComparison.Ordinal) != null ? RequireInt(value, propertyName) : fallback;

        internal static bool RequireBool(JObject value, string propertyName)
        {
            var token = RequireProperty(value, propertyName);
            if (token.Type != JTokenType.Boolean || token is not JValue { Value: bool boolValue })
            {
                throw Error($"{propertyName} must be a boolean.", token);
            }

            return boolValue;
        }

        internal static bool ReadOptionalBool(JObject value, string propertyName, bool fallback) =>
            value.Property(propertyName, StringComparison.Ordinal) != null ? RequireBool(value, propertyName) : fallback;

        // BigNum literals must always be JSON strings — a JSON number (a detour through double) is an error.
        internal static BigNum RequireBigNum(JObject value, string propertyName) =>
            RequireBigNumToken(RequireProperty(value, propertyName));

        internal static BigNum RequireBigNumToken(JToken token)
        {
            if (token.Type != JTokenType.String)
            {
                throw Error("A BigNum value must be a string.", token);
            }

            var text = token.Value<string>()!;
            if (!BigNum.TryParse(text, out var parsed))
            {
                throw Error($"Cannot parse as BigNum: {text}", token);
            }

            return parsed;
        }

        internal static BigNum ReadOptionalBigNum(JObject value, string propertyName, BigNum fallback) =>
            value.Property(propertyName, StringComparison.Ordinal) != null
                ? RequireBigNum(value, propertyName)
                : fallback;

        internal static T RequireEnum<T>(JObject value, string propertyName) where T : struct, Enum
        {
            var token = RequireProperty(value, propertyName);
            var text = RequireStringValue(token, propertyName);
            if (!Enum.TryParse<T>(text, false, out var parsed) || !Enum.IsDefined(typeof(T), parsed))
            {
                throw Error($"Value not allowed: {propertyName} = {text}", token);
            }

            return parsed;
        }

        internal static JObject AsObject(JToken token, string message) => token as JObject ?? throw Error(message, token);

        internal static JArray AsArray(JToken token, string message) => token as JArray ?? throw Error(message, token);

        internal static TagCatalogException Error(string message, JToken token)
        {
            var lineInfo = (IJsonLineInfo)token;
            return new TagCatalogException(
                message, token.Path,
                lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
                lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1);
        }
    }
}
