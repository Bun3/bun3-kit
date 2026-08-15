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
                var text = textReader.ReadToEnd();
                StrictJsonSyntax.Validate(text);
                using var stringReader = new StringReader(text);
                using var jsonReader = new JsonTextReader(stringReader)
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

        private static class StrictJsonSyntax
        {
            internal static void Validate(string text)
            {
                var parser = new Parser(text);
                parser.ParseValue();
                parser.SkipWhitespace();
                if (!parser.End) parser.Fail();
            }

            private sealed class Parser
            {
                private readonly string _text;
                private int _cursor;
                private int _depth;

                internal Parser(string text) => _text = text;

                internal bool End => _cursor == _text.Length;

                internal void ParseValue()
                {
                    SkipWhitespace();
                    if (End) Fail();
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

                            Fail();
                            return;
                    }
                }

                internal void SkipWhitespace()
                {
                    while (!End && (Peek() == ' ' || Peek() == '\t' || Peek() == '\r' || Peek() == '\n')) Read();
                }

                internal void Fail() => throw new InvalidDataException("Native metadata contains invalid JSON syntax.");

                private void ParseObject()
                {
                    EnterContainer();
                    try
                    {
                        Read();
                        SkipWhitespace();
                        if (TryConsume('}')) return;
                        while (true)
                        {
                            if (End || Peek() != '"') Fail();
                            ParseString();
                            SkipWhitespace();
                            Expect(':');
                            ParseValue();
                            SkipWhitespace();
                            if (TryConsume('}')) return;
                            Expect(',');
                            SkipWhitespace();
                            if (End || Peek() != '"') Fail();
                        }
                    }
                    finally
                    {
                        _depth--;
                    }
                }

                private void ParseArray()
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
                            if (End || Peek() == ']') Fail();
                        }
                    }
                    finally
                    {
                        _depth--;
                    }
                }

                private void ParseString()
                {
                    Expect('"');
                    while (!End)
                    {
                        var current = Read();
                        if (current == '"') return;
                        if (current < 0x20) Fail();
                        if (current != '\\') continue;
                        if (End) Fail();

                        var escape = Read();
                        if (escape == 'u')
                        {
                            for (var index = 0; index < 4; index++)
                            {
                                if (End || !IsHex(Read())) Fail();
                            }
                        }
                        else if (escape != '"' && escape != '\\' && escape != '/' && escape != 'b'
                            && escape != 'f' && escape != 'n' && escape != 'r' && escape != 't')
                        {
                            Fail();
                        }
                    }

                    Fail();
                }

                private void ParseNumber()
                {
                    if (Peek() == '-') Read();
                    if (End) Fail();
                    if (Peek() == '0')
                    {
                        Read();
                        if (!End && IsDigit(Peek())) Fail();
                    }
                    else
                    {
                        if (!IsDigitOneToNine(Peek())) Fail();
                        do { Read(); } while (!End && IsDigit(Peek()));
                    }

                    if (!End && Peek() == '.')
                    {
                        Read();
                        if (End || !IsDigit(Peek())) Fail();
                        do { Read(); } while (!End && IsDigit(Peek()));
                    }

                    if (!End && (Peek() == 'e' || Peek() == 'E'))
                    {
                        Read();
                        if (!End && (Peek() == '+' || Peek() == '-')) Read();
                        if (End || !IsDigit(Peek())) Fail();
                        do { Read(); } while (!End && IsDigit(Peek()));
                    }
                }

                private void ConsumeLiteral(string literal)
                {
                    for (var index = 0; index < literal.Length; index++)
                    {
                        if (End || Read() != literal[index]) Fail();
                    }
                }

                private void EnterContainer()
                {
                    if (_depth >= 8) Fail();
                    _depth++;
                }

                private char Peek() => _text[_cursor];

                private char Read() => _text[_cursor++];

                private void Expect(char expected)
                {
                    if (End || Read() != expected) Fail();
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
