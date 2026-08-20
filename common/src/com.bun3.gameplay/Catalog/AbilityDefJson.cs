#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Effects.Catalog;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bun3.Gameplay.Abilities.Catalog
{
    /// <summary>
    /// Authoring JSON loader for ability definitions. Strictly validates syntax and shape (rejects
    /// duplicate keys, unknown fields, unknown enum values; BigNum literals are always strings)
    /// but performs no semantic validation of tag, effect, or seam references — that is
    /// <see cref="AbilityCatalogBuilder.Build"/>'s job. Its only semantic step is resolving
    /// attribute name strings to ushort ids via <see cref="Load"/>'s attributeNames dictionary.
    /// Operand, MagnitudeDef, ConditionDef, and the "level" field (<c>"Inherit"</c> or a fixed
    /// integer, compiling to <see cref="ChainLevelRule"/> + FixedLevel) reuse <see cref="EffectDefJson"/>'s
    /// internal parsers so the two loaders share one implementation of each shape.
    /// </summary>
    public static class AbilityDefJson
    {
        /// <summary>Reads the UTF-8 JSON stream from its current position to the end into a list of ability defs.</summary>
        /// <param name="utf8Json">Readable UTF-8 JSON stream.</param>
        /// <param name="attributeNames">Dictionary resolving attribute names in costs/conditions to ids.</param>
        /// <returns>Ability defs in the order they appear in the JSON.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="utf8Json"/> or <paramref name="attributeNames"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">The stream is not readable.</exception>
        /// <exception cref="TagCatalogException">The JSON syntax or ability shape is invalid.</exception>
        public static List<AbilityDef> Load(Stream utf8Json, IReadOnlyDictionary<string, ushort> attributeNames)
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

        private static List<AbilityDef> ReadRoot(JObject root, IReadOnlyDictionary<string, ushort> attributeNames)
        {
            RequireAllowedProperties(root, "schemaVersion", "abilities");
            RequireSchemaVersion(root);

            var abilitiesArray = RequireArray(root, "abilities");
            var result = new List<AbilityDef>(abilitiesArray.Count);
            foreach (var item in abilitiesArray)
            {
                result.Add(ReadAbility(AsObject(item, "abilities items must be objects."), attributeNames));
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

        private static AbilityDef ReadAbility(JObject ability, IReadOnlyDictionary<string, ushort> attributeNames)
        {
            RequireAllowedProperties(
                ability, "name", "maxLevel", "assetTags", "requiredTags", "blockedTags",
                "activationConditions", "costs", "cooldownEffect", "effects");

            var name = RequireString(ability, "name");
            var maxLevel = ReadOptionalInt(ability, "maxLevel", 0);

            var assetTags = ability.Property("assetTags", StringComparison.Ordinal) != null
                ? EffectDefJson.ReadStringArray(RequireArray(ability, "assetTags"))
                : new List<string>();
            var requiredTags = ability.Property("requiredTags", StringComparison.Ordinal) != null
                ? EffectDefJson.ReadStringArray(RequireArray(ability, "requiredTags"))
                : new List<string>();
            var blockedTags = ability.Property("blockedTags", StringComparison.Ordinal) != null
                ? EffectDefJson.ReadStringArray(RequireArray(ability, "blockedTags"))
                : new List<string>();

            var activationConditions = new List<ConditionDef>();
            if (ability.Property("activationConditions", StringComparison.Ordinal) != null)
            {
                foreach (var item in RequireArray(ability, "activationConditions"))
                {
                    activationConditions.Add(EffectDefJson.ReadCondition(
                        AsObject(item, "activationConditions items must be objects."), attributeNames));
                }
            }

            var costs = new List<CostDef>();
            if (ability.Property("costs", StringComparison.Ordinal) != null)
            {
                foreach (var item in RequireArray(ability, "costs"))
                {
                    costs.Add(ReadCost(AsObject(item, "costs items must be objects."), attributeNames));
                }
            }

            var cooldownEffect = ability.Property("cooldownEffect", StringComparison.Ordinal) != null
                ? RequireString(ability, "cooldownEffect")
                : null;

            var effects = new List<AbilityEffectDef>();
            if (ability.Property("effects", StringComparison.Ordinal) != null)
            {
                foreach (var item in RequireArray(ability, "effects"))
                {
                    effects.Add(ReadAbilityEffect(AsObject(item, "effects items must be objects.")));
                }
            }

            return new AbilityDef
            {
                Name = name,
                MaxLevel = maxLevel,
                AssetTags = assetTags,
                RequiredTags = requiredTags,
                BlockedTags = blockedTags,
                ActivationConditions = activationConditions,
                Costs = costs,
                CooldownEffect = cooldownEffect,
                Effects = effects,
            };
        }

        private static CostDef ReadCost(JObject cost, IReadOnlyDictionary<string, ushort> attributeNames)
        {
            RequireAllowedProperties(cost, "attribute", "magnitude");
            var attributeId = EffectDefJson.ResolveAttributeId(cost, "attribute", attributeNames);
            var magnitude = EffectDefJson.ReadMagnitude(RequireObject(cost, "magnitude"), attributeNames);
            return new CostDef { AttributeId = attributeId, Magnitude = magnitude };
        }

        private static AbilityEffectDef ReadAbilityEffect(JObject entry)
        {
            RequireAllowedProperties(entry, "effect", "selector", "selectorParams", "level");

            var effectName = RequireString(entry, "effect");

            string? selectorTag = null;
            if (entry.Property("selector", StringComparison.Ordinal) != null)
            {
                selectorTag = RequireString(entry, "selector");
            }

            var selectorParams = entry.Property("selectorParams", StringComparison.Ordinal) is { } selectorParamsProperty
                ? EffectDefJson.ReadBigNumArray(AsArray(selectorParamsProperty.Value, "selectorParams must be an array."))
                : new List<BigNum>();

            var levelRule = ChainLevelRule.Inherit;
            var fixedLevel = 0;
            if (entry.Property("level", StringComparison.Ordinal) is { } levelProperty)
            {
                (levelRule, fixedLevel) = EffectDefJson.ReadChainLevel(levelProperty.Value);
            }

            return new AbilityEffectDef
            {
                EffectName = effectName,
                SelectorTag = selectorTag,
                SelectorParams = selectorParams,
                LevelRule = levelRule,
                FixedLevel = fixedLevel,
            };
        }

        // ---- Primitive field readers (same pattern as EffectDefJson/TagCatalogJson/TagSourceJson) ----

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

        private static JObject AsObject(JToken token, string message) => token as JObject ?? throw Error(message, token);

        private static JArray AsArray(JToken token, string message) => token as JArray ?? throw Error(message, token);

        private static TagCatalogException Error(string message, JToken token) => EffectDefJson.Error(message, token);
    }
}
