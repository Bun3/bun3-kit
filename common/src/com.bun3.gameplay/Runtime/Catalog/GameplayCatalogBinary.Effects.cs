// GameplayCatalogBinary partial — effect record codec.
#nullable enable
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Catalog
{
    public static partial class GameplayCatalogBinary
    {
        private static void WriteEffect(BufferWriter writer, CompiledEffect effect)
        {
            writer.WriteString(effect.Name);
            writer.WriteInt32(effect.MaxLevel);
            writer.WriteByte((byte)effect.DurationType);
            writer.WriteInt32(effect.DurationTicks);
            writer.WriteInt32(effect.PeriodTicks);

            var stack = effect.Stack;
            writer.WriteInt32(stack.MaxStack);
            writer.WriteByte((byte)stack.OnReapply);
            writer.WriteInt32(stack.AddStackCount);
            writer.WriteBool(stack.RefreshDurationOnReapply);
            writer.WriteBool(stack.ResetPeriodOnReapply);
            writer.WriteByte((byte)stack.OnExpiration);
            writer.WriteByte((byte)stack.OnOverflow);
            writer.WriteString(stack.OverflowEffectName);
            writer.WriteBool(stack.ClearStacksOnOverflow);
            writer.WriteBool(stack.LevelFromStack);
            writer.WriteBigNum(stack.ExtendCapMultiplier);

            writer.WriteInt32(effect.Modifiers.Length);
            foreach (var modifier in effect.Modifiers)
            {
                writer.WriteUInt16(modifier.AttributeId);
                writer.WriteByte((byte)modifier.Op);
                writer.WriteNullableOperand(modifier.Base);
                writer.WriteNullableOperand(modifier.PerLevel);
                writer.WriteTag(modifier.CalcTag);
                writer.WriteNullableBigNums(modifier.ByLevel);
                writer.WriteByte((byte)modifier.Tail);
                writer.WriteBigNum(modifier.Increment);
                writer.WriteBool(modifier.ScaleWithStack);
            }

            writer.WriteInt32(effect.Executions.Length);
            foreach (var execution in effect.Executions)
            {
                writer.WriteTag(execution.CalcTag);
                writer.WriteInt32(execution.Inputs.Length);
                foreach (var input in execution.Inputs) writer.WriteOperand(input);
            }

            WriteConditions(writer, effect.ApplicationConditions);
            WriteConditions(writer, effect.OngoingConditions);
            WriteTags(writer, effect.GrantedTags);

            writer.WriteInt32(effect.GrantedAbilityNames.Length);
            foreach (var name in effect.GrantedAbilityNames) writer.WriteString(name);

            WriteTags(writer, effect.AssetTags);
            WriteTags(writer, effect.ImmunityTags);

            writer.WriteInt32(effect.Chains.Length);
            foreach (var chain in effect.Chains)
            {
                writer.WriteByte((byte)chain.Trigger);
                writer.WriteInt32(chain.EffectId);
                writer.WriteTag(chain.SelectorTag);
                writer.WriteBigNums(chain.SelectorParams);
                WriteConditions(writer, chain.Conditions);
                writer.WriteByte((byte)chain.LevelRule);
                writer.WriteInt32(chain.FixedLevel);
                writer.WriteTag(chain.EventTag);
                writer.WriteByte((byte)chain.Listen);
            }

            writer.WriteInt32(effect.OverflowEffectId);
            WriteTags(writer, effect.RemoveOnApplyTags);
            WriteMagnitude(writer, effect.ChanceToApply);
            writer.WriteNullableBigNums(effect.DurationPerLevel);
            WriteMagnitude(writer, effect.DurationScale);
            writer.WriteTag(effect.DrCategory);
            writer.WriteInt32(effect.DrWindowTicks);
            writer.WriteBigNums(effect.DrStageMultipliers);

            writer.WriteInt32(effect.RaiseEvents.Length);
            foreach (var raise in effect.RaiseEvents)
            {
                writer.WriteTag(raise.Tag);
                writer.WriteUInt16(raise.AttributeId);
            }
        }

        private static CompiledEffect ReadEffect(PayloadReader reader, SeamRegistry seams)
        {
            var name = reader.ReadString("effect name") ?? throw Format("Effect name must not be null.");
            var maxLevel = reader.ReadInt32("effect max level");
            var durationType = (EffectDurationType)reader.ReadByte("duration type");
            var durationTicks = reader.ReadInt32("duration ticks");
            var periodTicks = reader.ReadInt32("period ticks");

            var stack = new CompiledStackPolicy(
                reader.ReadInt32("max stack"),
                (StackReapply)reader.ReadByte("stack reapply"),
                reader.ReadInt32("add stack count"),
                reader.ReadBool("refresh duration on reapply"),
                reader.ReadBool("reset period on reapply"),
                (StackExpiration)reader.ReadByte("stack expiration"),
                (StackOverflow)reader.ReadByte("stack overflow"),
                reader.ReadString("overflow effect name"),
                reader.ReadBool("clear stacks on overflow"),
                reader.ReadBool("level from stack"),
                reader.ReadBigNum("extend cap multiplier"));

            var modifiers = new CompiledModifier[reader.ReadLength("modifier count")];
            for (var i = 0; i < modifiers.Length; i++)
            {
                var attributeId = reader.ReadUInt16("modifier attribute id");
                var op = (AttributeModifierOp)reader.ReadByte("modifier op");
                var magnitudeBase = reader.ReadNullableOperand("modifier base");
                var perLevel = reader.ReadNullableOperand("modifier per level");
                var calcTag = reader.ReadTag("modifier calc tag");
                var byLevel = reader.ReadNullableBigNums("modifier level table");
                var tail = (LevelTail)reader.ReadByte("modifier tail");
                var increment = reader.ReadBigNum("modifier increment");
                var scaleWithStack = reader.ReadBool("modifier scale with stack");
                modifiers[i] = new CompiledModifier(
                    attributeId, op, magnitudeBase, perLevel, MagnitudeCalc(seams, calcTag), calcTag,
                    byLevel, tail, increment, scaleWithStack);
            }

            var executions = new CompiledExecution[reader.ReadLength("execution count")];
            for (var i = 0; i < executions.Length; i++)
            {
                var calcTag = reader.ReadTag("execution calc tag");
                var inputs = new Operand[reader.ReadLength("execution input count")];
                for (var j = 0; j < inputs.Length; j++) inputs[j] = reader.ReadOperand("execution input");
                executions[i] = new CompiledExecution(ExecutionCalc(seams, calcTag), calcTag, inputs);
            }

            var applicationConditions = ReadConditions(reader, "application condition");
            var ongoingConditions = ReadConditions(reader, "ongoing condition");
            var grantedTags = ReadTags(reader, "granted tag");

            var grantedAbilityNames = new string[reader.ReadLength("granted ability count")];
            for (var i = 0; i < grantedAbilityNames.Length; i++)
            {
                grantedAbilityNames[i] = reader.ReadString("granted ability name")
                                         ?? throw Format("Granted ability name must not be null.");
            }

            var assetTags = ReadTags(reader, "asset tag");
            var immunityTags = ReadTags(reader, "immunity tag");

            var chains = new CompiledChainEdge[reader.ReadLength("chain count")];
            for (var i = 0; i < chains.Length; i++)
            {
                var trigger = (ChainTrigger)reader.ReadByte("chain trigger");
                var effectId = reader.ReadInt32("chain effect id");
                var selectorTag = reader.ReadTag("chain selector tag");
                var selectorParams = reader.ReadBigNums("chain selector params");
                var conditions = ReadConditions(reader, "chain condition");
                var levelRule = (ChainLevelRule)reader.ReadByte("chain level rule");
                var fixedLevel = reader.ReadInt32("chain fixed level");
                var eventTag = reader.ReadTag("chain event tag");
                var listen = (ChainListen)reader.ReadByte("chain listen");
                chains[i] = new CompiledChainEdge(
                    trigger, effectId, TargetSelector(seams, selectorTag), selectorTag, selectorParams,
                    conditions, levelRule, fixedLevel, eventTag, listen);
            }

            var overflowEffectId = reader.ReadInt32("overflow effect id");
            var removeOnApplyTags = ReadTags(reader, "remove on apply tag");
            var chanceToApply = ReadMagnitude(reader, seams, "chance to apply");
            var durationPerLevel = reader.ReadNullableBigNums("duration per level");
            var durationScale = ReadMagnitude(reader, seams, "duration scale");
            var drCategory = reader.ReadTag("dr category");
            var drWindowTicks = reader.ReadInt32("dr window ticks");
            var drStageMultipliers = reader.ReadBigNums("dr stage multipliers");

            var raiseEvents = new CompiledRaiseEvent[reader.ReadLength("raise event count")];
            for (var i = 0; i < raiseEvents.Length; i++)
            {
                raiseEvents[i] = new CompiledRaiseEvent(
                    reader.ReadTag("raise event tag"), reader.ReadUInt16("raise event attribute id"));
            }

            return new CompiledEffect(
                name, maxLevel, durationType, durationTicks, periodTicks, stack,
                modifiers, executions, applicationConditions, ongoingConditions,
                grantedTags, grantedAbilityNames, assetTags, immunityTags, chains, overflowEffectId,
                removeOnApplyTags, chanceToApply, durationPerLevel, durationScale,
                drCategory, drWindowTicks, drStageMultipliers, raiseEvents);
        }
    }
}
