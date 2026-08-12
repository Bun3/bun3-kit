#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bun3.Gameplay.Tags
{
    public sealed partial class TagCatalog
    {
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
                        throw Error("루트 JSON 값 뒤에 토큰이 있습니다.", root);
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
                if (schemaVersion.Type != JTokenType.Integer || schemaVersion.Value<long>() != 1)
                {
                    throw Error("schemaVersion은 정수 1이어야 합니다.", schemaVersion);
                }

                var tags = RequireProperty(root, "tags") as JArray;
                if (tags is null)
                {
                    throw Error("tags는 배열이어야 합니다.", RequireProperty(root, "tags"));
                }

                var explicitTags = ReadTags(tags);
                var redirects = root.Property("redirects", StringComparison.Ordinal)?.Value;
                if (redirects is not null)
                {
                    if (redirects is not JArray redirectArray)
                    {
                        throw Error("redirects는 배열이어야 합니다.", redirects);
                    }

                    ValidateRedirects(redirectArray);
                }

                return Build(explicitTags);
            }

            private static List<ExplicitTag> ReadTags(JArray tags)
            {
                var explicitTags = new List<ExplicitTag>(tags.Count);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var token in tags)
                {
                    if (token is not JObject tag)
                    {
                        throw Error("tags의 항목은 객체여야 합니다.", token);
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
                            "대소문자를 제외하고 중복된 태그 이름입니다.",
                            name.Path,
                            name.LineNumber,
                            name.LinePosition);
                    }

                    var comment = tag.Property("comment", StringComparison.Ordinal)?.Value;
                    if (comment is not null && comment.Type != JTokenType.String)
                    {
                        throw Error("comment는 문자열이어야 합니다.", comment);
                    }

                    explicitTags.Add(new ExplicitTag(canonical, name.Value));
                }

                return explicitTags;
            }

            private static void ValidateRedirects(JArray redirects)
            {
                foreach (var token in redirects)
                {
                    if (token is not JObject redirect)
                    {
                        throw Error("redirects의 항목은 객체여야 합니다.", token);
                    }

                    RequireAllowedProperties(redirect, "from", "to");
                    var from = RequireString(redirect, "from");
                    var to = RequireString(redirect, "to");
                    TagName.ValidateAndFold(from.Value, from.Path, from.LineNumber, from.LinePosition);
                    TagName.ValidateAndFold(to.Value, to.Path, to.LineNumber, to.LinePosition);
                }
            }

            private static LocatedString RequireString(JObject value, string propertyName)
            {
                var token = RequireProperty(value, propertyName);
                if (token.Type != JTokenType.String)
                {
                    throw Error($"{propertyName}은 문자열이어야 합니다.", token);
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
                    throw Error($"필수 필드 {propertyName}이(가) 없습니다.", value);
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
                        throw Error($"허용되지 않은 필드입니다: {property.Name}", property);
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

        private static class StrictJsonSyntax
        {
            internal static void Validate(string text)
            {
                var parser = new Parser(text);
                parser.ParseValue();
                parser.SkipWhitespace();
                if (!parser.End)
                {
                    parser.Fail("루트 JSON 값 뒤에 허용되지 않은 문자가 있습니다.");
                }
            }

            private sealed class Parser
            {
                private readonly string _text;
                private int _cursor;
                private int _line = 1;
                private int _linePosition = 1;

                internal Parser(string text) => _text = text;

                internal bool End => _cursor == _text.Length;

                internal void ParseValue()
                {
                    SkipWhitespace();
                    if (End) Fail("JSON 값이 필요합니다.");
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

                            Fail("허용되지 않은 JSON 값입니다.");
                            return;
                    }
                }

                internal void ParseObject()
                {
                    Read();
                    SkipWhitespace();
                    if (TryConsume('}')) return;
                    while (true)
                    {
                        if (End || Peek() != '"') Fail("객체 속성 이름은 큰따옴표 문자열이어야 합니다.");
                        ParseString();
                        SkipWhitespace();
                        Expect(':');
                        ParseValue();
                        SkipWhitespace();
                        if (TryConsume('}')) return;
                        Expect(',');
                        SkipWhitespace();
                        if (End || Peek() != '"') Fail("쉼표 뒤에는 객체 속성 이름이 필요합니다.");
                    }
                }

                internal void ParseArray()
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
                        if (End || Peek() == ']') Fail("쉼표 뒤에는 배열 값이 필요합니다.");
                    }
                }

                internal void ParseString()
                {
                    Expect('"');
                    while (!End)
                    {
                        var current = Read();
                        if (current == '"') return;
                        if (current < 0x20) Fail("제어 문자는 JSON 문자열에 이스케이프되어야 합니다.");
                        if (current != '\\') continue;

                        if (End) Fail("완전한 JSON 이스케이프가 필요합니다.");
                        var escape = Read();
                        if (escape == 'u')
                        {
                            for (var i = 0; i < 4; i++)
                            {
                                if (End || !IsHex(Read())) Fail("\\u 이스케이프에는 네 개의 16진수가 필요합니다.");
                            }
                        }
                        else if (escape != '"' && escape != '\\' && escape != '/' && escape != 'b'
                            && escape != 'f' && escape != 'n' && escape != 'r' && escape != 't')
                        {
                            Fail("허용되지 않은 JSON 이스케이프입니다.");
                        }
                    }

                    Fail("닫히지 않은 JSON 문자열입니다.");
                }

                internal void ParseNumber()
                {
                    if (Peek() == '-') Read();
                    if (End) Fail("숫자에는 숫자 부분이 필요합니다.");
                    if (Peek() == '0')
                    {
                        Read();
                        if (!End && IsDigit(Peek())) Fail("선행 0은 허용되지 않습니다.");
                    }
                    else
                    {
                        if (!IsDigitOneToNine(Peek())) Fail("숫자에는 0 또는 1-9가 필요합니다.");
                        do { Read(); } while (!End && IsDigit(Peek()));
                    }

                    if (!End && Peek() == '.')
                    {
                        Read();
                        if (End || !IsDigit(Peek())) Fail("소수점 뒤에는 숫자가 필요합니다.");
                        do { Read(); } while (!End && IsDigit(Peek()));
                    }

                    if (!End && (Peek() == 'e' || Peek() == 'E'))
                    {
                        Read();
                        if (!End && (Peek() == '+' || Peek() == '-')) Read();
                        if (End || !IsDigit(Peek())) Fail("지수 뒤에는 숫자가 필요합니다.");
                        do { Read(); } while (!End && IsDigit(Peek()));
                    }
                }

                internal void ConsumeLiteral(string literal)
                {
                    for (var i = 0; i < literal.Length; i++)
                    {
                        if (End || Read() != literal[i]) Fail("허용되지 않은 JSON 리터럴입니다.");
                    }
                }

                internal void SkipWhitespace()
                {
                    while (!End && (Peek() == ' ' || Peek() == '\t' || Peek() == '\r' || Peek() == '\n')) Read();
                }

                internal void Fail(string message) => throw new TagCatalogException(message, string.Empty, _line, _linePosition);

                private char Peek() => _text[_cursor];

                private char Read()
                {
                    var value = _text[_cursor++];
                    if (value == '\n')
                    {
                        _line++;
                        _linePosition = 1;
                    }
                    else
                    {
                        _linePosition++;
                    }

                    return value;
                }

                private void Expect(char expected)
                {
                    if (End || Read() != expected) Fail($"'{expected}'가 필요합니다.");
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
