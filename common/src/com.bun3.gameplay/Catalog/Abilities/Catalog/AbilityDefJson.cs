#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Effects.Catalog;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;
using Newtonsoft.Json.Linq;

using static Bun3.Gameplay.Effects.Catalog.JsonFields;

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
        public static List<AbilityDef> Load(Stream utf8Json, IReadOnlyDictionary<string, ushort> attributeNames) =>
            LoadDocument(utf8Json, attributeNames, "abilities", ReadAbility);

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
    }
}
