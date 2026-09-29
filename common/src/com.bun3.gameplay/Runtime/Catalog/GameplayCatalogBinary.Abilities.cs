// GameplayCatalogBinary partial — ability record codec.
#nullable enable
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Catalog
{
    public static partial class GameplayCatalogBinary
    {
        private static void WriteAbility(BufferWriter writer, CompiledAbility ability)
        {
            writer.WriteString(ability.Name);
            writer.WriteInt32(ability.MaxLevel);
            WriteTags(writer, ability.AssetTags);
            WriteTags(writer, ability.RequiredTags);
            WriteTags(writer, ability.BlockedTags);
            WriteConditions(writer, ability.ActivationConditions);

            writer.WriteInt32(ability.Costs.Length);
            foreach (var cost in ability.Costs)
            {
                writer.WriteUInt16(cost.AttributeId);
                WriteMagnitude(writer, cost.Magnitude);
            }

            writer.WriteInt32(ability.CooldownEffectId);

            writer.WriteInt32(ability.Effects.Length);
            foreach (var effect in ability.Effects)
            {
                writer.WriteInt32(effect.EffectId);
                writer.WriteTag(effect.SelectorTag);
                writer.WriteBigNums(effect.SelectorParams);
                writer.WriteByte((byte)effect.LevelRule);
                writer.WriteInt32(effect.FixedLevel);
            }
        }

        private static CompiledAbility ReadAbility(PayloadReader reader, SeamRegistry seams)
        {
            var name = reader.ReadString("ability name") ?? throw Format("Ability name must not be null.");
            var maxLevel = reader.ReadInt32("ability max level");
            var assetTags = ReadTags(reader, "ability asset tag");
            var requiredTags = ReadTags(reader, "ability required tag");
            var blockedTags = ReadTags(reader, "ability blocked tag");
            var activationConditions = ReadConditions(reader, "activation condition");

            var costs = new CompiledCost[reader.ReadLength("cost count")];
            for (var i = 0; i < costs.Length; i++)
            {
                var attributeId = reader.ReadUInt16("cost attribute id");
                var magnitude = ReadMagnitude(reader, seams, "cost magnitude")
                                ?? throw Format("Cost magnitude must not be null.");
                costs[i] = new CompiledCost(attributeId, magnitude);
            }

            var cooldownEffectId = reader.ReadInt32("cooldown effect id");

            var effects = new CompiledAbilityEffect[reader.ReadLength("ability effect count")];
            for (var i = 0; i < effects.Length; i++)
            {
                var effectId = reader.ReadInt32("ability effect id");
                var selectorTag = reader.ReadTag("ability effect selector tag");
                var selectorParams = reader.ReadBigNums("ability effect selector params");
                var levelRule = (ChainLevelRule)reader.ReadByte("ability effect level rule");
                var fixedLevel = reader.ReadInt32("ability effect fixed level");
                effects[i] = new CompiledAbilityEffect(
                    effectId, TargetSelector(seams, selectorTag), selectorTag, selectorParams, levelRule,
                    fixedLevel);
            }

            return new CompiledAbility(
                name, maxLevel, assetTags, requiredTags, blockedTags, activationConditions, costs,
                cooldownEffectId, effects);
        }
    }
}
