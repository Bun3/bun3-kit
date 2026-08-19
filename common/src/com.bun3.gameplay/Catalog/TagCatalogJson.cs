#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>
    /// Legacy JSON catalog loader for authoring-tool compatibility. The runtime loading path is
    /// <see cref="TagCatalogBinary"/>; this format is read only by authoring tools.
    /// </summary>
    public static class TagCatalogJson
    {
        /// <summary>Reads the UTF-8 JSON stream from its current position to the end into an immutable catalog.</summary>
        /// <param name="utf8Json">Readable UTF-8 JSON stream.</param>
        /// <returns>Validated and indexed catalog.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="utf8Json"/> is null.</exception>
        /// <exception cref="ArgumentException">The stream is not readable.</exception>
        /// <exception cref="TagCatalogException">The JSON or catalog is invalid.</exception>
        public static TagCatalog Load(Stream utf8Json)
        {
            if (utf8Json is null) throw new ArgumentNullException(nameof(utf8Json));
            if (!utf8Json.CanRead) throw new ArgumentException("A readable stream is required.", nameof(utf8Json));
            return Loader.Load(utf8Json);
        }

        private static class Loader
        {
            internal static TagCatalog Load(Stream utf8Json)
            {
                string text;
                try
                {
                    using var streamReader = new StreamReader(
                        utf8Json,
                        new UTF8Encoding(false, true),
                        false,
                        1024,
                        true);
                    text = streamReader.ReadToEnd();
                }
                catch (DecoderFallbackException exception)
                {
                    throw new TagCatalogException(exception.Message, string.Empty, 1, 1);
                }

                StrictJsonSyntax.Validate(text);

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
                    if (reader.Read())
                    {
                        throw Error("Unexpected token after the root JSON value.", root);
                    }

                    return ReadRoot(root);
                }
                catch (TagCatalogException)
                {
                    throw;
                }
                catch (JsonReaderException exception)
                {
                    throw new TagCatalogException(
                        exception.Message,
                        string.Empty,
                        Math.Max(1, exception.LineNumber),
                        Math.Max(1, exception.LinePosition));
                }
            }

            private static TagCatalog ReadRoot(JObject root)
            {
                RequireAllowedProperties(root, "schemaVersion", "tags", "redirects");
                var schemaVersion = RequireProperty(root, "schemaVersion");
                if (schemaVersion.Type != JTokenType.Integer
                    || schemaVersion is not JValue { Value: long schemaVersionValue }
                    || schemaVersionValue != 1)
                {
                    throw Error("schemaVersion must be the integer 1.", schemaVersion);
                }

                var tags = RequireProperty(root, "tags") as JArray;
                if (tags is null)
                {
                    throw Error("tags must be an array.", RequireProperty(root, "tags"));
                }

                var explicitTags = ReadTags(tags);
                var redirectDefinitions = ReadRedirects(root.Property("redirects", StringComparison.Ordinal)?.Value);
                return TagCatalog.Create(explicitTags, redirectDefinitions);
            }

            private static List<string> ReadTags(JArray tags)
            {
                var explicitTags = new List<string>(tags.Count);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var token in tags)
                {
                    if (token is not JObject tag)
                    {
                        throw Error("tags items must be objects.", token);
                    }

                    RequireAllowedProperties(tag, "name", "comment");
                    var name = RequireString(tag, "name");
                    var canonical = TagName.ValidateAndFold(
                        name.Value,
                        name.Path,
                        name.LineNumber,
                        name.LinePosition);
                    if (!seen.Add(canonical))
                    {
                        throw new TagCatalogException(
                            "Duplicate tag name ignoring case.",
                            name.Path,
                            name.LineNumber,
                            name.LinePosition);
                    }

                    var comment = tag.Property("comment", StringComparison.Ordinal)?.Value;
                    if (comment is not null && comment.Type != JTokenType.String)
                    {
                        throw Error("comment must be a string.", comment);
                    }

                    explicitTags.Add(canonical);
                }

                return explicitTags;
            }

            private static List<TagCatalog.RedirectDefinition> ReadRedirects(JToken? redirects)
            {
                if (redirects is null) return new List<TagCatalog.RedirectDefinition>();
                if (redirects is not JArray redirectArray)
                {
                    throw Error("redirects must be an array.", redirects);
                }

                var definitions = new List<TagCatalog.RedirectDefinition>(redirectArray.Count);
                foreach (var token in redirectArray)
                {
                    if (token is not JObject redirect)
                    {
                        throw Error("redirects items must be objects.", token);
                    }

                    RequireAllowedProperties(redirect, "from", "to");
                    var from = RequireString(redirect, "from");
                    var to = RequireString(redirect, "to");
                    var canonicalFrom = TagName.ValidateAndFold(from.Value, from.Path, from.LineNumber, from.LinePosition);
                    var canonicalTo = TagName.ValidateAndFold(to.Value, to.Path, to.LineNumber, to.LinePosition);
                    definitions.Add(new TagCatalog.RedirectDefinition(
                        canonicalFrom,
                        canonicalTo,
                        from.Path,
                        from.LineNumber,
                        from.LinePosition,
                        to.Path,
                        to.LineNumber,
                        to.LinePosition));
                }

                return definitions;
            }

            private static LocatedString RequireString(JObject value, string propertyName)
            {
                var token = RequireProperty(value, propertyName);
                if (token.Type != JTokenType.String)
                {
                    throw Error($"{propertyName} must be a string.", token);
                }

                var lineInfo = (IJsonLineInfo)token;
                return new LocatedString(
                    token.Value<string>()!,
                    token.Path,
                    lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
                    lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1);
            }

            private static JToken RequireProperty(JObject value, string propertyName)
            {
                var property = value.Property(propertyName, StringComparison.Ordinal);
                if (property is null)
                {
                    throw Error($"Missing required field {propertyName}.", value);
                }

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

                    if (!permitted)
                    {
                        throw Error($"Field not allowed: {property.Name}", property);
                    }
                }
            }

            private static TagCatalogException Error(string message, JToken token)
            {
                var lineInfo = (IJsonLineInfo)token;
                return new TagCatalogException(
                    message,
                    token.Path,
                    lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
                    lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1);
            }

            private readonly struct LocatedString
            {
                internal LocatedString(string value, string path, int lineNumber, int linePosition)
                {
                    Value = value;
                    Path = path;
                    LineNumber = lineNumber;
                    LinePosition = linePosition;
                }

                internal string Value { get; }
                internal string Path { get; }
                internal int LineNumber { get; }
                internal int LinePosition { get; }
            }
        }

        internal static class StrictJsonSyntax
        {
            internal static void Validate(string text)
            {
                var parser = new Parser(text);
                parser.ParseValue();
                parser.SkipWhitespace();
                if (!parser.End)
                {
                    parser.Fail("Unexpected character after the root JSON value.");
                }
            }

            private sealed class Parser
            {
                private readonly string _text;
                private int _cursor;
                private int _line = 1;
                private int _linePosition = 1;
                private int _depth;
                private bool _previousWasCarriageReturn;

                internal Parser(string text) => _text = text;

                internal bool End => _cursor == _text.Length;

                internal void ParseValue()
                {
                    SkipWhitespace();
                    if (End) Fail("A JSON value is required.");
                    switch (Peek())
                    {
                        case '{': ParseObject(); return;
                        case '[': ParseArray(); return;
                        case '"': ParseString(); return;
                        case 't': ConsumeLiteral("true"); return;
                        case 'f': ConsumeLiteral("false"); return;
                        case 'n': ConsumeLiteral("null"); return;
                        default:
                            if (Peek() == '-' || IsDigit(Peek()))
                            {
                                ParseNumber();
                                return;
                            }

                            Fail("JSON value not allowed.");
                            return;
                    }
                }

                internal void ParseObject()
                {
                    EnterContainer();
                    try
                    {
                        Read();
                        SkipWhitespace();
                        if (TryConsume('}')) return;
                        while (true)
                        {
                            if (End || Peek() != '"') Fail("Object property names must be double-quoted strings.");
                            ParseString();
                            SkipWhitespace();
                            Expect(':');
                            ParseValue();
                            SkipWhitespace();
                            if (TryConsume('}')) return;
                            Expect(',');
                            SkipWhitespace();
                            if (End || Peek() != '"') Fail("An object property name is required after a comma.");
                        }
                    }
                    finally
                    {
                        _depth--;
                    }
                }

                internal void ParseArray()
                {
                    EnterContainer();
                    try
                    {
                        Read();
                        SkipWhitespace();
                        if (TryConsume(']')) return;
                        while (true)
                        {
                            ParseValue();
                            SkipWhitespace();
                            if (TryConsume(']')) return;
                            Expect(',');
                            SkipWhitespace();
                            if (End || Peek() == ']') Fail("An array value is required after a comma.");
                        }
                    }
                    finally
                    {
                        _depth--;
                    }
                }

                internal void ParseString()
                {
                    Expect('"');
                    while (!End)
                    {
                        var current = Read();
                        if (current == '"') return;
                        if (current < 0x20) Fail("Control characters must be escaped in JSON strings.");
                        if (current != '\\') continue;

                        if (End) Fail("A complete JSON escape is required.");
                        var escape = Read();
                        if (escape == 'u')
                        {
                            for (var i = 0; i < 4; i++)
                            {
                                if (End || !IsHex(Read())) Fail("A \\u escape requires four hex digits.");
                            }
                        }
                        else if (escape != '"' && escape != '\\' && escape != '/' && escape != 'b'
                            && escape != 'f' && escape != 'n' && escape != 'r' && escape != 't')
                        {
                            Fail("JSON escape not allowed.");
                        }
                    }

                    Fail("Unterminated JSON string.");
                }

                internal void ParseNumber()
                {
                    if (Peek() == '-') Read();
                    if (End) Fail("A number requires a digit part.");
                    if (Peek() == '0')
                    {
                        Read();
                        if (!End && IsDigit(Peek())) Fail("Leading zeros are not allowed.");
                    }
                    else
                    {
                        if (!IsDigitOneToNine(Peek())) Fail("A number requires 0 or 1-9.");
                        do { Read(); } while (!End && IsDigit(Peek()));
                    }

                    if (!End && Peek() == '.')
                    {
                        Read();
                        if (End || !IsDigit(Peek())) Fail("A digit is required after the decimal point.");
                        do { Read(); } while (!End && IsDigit(Peek()));
                    }

                    if (!End && (Peek() == 'e' || Peek() == 'E'))
                    {
                        Read();
                        if (!End && (Peek() == '+' || Peek() == '-')) Read();
                        if (End || !IsDigit(Peek())) Fail("A digit is required after the exponent.");
                        do { Read(); } while (!End && IsDigit(Peek()));
                    }
                }

                internal void ConsumeLiteral(string literal)
                {
                    for (var i = 0; i < literal.Length; i++)
                    {
                        if (End || Read() != literal[i]) Fail("JSON literal not allowed.");
                    }
                }

                internal void SkipWhitespace()
                {
                    while (!End && (Peek() == ' ' || Peek() == '\t' || Peek() == '\r' || Peek() == '\n')) Read();
                }

                internal void Fail(string message) => throw new TagCatalogException(message, string.Empty, _line, _linePosition);

                private void EnterContainer()
                {
                    if (_depth >= 8) Fail("JSON nesting depth cannot exceed 8.");
                    _depth++;
                }

                private char Peek() => _text[_cursor];

                private char Read()
                {
                    var value = _text[_cursor++];
                    if (value == '\r')
                    {
                        _line++;
                        _linePosition = 1;
                        _previousWasCarriageReturn = true;
                    }
                    else if (value == '\n')
                    {
                        if (!_previousWasCarriageReturn)
                        {
                            _line++;
                            _linePosition = 1;
                        }

                        _previousWasCarriageReturn = false;
                    }
                    else
                    {
                        _linePosition++;
                        _previousWasCarriageReturn = false;
                    }

                    return value;
                }

                private void Expect(char expected)
                {
                    if (End || Read() != expected) Fail($"'{expected}' is required.");
                }

                private bool TryConsume(char expected)
                {
                    if (End || Peek() != expected) return false;
                    Read();
                    return true;
                }

                private static bool IsDigit(char value) => value >= '0' && value <= '9';
                private static bool IsDigitOneToNine(char value) => value >= '1' && value <= '9';
                private static bool IsHex(char value) =>
                    (value >= '0' && value <= '9') || (value >= 'a' && value <= 'f') || (value >= 'A' && value <= 'F');
            }
        }
    }
}
