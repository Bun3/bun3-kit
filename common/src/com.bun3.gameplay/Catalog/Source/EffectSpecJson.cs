#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bun3.Gameplay.Effects.Catalog
{
    /// <summary>
    /// 효과 스펙 저작 JSON 로더입니다. 문법·형태는 엄격히 검증하지만(중복 키·미지 필드·미지 열거형
    /// 거부, BigNum 리터럴은 항상 문자열) 태그·시섬 참조의 의미 검증은 하지 않습니다 — 그건
    /// <see cref="EffectCatalogBuilder.Build"/>의 몫입니다. 이 로더가 하는 유일한 의미 해석은
    /// 속성 "이름" 문자열을 <see cref="Load"/>의 attributeNames 사전으로 id(ushort)로 바꾸는 것뿐입니다.
    /// </summary>
    public static class EffectSpecJson
    {
        /// <summary>UTF-8 JSON 스트림의 현재 위치부터 끝까지 읽어 효과 스펙 목록을 만듭니다.</summary>
        /// <param name="utf8Json">읽을 수 있는 UTF-8 JSON 스트림입니다.</param>
        /// <param name="attributeNames">Operand·ModifierDef의 속성 이름을 id로 해석할 사전입니다.</param>
        /// <returns>JSON에 나온 순서 그대로의 효과 스펙 목록입니다.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="utf8Json"/> 또는 <paramref name="attributeNames"/>가 null인 경우입니다.
        /// </exception>
        /// <exception cref="ArgumentException">스트림을 읽을 수 없는 경우입니다.</exception>
        /// <exception cref="TagCatalogException">JSON 문법 또는 스펙 형식이 유효하지 않은 경우입니다.</exception>
        public static List<EffectSpec> Load(Stream utf8Json, IReadOnlyDictionary<string, ushort> attributeNames)
        {
            if (utf8Json is null) throw new ArgumentNullException(nameof(utf8Json));
            if (!utf8Json.CanRead) throw new ArgumentException("읽을 수 있는 스트림이 필요합니다.", nameof(utf8Json));
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
                    throw Error("루트 JSON 값 뒤에 토큰이 있습니다.", root);
                }

                return ReadRoot(root, attributeNames);
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

        private static List<EffectSpec> ReadRoot(JObject root, IReadOnlyDictionary<string, ushort> attributeNames)
        {
            RequireAllowedProperties(root, "schemaVersion", "specs");
            RequireSchemaVersion(root);

            var specsArray = RequireArray(root, "specs");
            var result = new List<EffectSpec>(specsArray.Count);
            foreach (var item in specsArray)
            {
                result.Add(ReadSpec(AsObject(item, "specs의 항목은 객체여야 합니다."), attributeNames));
            }

            return result;
        }

        private static void RequireSchemaVersion(JObject root)
        {
            var token = RequireProperty(root, "schemaVersion");
            if (token.Type != JTokenType.Integer || token is not JValue { Value: long value } || value != 1)
            {
                throw Error("schemaVersion은 정수 1이어야 합니다.", token);
            }
        }

        private static EffectSpec ReadSpec(JObject spec, IReadOnlyDictionary<string, ushort> attributeNames)
        {
            RequireAllowedProperties(
                spec, "name", "duration", "stack", "modifiers", "executions",
                "applicationConditions", "ongoingConditions", "grantedTags", "assetTags",
                "immunityTags", "chains");

            var name = RequireString(spec, "name");
            var (durationType, durationTicks, periodTicks) = ReadDuration(RequireObject(spec, "duration"));
            var stack = spec.Property("stack", StringComparison.Ordinal) is { } stackProperty
                ? ReadStack(AsObject(stackProperty.Value, "stack은 객체여야 합니다."))
                : new StackPolicy();

            var modifiers = new List<ModifierDef>();
            foreach (var item in RequireArray(spec, "modifiers"))
            {
                modifiers.Add(ReadModifier(AsObject(item, "modifiers의 항목은 객체여야 합니다."), attributeNames));
            }

            var executions = new List<ExecutionDef>();
            foreach (var item in RequireArray(spec, "executions"))
            {
                executions.Add(ReadExecution(AsObject(item, "executions의 항목은 객체여야 합니다."), attributeNames));
            }

            var applicationConditions = new List<ConditionDef>();
            foreach (var item in RequireArray(spec, "applicationConditions"))
            {
                applicationConditions.Add(
                    ReadCondition(AsObject(item, "applicationConditions의 항목은 객체여야 합니다."), attributeNames));
            }

            var ongoingConditions = new List<ConditionDef>();
            foreach (var item in RequireArray(spec, "ongoingConditions"))
            {
                ongoingConditions.Add(
                    ReadCondition(AsObject(item, "ongoingConditions의 항목은 객체여야 합니다."), attributeNames));
            }

            var grantedTags = ReadStringArray(RequireArray(spec, "grantedTags"));
            var assetTags = ReadStringArray(RequireArray(spec, "assetTags"));
            var immunityTags = ReadStringArray(RequireArray(spec, "immunityTags"));

            var chains = new List<ChainEdgeDef>();
            foreach (var item in RequireArray(spec, "chains"))
            {
                chains.Add(ReadChain(AsObject(item, "chains의 항목은 객체여야 합니다."), attributeNames));
            }

            return new EffectSpec
            {
                Name = name,
                DurationType = durationType,
                DurationTicks = durationTicks,
                PeriodTicks = periodTicks,
                Stack = stack,
                Modifiers = modifiers,
                Executions = executions,
                ApplicationConditions = applicationConditions,
                OngoingConditions = ongoingConditions,
                GrantedTags = grantedTags,
                AssetTags = assetTags,
                ImmunityTags = immunityTags,
                Chains = chains,
            };
        }

        private static (EffectDurationType Type, int Ticks, int PeriodTicks) ReadDuration(JObject duration)
        {
            RequireAllowedProperties(duration, "type", "ticks", "periodTicks");
            var type = RequireEnum<EffectDurationType>(duration, "type");
            var ticks = ReadOptionalInt(duration, "ticks", 0);
            var periodTicks = ReadOptionalInt(duration, "periodTicks", 0);
            return (type, ticks, periodTicks);
        }

        private static StackPolicy ReadStack(JObject stack)
        {
            RequireAllowedProperties(
                stack, "maxStack", "onReapply", "addStackCount", "refreshDurationOnReapply",
                "resetPeriodOnReapply", "onExpiration", "onOverflow", "overflowEffect", "clearStacksOnOverflow");

            var result = new StackPolicy
            {
                MaxStack = RequireInt(stack, "maxStack"),
                OnReapply = RequireEnum<StackReapply>(stack, "onReapply"),
                OnOverflow = RequireEnum<StackOverflow>(stack, "onOverflow"),
            };

            if (stack.Property("addStackCount", StringComparison.Ordinal) != null)
            {
                result.AddStackCount = RequireInt(stack, "addStackCount");
            }

            if (stack.Property("refreshDurationOnReapply", StringComparison.Ordinal) != null)
            {
                result.RefreshDurationOnReapply = RequireBool(stack, "refreshDurationOnReapply");
            }

            if (stack.Property("resetPeriodOnReapply", StringComparison.Ordinal) != null)
            {
                result.ResetPeriodOnReapply = RequireBool(stack, "resetPeriodOnReapply");
            }

            if (stack.Property("onExpiration", StringComparison.Ordinal) != null)
            {
                result.OnExpiration = RequireEnum<StackExpiration>(stack, "onExpiration");
            }

            if (stack.Property("overflowEffect", StringComparison.Ordinal) != null)
            {
                result.OverflowEffectName = RequireString(stack, "overflowEffect");
            }

            if (stack.Property("clearStacksOnOverflow", StringComparison.Ordinal) != null)
            {
                result.ClearStacksOnOverflow = RequireBool(stack, "clearStacksOnOverflow");
            }

            return result;
        }

        private static ModifierDef ReadModifier(JObject modifier, IReadOnlyDictionary<string, ushort> attributeNames)
        {
            RequireAllowedProperties(modifier, "attribute", "op", "magnitude", "scaleWithStack");
            var attributeId = ResolveAttributeId(modifier, "attribute", attributeNames);
            var op = RequireEnum<AttributeModifierOp>(modifier, "op");
            var magnitude = ReadMagnitude(RequireObject(modifier, "magnitude"), attributeNames);
            var scaleWithStack = ReadOptionalBool(modifier, "scaleWithStack", true);
            return new ModifierDef
            {
                AttributeId = attributeId, Op = op, Magnitude = magnitude, ScaleWithStack = scaleWithStack,
            };
        }

        // magnitude JSON은 판별 프로퍼티로 세 형태 중 하나: calc | base(+perLevel?) | 맨몸 Operand.
        private static MagnitudeDef ReadMagnitude(JObject magnitude, IReadOnlyDictionary<string, ushort> attributeNames)
        {
            if (magnitude.Property("calc", StringComparison.Ordinal) != null)
            {
                RequireAllowedProperties(magnitude, "calc");
                return new MagnitudeDef { CalcTag = RequireString(magnitude, "calc") };
            }

            if (magnitude.Property("base", StringComparison.Ordinal) != null)
            {
                RequireAllowedProperties(magnitude, "base", "perLevel");
                var baseOperand = ReadOperand(RequireObject(magnitude, "base"), attributeNames);
                Operand? perLevel = null;
                if (magnitude.Property("perLevel", StringComparison.Ordinal) is { } perLevelProperty)
                {
                    perLevel = ReadOperand(AsObject(perLevelProperty.Value, "perLevel은 객체여야 합니다."), attributeNames);
                }

                return new MagnitudeDef { Base = baseOperand, PerLevel = perLevel };
            }

            return new MagnitudeDef { Base = ReadOperand(magnitude, attributeNames) };
        }

        private static ExecutionDef ReadExecution(JObject execution, IReadOnlyDictionary<string, ushort> attributeNames)
        {
            RequireAllowedProperties(execution, "calc", "inputs");
            var calcTag = RequireString(execution, "calc");
            var inputs = new List<Operand>();
            foreach (var item in RequireArray(execution, "inputs"))
            {
                inputs.Add(ReadOperand(AsObject(item, "inputs의 항목은 객체여야 합니다."), attributeNames));
            }

            return new ExecutionDef { CalcTag = calcTag, Inputs = inputs };
        }

        private static ConditionDef ReadCondition(JObject condition, IReadOnlyDictionary<string, ushort> attributeNames)
        {
            RequireAllowedProperties(condition, "left", "op", "right");
            var left = ReadOperand(RequireObject(condition, "left"), attributeNames);
            var op = RequireEnum<ComparisonOp>(condition, "op");
            var right = ReadOperand(RequireObject(condition, "right"), attributeNames);
            return new ConditionDef { Left = left, Op = op, Right = right };
        }

        private static ChainEdgeDef ReadChain(JObject chain, IReadOnlyDictionary<string, ushort> attributeNames)
        {
            RequireAllowedProperties(chain, "trigger", "effect", "selector", "selectorParams", "conditions", "level");
            var trigger = RequireEnum<ChainTrigger>(chain, "trigger");
            var effectName = RequireString(chain, "effect");

            string? selectorTag = null;
            if (chain.Property("selector", StringComparison.Ordinal) != null)
            {
                selectorTag = RequireString(chain, "selector");
            }

            var selectorParams = new List<BigNum>();
            if (chain.Property("selectorParams", StringComparison.Ordinal) is { } selectorParamsProperty)
            {
                foreach (var item in AsArray(selectorParamsProperty.Value, "selectorParams는 배열이어야 합니다."))
                {
                    selectorParams.Add(RequireBigNumToken(item));
                }
            }

            var conditions = new List<ConditionDef>();
            if (chain.Property("conditions", StringComparison.Ordinal) is { } conditionsProperty)
            {
                foreach (var item in AsArray(conditionsProperty.Value, "conditions는 배열이어야 합니다."))
                {
                    conditions.Add(ReadCondition(AsObject(item, "conditions의 항목은 객체여야 합니다."), attributeNames));
                }
            }

            var levelRule = ChainLevelRule.Inherit;
            var fixedLevel = 0;
            if (chain.Property("level", StringComparison.Ordinal) is { } levelProperty)
            {
                (levelRule, fixedLevel) = ReadChainLevel(levelProperty.Value);
            }

            return new ChainEdgeDef
            {
                Trigger = trigger,
                EffectName = effectName,
                SelectorTag = selectorTag,
                SelectorParams = selectorParams,
                Conditions = conditions,
                LevelRule = levelRule,
                FixedLevel = fixedLevel,
            };
        }

        // level은 문자열 "Inherit" 또는 JSON 정수(고정 레벨) 중 하나여야 한다.
        private static (ChainLevelRule Rule, int Fixed) ReadChainLevel(JToken token)
        {
            if (token.Type == JTokenType.String)
            {
                if (token.Value<string>() == "Inherit")
                {
                    return (ChainLevelRule.Inherit, 0);
                }

                throw Error("level 문자열은 \"Inherit\"만 허용됩니다.", token);
            }

            if (token.Type == JTokenType.Integer && token is JValue { Value: long fixedValue }
                && fixedValue >= int.MinValue && fixedValue <= int.MaxValue)
            {
                return (ChainLevelRule.Fixed, (int)fixedValue);
            }

            throw Error("level은 \"Inherit\" 또는 정수여야 합니다.", token);
        }

        // Operand 판별: constant | attribute(+coefficient?) | sourceAttribute(+coefficient?) 중 정확히 하나.
        private static Operand ReadOperand(JObject operand, IReadOnlyDictionary<string, ushort> attributeNames)
        {
            var hasConstant = operand.Property("constant", StringComparison.Ordinal) != null;
            var hasAttribute = operand.Property("attribute", StringComparison.Ordinal) != null;
            var hasSourceAttribute = operand.Property("sourceAttribute", StringComparison.Ordinal) != null;
            var discriminatorCount = (hasConstant ? 1 : 0) + (hasAttribute ? 1 : 0) + (hasSourceAttribute ? 1 : 0);
            if (discriminatorCount != 1)
            {
                throw Error("피연산자는 constant, attribute, sourceAttribute 중 정확히 하나를 가져야 합니다.", operand);
            }

            if (hasConstant)
            {
                RequireAllowedProperties(operand, "constant");
                return Operand.Constant(RequireBigNum(operand, "constant"));
            }

            if (hasAttribute)
            {
                RequireAllowedProperties(operand, "attribute", "coefficient");
                var attributeId = ResolveAttributeId(operand, "attribute", attributeNames);
                var coefficient = ReadOptionalBigNum(operand, "coefficient", BigNum.One);
                return Operand.Attribute(attributeId, coefficient);
            }

            RequireAllowedProperties(operand, "sourceAttribute", "coefficient");
            var sourceAttributeId = ResolveAttributeId(operand, "sourceAttribute", attributeNames);
            var sourceCoefficient = ReadOptionalBigNum(operand, "coefficient", BigNum.One);
            return Operand.SourceAttribute(sourceAttributeId, sourceCoefficient);
        }

        private static ushort ResolveAttributeId(
            JObject value, string propertyName, IReadOnlyDictionary<string, ushort> attributeNames)
        {
            var token = RequireProperty(value, propertyName);
            var name = RequireStringValue(token, propertyName);
            if (!attributeNames.TryGetValue(name, out var id))
            {
                throw Error($"알 수 없는 속성 이름입니다: {name}", token);
            }

            return id;
        }

        private static List<string> ReadStringArray(JArray array)
        {
            var result = new List<string>(array.Count);
            foreach (var item in array)
            {
                result.Add(RequireStringValue(item, string.Empty));
            }

            return result;
        }

        // ---- 원시 필드 판독 유틸 (TagCatalogJson/TagSourceJson과 같은 패턴) ----

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

        private static JObject RequireObject(JObject value, string propertyName) =>
            AsObject(RequireProperty(value, propertyName), $"{propertyName}는 객체여야 합니다.");

        private static JArray RequireArray(JObject value, string propertyName) =>
            AsArray(RequireProperty(value, propertyName), $"{propertyName}는 배열이어야 합니다.");

        private static string RequireString(JObject value, string propertyName) =>
            RequireStringValue(RequireProperty(value, propertyName), propertyName);

        private static string RequireStringValue(JToken token, string propertyName)
        {
            if (token.Type != JTokenType.String)
            {
                throw Error($"{propertyName}은 문자열이어야 합니다.", token);
            }

            return token.Value<string>()!;
        }

        private static int RequireInt(JObject value, string propertyName)
        {
            var token = RequireProperty(value, propertyName);
            if (token.Type != JTokenType.Integer || token is not JValue { Value: long longValue }
                || longValue < int.MinValue || longValue > int.MaxValue)
            {
                throw Error($"{propertyName}은 정수여야 합니다.", token);
            }

            return (int)longValue;
        }

        private static int ReadOptionalInt(JObject value, string propertyName, int fallback) =>
            value.Property(propertyName, StringComparison.Ordinal) != null ? RequireInt(value, propertyName) : fallback;

        private static bool RequireBool(JObject value, string propertyName)
        {
            var token = RequireProperty(value, propertyName);
            if (token.Type != JTokenType.Boolean || token is not JValue { Value: bool boolValue })
            {
                throw Error($"{propertyName}은 불리언이어야 합니다.", token);
            }

            return boolValue;
        }

        private static bool ReadOptionalBool(JObject value, string propertyName, bool fallback) =>
            value.Property(propertyName, StringComparison.Ordinal) != null ? RequireBool(value, propertyName) : fallback;

        // BigNum 리터럴은 항상 JSON 문자열이어야 한다 — number로 오면(double 경유 우회) 오류.
        private static BigNum RequireBigNum(JObject value, string propertyName) =>
            RequireBigNumToken(RequireProperty(value, propertyName));

        private static BigNum RequireBigNumToken(JToken token)
        {
            if (token.Type != JTokenType.String)
            {
                throw Error("BigNum 값은 문자열이어야 합니다.", token);
            }

            var text = token.Value<string>()!;
            if (!BigNum.TryParse(text, out var parsed))
            {
                throw Error($"BigNum으로 파싱할 수 없습니다: {text}", token);
            }

            return parsed;
        }

        private static BigNum ReadOptionalBigNum(JObject value, string propertyName, BigNum fallback) =>
            value.Property(propertyName, StringComparison.Ordinal) != null
                ? RequireBigNum(value, propertyName)
                : fallback;

        private static T RequireEnum<T>(JObject value, string propertyName) where T : struct, Enum
        {
            var token = RequireProperty(value, propertyName);
            var text = RequireStringValue(token, propertyName);
            if (!Enum.TryParse<T>(text, false, out var parsed) || !Enum.IsDefined(typeof(T), parsed))
            {
                throw Error($"허용되지 않은 값입니다: {propertyName} = {text}", token);
            }

            return parsed;
        }

        private static JObject AsObject(JToken token, string message) => token as JObject ?? throw Error(message, token);

        private static JArray AsArray(JToken token, string message) => token as JArray ?? throw Error(message, token);

        private static TagCatalogException Error(string message, JToken token)
        {
            var lineInfo = (IJsonLineInfo)token;
            return new TagCatalogException(
                message, token.Path,
                lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
                lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1);
        }
    }
}
