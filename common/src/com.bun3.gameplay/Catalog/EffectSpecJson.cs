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
    /// Authoring JSON loader for effect specs. Strictly validates syntax and shape (rejects
    /// duplicate keys, unknown fields, unknown enum values; BigNum literals are always strings)
    /// but performs no semantic validation of tag or seam references — that is
    /// <see cref="EffectCatalogBuilder.Build"/>'s job. Its only semantic step is resolving
    /// attribute name strings to ushort ids via <see cref="Load"/>'s attributeNames dictionary.
    /// </summary>
    public static class EffectSpecJson
    {
        /// <summary>Reads the UTF-8 JSON stream from its current position to the end into a list of effect specs.</summary>
        /// <param name="utf8Json">Readable UTF-8 JSON stream.</param>
        /// <param name="attributeNames">Dictionary resolving attribute names in Operand/ModifierDef to ids.</param>
        /// <returns>Effect specs in the order they appear in the JSON.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="utf8Json"/> or <paramref name="attributeNames"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">The stream is not readable.</exception>
        /// <exception cref="TagCatalogException">The JSON syntax or spec shape is invalid.</exception>
        public static List<EffectSpec> Load(Stream utf8Json, IReadOnlyDictionary<string, ushort> attributeNames)
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
                result.Add(ReadSpec(AsObject(item, "specs items must be objects."), attributeNames));
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

        private static EffectSpec ReadSpec(JObject spec, IReadOnlyDictionary<string, ushort> attributeNames)
        {
            RequireAllowedProperties(
                spec, "name", "maxLevel", "duration", "stack", "modifiers", "executions",
                "applicationConditions", "ongoingConditions", "grantedTags", "assetTags",
                "immunityTags", "chains", "removeOnApplyTags", "chanceToApply",
                "durationPerLevel", "durationScale", "drCategory", "drWindowTicks", "drStageMultipliers");

            var name = RequireString(spec, "name");
            var maxLevel = ReadOptionalInt(spec, "maxLevel", 0);
            var (durationType, durationTicks, periodTicks) = ReadDuration(RequireObject(spec, "duration"));
            var stack = spec.Property("stack", StringComparison.Ordinal) is { } stackProperty
                ? ReadStack(AsObject(stackProperty.Value, "stack must be an object."))
                : new StackPolicy();

            var modifiers = new List<ModifierDef>();
            foreach (var item in RequireArray(spec, "modifiers"))
            {
                modifiers.Add(ReadModifier(AsObject(item, "modifiers items must be objects."), attributeNames));
            }

            var executions = new List<ExecutionDef>();
            foreach (var item in RequireArray(spec, "executions"))
            {
                executions.Add(ReadExecution(AsObject(item, "executions items must be objects."), attributeNames));
            }

            var applicationConditions = new List<ConditionDef>();
            foreach (var item in RequireArray(spec, "applicationConditions"))
            {
                applicationConditions.Add(
                    ReadCondition(AsObject(item, "applicationConditions items must be objects."), attributeNames));
            }

            var ongoingConditions = new List<ConditionDef>();
            foreach (var item in RequireArray(spec, "ongoingConditions"))
            {
                ongoingConditions.Add(
                    ReadCondition(AsObject(item, "ongoingConditions items must be objects."), attributeNames));
            }

            var grantedTags = ReadStringArray(RequireArray(spec, "grantedTags"));
            var assetTags = ReadStringArray(RequireArray(spec, "assetTags"));
            var immunityTags = ReadStringArray(RequireArray(spec, "immunityTags"));

            var chains = new List<ChainEdgeDef>();
            foreach (var item in RequireArray(spec, "chains"))
            {
                chains.Add(ReadChain(AsObject(item, "chains items must be objects."), attributeNames));
            }

            var removeOnApplyTags = spec.Property("removeOnApplyTags", StringComparison.Ordinal) != null
                ? ReadStringArray(RequireArray(spec, "removeOnApplyTags"))
                : new List<string>();

            MagnitudeDef? chanceToApply = null;
            if (spec.Property("chanceToApply", StringComparison.Ordinal) is { } chanceToApplyProperty)
            {
                chanceToApply = ReadMagnitude(
                    AsObject(chanceToApplyProperty.Value, "chanceToApply must be an object."), attributeNames);
            }

            List<BigNum>? durationPerLevel = null;
            if (spec.Property("durationPerLevel", StringComparison.Ordinal) != null)
            {
                durationPerLevel = new List<BigNum>();
                foreach (var item in RequireArray(spec, "durationPerLevel"))
                {
                    durationPerLevel.Add(RequireBigNumToken(item));
                }
            }

            MagnitudeDef? durationScale = null;
            if (spec.Property("durationScale", StringComparison.Ordinal) is { } durationScaleProperty)
            {
                durationScale = ReadMagnitude(
                    AsObject(durationScaleProperty.Value, "durationScale must be an object."), attributeNames);
            }

            var drCategory = spec.Property("drCategory", StringComparison.Ordinal) != null
                ? RequireString(spec, "drCategory")
                : null;
            var drWindowTicks = ReadOptionalInt(spec, "drWindowTicks", 0);

            var drStageMultipliers = new List<BigNum>();
            if (spec.Property("drStageMultipliers", StringComparison.Ordinal) != null)
            {
                foreach (var item in RequireArray(spec, "drStageMultipliers"))
                {
                    drStageMultipliers.Add(RequireBigNumToken(item));
                }
            }

            return new EffectSpec
            {
                Name = name,
                MaxLevel = maxLevel,
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
                RemoveOnApplyTags = removeOnApplyTags,
                ChanceToApply = chanceToApply,
                DurationPerLevel = durationPerLevel,
                DurationScale = durationScale,
                DrCategory = drCategory,
                DrWindowTicks = drWindowTicks,
                DrStageMultipliers = drStageMultipliers,
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
                "resetPeriodOnReapply", "onExpiration", "onOverflow", "overflowEffect", "clearStacksOnOverflow",
                "levelFromStack", "extendCapMultiplier");

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

            if (stack.Property("levelFromStack", StringComparison.Ordinal) != null)
            {
                result.LevelFromStack = RequireBool(stack, "levelFromStack");
            }

            if (stack.Property("extendCapMultiplier", StringComparison.Ordinal) != null)
            {
                result.ExtendCapMultiplier = RequireBigNum(stack, "extendCapMultiplier");
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

        // magnitude JSON is discriminated into one of three shapes: calc | base(+perLevel?) | bare operand.
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
                    perLevel = ReadOperand(AsObject(perLevelProperty.Value, "perLevel must be an object."), attributeNames);
                }

                return new MagnitudeDef { Base = baseOperand, PerLevel = perLevel };
            }

            if (magnitude.Property("perLevelValues", StringComparison.Ordinal) != null)
            {
                RequireAllowedProperties(magnitude, "perLevelValues", "tail", "extrapolateIncrement");
                var values = new List<BigNum>();
                foreach (var item in RequireArray(magnitude, "perLevelValues"))
                {
                    values.Add(RequireBigNumToken(item));
                }

                var (tail, increment) = ReadLevelTailOptions(magnitude);
                return new MagnitudeDef { PerLevelValues = values, Tail = tail, ExtrapolateIncrement = increment };
            }

            if (magnitude.Property("formula", StringComparison.Ordinal) != null)
            {
                RequireAllowedProperties(magnitude, "formula", "tail", "extrapolateIncrement");
                var formula = RequireString(magnitude, "formula");
                var (tail, increment) = ReadLevelTailOptions(magnitude);
                return new MagnitudeDef { Formula = formula, Tail = tail, ExtrapolateIncrement = increment };
            }

            if (magnitude.Property("curveKeys", StringComparison.Ordinal) != null)
            {
                RequireAllowedProperties(magnitude, "curveKeys", "tail", "extrapolateIncrement");
                var keys = new List<LevelKey>();
                foreach (var item in RequireArray(magnitude, "curveKeys"))
                {
                    var keyObject = AsObject(item, "curveKeys items must be objects.");
                    RequireAllowedProperties(keyObject, "level", "value");
                    keys.Add(new LevelKey
                    {
                        Level = RequireInt(keyObject, "level"), Value = RequireBigNum(keyObject, "value"),
                    });
                }

                var (curveTail, curveIncrement) = ReadLevelTailOptions(magnitude);
                return new MagnitudeDef { CurveKeys = keys, Tail = curveTail, ExtrapolateIncrement = curveIncrement };
            }

            return new MagnitudeDef { Base = ReadOperand(magnitude, attributeNames) };
        }

        // "tail" and "extrapolateIncrement" are optional fields shared by the level-table shapes.
        private static (LevelTail Tail, BigNum Increment) ReadLevelTailOptions(JObject magnitude)
        {
            var tail = magnitude.Property("tail", StringComparison.Ordinal) != null
                ? RequireEnum<LevelTail>(magnitude, "tail")
                : LevelTail.Clamp;
            var increment = ReadOptionalBigNum(magnitude, "extrapolateIncrement", BigNum.Zero);
            return (tail, increment);
        }

        private static ExecutionDef ReadExecution(JObject execution, IReadOnlyDictionary<string, ushort> attributeNames)
        {
            RequireAllowedProperties(execution, "calc", "inputs");
            var calcTag = RequireString(execution, "calc");
            var inputs = new List<Operand>();
            foreach (var item in RequireArray(execution, "inputs"))
            {
                inputs.Add(ReadOperand(AsObject(item, "inputs items must be objects."), attributeNames));
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
                foreach (var item in AsArray(selectorParamsProperty.Value, "selectorParams must be an array."))
                {
                    selectorParams.Add(RequireBigNumToken(item));
                }
            }

            var conditions = new List<ConditionDef>();
            if (chain.Property("conditions", StringComparison.Ordinal) is { } conditionsProperty)
            {
                foreach (var item in AsArray(conditionsProperty.Value, "conditions must be an array."))
                {
                    conditions.Add(ReadCondition(AsObject(item, "conditions items must be objects."), attributeNames));
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

        // level must be either the string "Inherit" or a JSON integer (fixed level).
        private static (ChainLevelRule Rule, int Fixed) ReadChainLevel(JToken token)
        {
            if (token.Type == JTokenType.String)
            {
                if (token.Value<string>() == "Inherit")
                {
                    return (ChainLevelRule.Inherit, 0);
                }

                throw Error("The only allowed level string is \"Inherit\".", token);
            }

            if (token.Type == JTokenType.Integer && token is JValue { Value: long fixedValue }
                && fixedValue >= int.MinValue && fixedValue <= int.MaxValue)
            {
                return (ChainLevelRule.Fixed, (int)fixedValue);
            }

            throw Error("level must be \"Inherit\" or an integer.", token);
        }

        // Operand discrimination: exactly one of constant | attribute(+coefficient?) | sourceAttribute(+coefficient?).
        private static Operand ReadOperand(JObject operand, IReadOnlyDictionary<string, ushort> attributeNames)
        {
            var hasConstant = operand.Property("constant", StringComparison.Ordinal) != null;
            var hasAttribute = operand.Property("attribute", StringComparison.Ordinal) != null;
            var hasSourceAttribute = operand.Property("sourceAttribute", StringComparison.Ordinal) != null;
            var discriminatorCount = (hasConstant ? 1 : 0) + (hasAttribute ? 1 : 0) + (hasSourceAttribute ? 1 : 0);
            if (discriminatorCount != 1)
            {
                throw Error("An operand must have exactly one of constant, attribute, or sourceAttribute.", operand);
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
                throw Error($"Unknown attribute name: {name}", token);
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

        // ---- Primitive field readers (same pattern as TagCatalogJson/TagSourceJson) ----

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

        private static JObject RequireObject(JObject value, string propertyName) =>
            AsObject(RequireProperty(value, propertyName), $"{propertyName} must be an object.");

        private static JArray RequireArray(JObject value, string propertyName) =>
            AsArray(RequireProperty(value, propertyName), $"{propertyName} must be an array.");

        private static string RequireString(JObject value, string propertyName) =>
            RequireStringValue(RequireProperty(value, propertyName), propertyName);

        private static string RequireStringValue(JToken token, string propertyName)
        {
            if (token.Type != JTokenType.String)
            {
                throw Error($"{propertyName} must be a string.", token);
            }

            return token.Value<string>()!;
        }

        private static int RequireInt(JObject value, string propertyName)
        {
            var token = RequireProperty(value, propertyName);
            if (token.Type != JTokenType.Integer || token is not JValue { Value: long longValue }
                || longValue < int.MinValue || longValue > int.MaxValue)
            {
                throw Error($"{propertyName} must be an integer.", token);
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
                throw Error($"{propertyName} must be a boolean.", token);
            }

            return boolValue;
        }

        private static bool ReadOptionalBool(JObject value, string propertyName, bool fallback) =>
            value.Property(propertyName, StringComparison.Ordinal) != null ? RequireBool(value, propertyName) : fallback;

        // BigNum literals must always be JSON strings — a JSON number (a detour through double) is an error.
        private static BigNum RequireBigNum(JObject value, string propertyName) =>
            RequireBigNumToken(RequireProperty(value, propertyName));

        private static BigNum RequireBigNumToken(JToken token)
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
                throw Error($"Value not allowed: {propertyName} = {text}", token);
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
