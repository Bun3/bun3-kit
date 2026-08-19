#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Catalog
{
    // Byte layout, schema 1. All integers are little-endian; bytes cannot describe themselves, so the
    // layout is spelled out here and nowhere else.
    //
    // Header, 83 bytes:
    //   0   9   magic "BUN3GPCAT"
    //   9   2   schema version (ushort)
    //   11  4   AttributeRegistry.Signature (uint)
    //   15  4   payload length in bytes (uint)
    //   19  32  TagCatalog.Fingerprint
    //   51  32  SHA-256 of the whole artifact computed with these 32 bytes zeroed
    //   83  ..  payload
    //
    // Payload primitives:
    //   bool     1 byte, 0 or 1
    //   enum     1 byte
    //   tag      ushort catalog index; 0 is GameplayTag.None
    //   bignum   int64 mantissa + int32 exponent
    //   operand  byte kind + ushort attribute id + bignum coefficient
    //   string   int32 byte count (-1 = null) + UTF-8 bytes
    //   array    int32 element count (-1 = null for the nullable variants) + elements
    //
    // Payload sections, in order:
    //   effects            array of effect records
    //   build warnings     array of string
    //   abilities          array of ability records
    //
    // Effect record: name, maxLevel, durationType, durationTicks, periodTicks, stack policy
    //   (maxStack, onReapply, addStackCount, refreshDurationOnReapply, resetPeriodOnReapply,
    //   onExpiration, onOverflow, overflowEffectName, clearStacksOnOverflow, levelFromStack,
    //   extendCapMultiplier), modifiers, executions, applicationConditions, ongoingConditions,
    //   grantedTags, grantedAbilityNames, assetTags, immunityTags, chains, overflowEffectId,
    //   removeOnApplyTags, chanceToApply, durationPerLevel, durationScale, drCategory,
    //   drWindowTicks, drStageMultipliers, raiseEvents.
    //
    // Ability record: name, maxLevel, assetTags, requiredTags, blockedTags, activationConditions,
    //   costs, cooldownEffectId, effects.
    //
    // Seam contracts are never stored: only the tag they were resolved from travels, and Load
    // re-resolves them through the SeamRegistry it is handed. Name-to-id maps are not stored either
    // — records are written in id order, so ids are their positions.

    /// <summary>
    /// Reads and writes the shipping compiled-catalog artifact: an <see cref="EffectCatalog"/> and the
    /// <see cref="AbilityCatalog"/> built on top of it, in one file. Loading it replaces authoring-JSON
    /// parsing, level-table evaluation, and validation at startup; the JSON authoring data stays the
    /// source of truth and this artifact is a derived build output.
    /// </summary>
    public static class GameplayCatalogBinary
    {
        private const int HeaderSize = 83;
        private const int SignatureOffset = 11;
        private const int PayloadLengthOffset = 15;
        private const int FingerprintOffset = 19;
        private const int ChecksumOffset = 51;
        private const int HashLength = 32;
        private const ushort SupportedSchema = 1;

        // Same guard as the tag catalog artifact — larger input cannot be a valid catalog, so fail as a
        // format error instead of dying on OOM.
        private const int MaximumArtifactBytes = 64 * 1024 * 1024;

        private static readonly byte[] Magic =
        {
            (byte)'B', (byte)'U', (byte)'N', (byte)'3', (byte)'G', (byte)'P', (byte)'C', (byte)'A', (byte)'T',
        };

        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>Writes the compiled catalogs as a single deterministic schema 1 artifact.</summary>
        /// <param name="output">Writable output stream; the artifact is written at the current position.</param>
        /// <param name="effects">Compiled effect catalog.</param>
        /// <param name="abilities">Compiled ability catalog built against <paramref name="effects"/>.</param>
        /// <param name="tags">Tag catalog whose fingerprint pins the tag indices in the artifact.</param>
        /// <param name="attributes">Attribute registry whose signature pins the attribute shape.</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        /// <exception cref="ArgumentException">The output stream is not writable.</exception>
        public static void Write(
            Stream output, EffectCatalog effects, AbilityCatalog abilities, TagCatalog tags,
            AttributeRegistry attributes)
        {
            if (output is null) throw new ArgumentNullException(nameof(output));
            if (effects is null) throw new ArgumentNullException(nameof(effects));
            if (abilities is null) throw new ArgumentNullException(nameof(abilities));
            if (tags is null) throw new ArgumentNullException(nameof(tags));
            if (attributes is null) throw new ArgumentNullException(nameof(attributes));
            if (!output.CanWrite) throw new ArgumentException("A writable stream is required.", nameof(output));

            var writer = new BufferWriter(HeaderSize + 1024);
            writer.WriteBytes(Magic);
            writer.WriteUInt16(SupportedSchema);
            writer.WriteUInt32(attributes.Signature);
            writer.WriteUInt32(0);
            writer.WriteBytes(tags.Fingerprint);
            writer.WriteZeroes(HashLength);

            writer.WriteInt32(effects.Count);
            for (var id = 0; id < effects.Count; id++) WriteEffect(writer, effects.GetSpec(id));

            var warnings = effects.BuildWarnings;
            writer.WriteInt32(warnings.Count);
            for (var i = 0; i < warnings.Count; i++) writer.WriteString(warnings[i]);

            writer.WriteInt32(abilities.Count);
            for (var id = 0; id < abilities.Count; id++) WriteAbility(writer, abilities.GetAbility(id));

            writer.PatchUInt32(PayloadLengthOffset, checked((uint)(writer.Length - HeaderSize)));
            var bytes = writer.ToArray();
            using (var sha256 = SHA256.Create())
            {
                sha256.ComputeHash(bytes).CopyTo(bytes, ChecksumOffset);
            }

            output.Write(bytes, 0, bytes.Length);
        }

        /// <summary>Reads an artifact written by <see cref="Write"/>, re-resolving seams from the registry.</summary>
        /// <param name="input">Readable artifact stream; read from the current position to the end.</param>
        /// <param name="tags">Tag catalog the artifact must have been written against.</param>
        /// <param name="seams">Seam registry resolving the stored magnitude, execution, and selector tags.</param>
        /// <param name="attributes">Attribute registry the artifact must have been written against.</param>
        /// <returns>The restored effect and ability catalogs.</returns>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        /// <exception cref="ArgumentException">The input stream is not readable.</exception>
        /// <exception cref="TagCatalogFormatException">The magic, schema, length, checksum, or payload structure is invalid.</exception>
        /// <exception cref="TagCatalogCompatibilityException">The tag fingerprint or attribute signature
        /// differs, or a stored seam tag is not registered in <paramref name="seams"/>.</exception>
        public static (EffectCatalog Effects, AbilityCatalog Abilities) Load(
            Stream input, TagCatalog tags, SeamRegistry seams, AttributeRegistry attributes)
        {
            if (input is null) throw new ArgumentNullException(nameof(input));
            if (tags is null) throw new ArgumentNullException(nameof(tags));
            if (seams is null) throw new ArgumentNullException(nameof(seams));
            if (attributes is null) throw new ArgumentNullException(nameof(attributes));
            if (!input.CanRead) throw new ArgumentException("A readable stream is required.", nameof(input));

            var bytes = ReadToEnd(input);
            if (bytes.Length < HeaderSize) throw Format("Artifact header is truncated.");
            if (!bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic)) throw Format("Artifact magic is missing.");
            if (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(9, 2)) != SupportedSchema)
            {
                throw Format("Unsupported compiled gameplay catalog schema.");
            }

            var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(PayloadLengthOffset, 4));
            if ((ulong)HeaderSize + payloadLength != (ulong)bytes.Length)
            {
                throw Format("Artifact payload length does not match the actual file length.");
            }

            ValidateChecksum(bytes);

            if (!bytes.AsSpan(FingerprintOffset, HashLength).SequenceEqual(tags.Fingerprint))
            {
                throw new TagCatalogCompatibilityException(
                    "Compiled gameplay catalog was built against a different tag catalog fingerprint.");
            }

            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(SignatureOffset, 4)) != attributes.Signature)
            {
                throw new TagCatalogCompatibilityException(
                    "Compiled gameplay catalog was built against a different attribute registry shape.");
            }

            var reader = new PayloadReader(bytes, HeaderSize, checked((int)payloadLength));
            var specs = new CompiledEffectSpec[reader.ReadLength("effect count")];
            var effectNameToId = new Dictionary<string, int>(specs.Length, StringComparer.Ordinal);
            for (var id = 0; id < specs.Length; id++)
            {
                specs[id] = ReadEffect(reader, seams);
                if (!effectNameToId.TryAdd(specs[id].Name, id)) throw Format("Duplicate effect name.");
            }

            var warningCount = reader.ReadLength("build warning count");
            var warnings = new List<string>(warningCount);
            for (var i = 0; i < warningCount; i++)
            {
                warnings.Add(reader.ReadString("build warning") ?? throw Format("Build warning must not be null."));
            }

            var abilities = new CompiledAbility[reader.ReadLength("ability count")];
            var abilityNameToId = new Dictionary<string, int>(abilities.Length, StringComparer.Ordinal);
            for (var id = 0; id < abilities.Length; id++)
            {
                abilities[id] = ReadAbility(reader, seams);
                if (!abilityNameToId.TryAdd(abilities[id].Name, id)) throw Format("Duplicate ability name.");
            }

            if (reader.Remaining != 0) throw Format("Unparsed bytes remain after the payload.");

            return (
                new EffectCatalog(effectNameToId, specs, warnings),
                new AbilityCatalog(abilities, abilityNameToId));
        }

        private static void WriteEffect(BufferWriter writer, CompiledEffectSpec spec)
        {
            writer.WriteString(spec.Name);
            writer.WriteInt32(spec.MaxLevel);
            writer.WriteByte((byte)spec.DurationType);
            writer.WriteInt32(spec.DurationTicks);
            writer.WriteInt32(spec.PeriodTicks);

            var stack = spec.Stack;
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

            writer.WriteInt32(spec.Modifiers.Length);
            foreach (var modifier in spec.Modifiers)
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

            writer.WriteInt32(spec.Executions.Length);
            foreach (var execution in spec.Executions)
            {
                writer.WriteTag(execution.CalcTag);
                writer.WriteInt32(execution.Inputs.Length);
                foreach (var input in execution.Inputs) writer.WriteOperand(input);
            }

            WriteConditions(writer, spec.ApplicationConditions);
            WriteConditions(writer, spec.OngoingConditions);
            WriteTags(writer, spec.GrantedTags);

            writer.WriteInt32(spec.GrantedAbilityNames.Length);
            foreach (var name in spec.GrantedAbilityNames) writer.WriteString(name);

            WriteTags(writer, spec.AssetTags);
            WriteTags(writer, spec.ImmunityTags);

            writer.WriteInt32(spec.Chains.Length);
            foreach (var chain in spec.Chains)
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

            writer.WriteInt32(spec.OverflowEffectId);
            WriteTags(writer, spec.RemoveOnApplyTags);
            WriteMagnitude(writer, spec.ChanceToApply);
            writer.WriteNullableBigNums(spec.DurationPerLevel);
            WriteMagnitude(writer, spec.DurationScale);
            writer.WriteTag(spec.DrCategory);
            writer.WriteInt32(spec.DrWindowTicks);
            writer.WriteBigNums(spec.DrStageMultipliers);

            writer.WriteInt32(spec.RaiseEvents.Length);
            foreach (var raise in spec.RaiseEvents)
            {
                writer.WriteTag(raise.Tag);
                writer.WriteUInt16(raise.AttributeId);
            }
        }

        private static CompiledEffectSpec ReadEffect(PayloadReader reader, SeamRegistry seams)
        {
            var name = reader.ReadString("effect name") ?? throw Format("Effect name must not be null.");
            var maxLevel = reader.ReadInt32("effect max level");
            var durationType = (EffectDurationType)reader.ReadByte("duration type");
            var durationTicks = reader.ReadInt32("duration ticks");
            var periodTicks = reader.ReadInt32("period ticks");

            var stack = new StackPolicy
            {
                MaxStack = reader.ReadInt32("max stack"),
                OnReapply = (StackReapply)reader.ReadByte("stack reapply"),
                AddStackCount = reader.ReadInt32("add stack count"),
                RefreshDurationOnReapply = reader.ReadBool("refresh duration on reapply"),
                ResetPeriodOnReapply = reader.ReadBool("reset period on reapply"),
                OnExpiration = (StackExpiration)reader.ReadByte("stack expiration"),
                OnOverflow = (StackOverflow)reader.ReadByte("stack overflow"),
                OverflowEffectName = reader.ReadString("overflow effect name"),
                ClearStacksOnOverflow = reader.ReadBool("clear stacks on overflow"),
                LevelFromStack = reader.ReadBool("level from stack"),
                ExtendCapMultiplier = reader.ReadBigNum("extend cap multiplier"),
            };

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

            var chains = new CompiledChain[reader.ReadLength("chain count")];
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
                chains[i] = new CompiledChain(
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

            return new CompiledEffectSpec(
                name, maxLevel, durationType, durationTicks, periodTicks, stack,
                modifiers, executions, applicationConditions, ongoingConditions,
                grantedTags, grantedAbilityNames, assetTags, immunityTags, chains, overflowEffectId,
                removeOnApplyTags, chanceToApply, durationPerLevel, durationScale,
                drCategory, drWindowTicks, drStageMultipliers, raiseEvents);
        }

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

        private static void WriteMagnitude(BufferWriter writer, CompiledMagnitude? magnitude)
        {
            writer.WriteBool(magnitude != null);
            if (magnitude is null) return;
            writer.WriteNullableOperand(magnitude.Base);
            writer.WriteNullableOperand(magnitude.PerLevel);
            writer.WriteTag(magnitude.CalcTag);
            writer.WriteNullableBigNums(magnitude.ByLevel);
            writer.WriteByte((byte)magnitude.Tail);
            writer.WriteBigNum(magnitude.Increment);
        }

        private static CompiledMagnitude? ReadMagnitude(PayloadReader reader, SeamRegistry seams, string field)
        {
            if (!reader.ReadBool(field + " present")) return null;
            var magnitudeBase = reader.ReadNullableOperand(field + " base");
            var perLevel = reader.ReadNullableOperand(field + " per level");
            var calcTag = reader.ReadTag(field + " calc tag");
            var byLevel = reader.ReadNullableBigNums(field + " level table");
            var tail = (LevelTail)reader.ReadByte(field + " tail");
            var increment = reader.ReadBigNum(field + " increment");
            return new CompiledMagnitude(
                magnitudeBase, perLevel, MagnitudeCalc(seams, calcTag), calcTag, byLevel, tail, increment);
        }

        // An artifact naming a seam the running registry does not have is the same class of failure as a
        // fingerprint mismatch: the artifact was built against a different shape.
        private static IMagnitudeCalc? MagnitudeCalc(SeamRegistry seams, GameplayTag tag)
        {
            if (!tag.IsValid) return null;
            if (seams.TryGetMagnitudeCalc(tag, out var calc) && calc != null) return calc;
            throw Unregistered("magnitude calc", tag);
        }

        private static IExecutionCalc ExecutionCalc(SeamRegistry seams, GameplayTag tag)
        {
            if (seams.TryGetExecutionCalc(tag, out var calc) && calc != null) return calc;
            throw Unregistered("execution calc", tag);
        }

        private static ITargetSelector? TargetSelector(SeamRegistry seams, GameplayTag tag)
        {
            if (!tag.IsValid) return null;
            if (seams.TryGetTargetSelector(tag, out var selector) && selector != null) return selector;
            throw Unregistered("target selector", tag);
        }

        private static TagCatalogCompatibilityException Unregistered(string kind, GameplayTag tag) =>
            new TagCatalogCompatibilityException(
                $"Compiled gameplay catalog needs a {kind} registered for tag index {tag.Index}, "
                + "but the seam registry has none.");

        private static void WriteConditions(BufferWriter writer, CompiledCondition[] conditions)
        {
            writer.WriteInt32(conditions.Length);
            foreach (var condition in conditions)
            {
                writer.WriteOperand(condition.Left);
                writer.WriteByte((byte)condition.Op);
                writer.WriteOperand(condition.Right);
            }
        }

        private static CompiledCondition[] ReadConditions(PayloadReader reader, string field)
        {
            var conditions = new CompiledCondition[reader.ReadLength(field + " count")];
            for (var i = 0; i < conditions.Length; i++)
            {
                conditions[i] = new CompiledCondition(
                    reader.ReadOperand(field + " left"),
                    (ComparisonOp)reader.ReadByte(field + " op"),
                    reader.ReadOperand(field + " right"));
            }

            return conditions;
        }

        private static void WriteTags(BufferWriter writer, GameplayTag[] tags)
        {
            writer.WriteInt32(tags.Length);
            foreach (var tag in tags) writer.WriteTag(tag);
        }

        private static GameplayTag[] ReadTags(PayloadReader reader, string field)
        {
            var tags = new GameplayTag[reader.ReadLength(field + " count")];
            for (var i = 0; i < tags.Length; i++) tags[i] = reader.ReadTag(field);
            return tags;
        }

        private static byte[] ReadToEnd(Stream input)
        {
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                var read = input.Read(buffer, 0, buffer.Length);
                if (read == 0) return output.ToArray();
                if (output.Length + read > MaximumArtifactBytes)
                {
                    throw Format("Compiled gameplay catalog input exceeds the allowed size.");
                }

                output.Write(buffer, 0, read);
            }
        }

        private static void ValidateChecksum(byte[] bytes)
        {
            var stored = bytes.AsSpan(ChecksumOffset, HashLength).ToArray();
            bytes.AsSpan(ChecksumOffset, HashLength).Clear();
            byte[] actual;
            using (var sha256 = SHA256.Create())
            {
                actual = sha256.ComputeHash(bytes);
            }

            stored.CopyTo(bytes, ChecksumOffset);
            if (!actual.AsSpan().SequenceEqual(stored))
            {
                throw Format("Compiled gameplay catalog checksum mismatch.");
            }
        }

        private static TagCatalogFormatException Format(string message) => new TagCatalogFormatException(message);

        private sealed class BufferWriter
        {
            private byte[] _buffer;

            internal BufferWriter(int capacity) => _buffer = new byte[capacity];

            internal int Length { get; private set; }

            internal void WriteByte(byte value)
            {
                Ensure(1);
                _buffer[Length++] = value;
            }

            internal void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

            internal void WriteUInt16(ushort value)
            {
                Ensure(2);
                BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(Length, 2), value);
                Length += 2;
            }

            internal void WriteUInt32(uint value)
            {
                Ensure(4);
                BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(Length, 4), value);
                Length += 4;
            }

            internal void WriteInt32(int value)
            {
                Ensure(4);
                BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(Length, 4), value);
                Length += 4;
            }

            internal void WriteInt64(long value)
            {
                Ensure(8);
                BinaryPrimitives.WriteInt64LittleEndian(_buffer.AsSpan(Length, 8), value);
                Length += 8;
            }

            internal void WriteTag(GameplayTag tag) => WriteUInt16(tag.Index);

            internal void WriteBigNum(BigNum value)
            {
                WriteInt64(value.Mantissa);
                WriteInt32(value.Exponent);
            }

            internal void WriteOperand(Operand operand)
            {
                WriteByte((byte)operand.Kind);
                WriteUInt16(operand.AttributeId);
                WriteBigNum(operand.Value);
            }

            internal void WriteNullableOperand(Operand? operand)
            {
                WriteBool(operand.HasValue);
                if (operand.HasValue) WriteOperand(operand.Value);
            }

            internal void WriteBigNums(BigNum[] values)
            {
                WriteInt32(values.Length);
                foreach (var value in values) WriteBigNum(value);
            }

            internal void WriteNullableBigNums(BigNum[]? values)
            {
                if (values is null)
                {
                    WriteInt32(-1);
                    return;
                }

                WriteBigNums(values);
            }

            internal void WriteString(string? value)
            {
                if (value is null)
                {
                    WriteInt32(-1);
                    return;
                }

                var bytes = StrictUtf8.GetBytes(value);
                WriteInt32(bytes.Length);
                WriteBytes(bytes);
            }

            internal void WriteBytes(ReadOnlySpan<byte> value)
            {
                Ensure(value.Length);
                value.CopyTo(_buffer.AsSpan(Length));
                Length += value.Length;
            }

            internal void WriteZeroes(int count)
            {
                Ensure(count);
                _buffer.AsSpan(Length, count).Clear();
                Length += count;
            }

            internal void PatchUInt32(int offset, uint value) =>
                BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(offset, 4), value);

            internal byte[] ToArray()
            {
                var result = new byte[Length];
                Array.Copy(_buffer, result, Length);
                return result;
            }

            private void Ensure(int additional)
            {
                var required = checked(Length + additional);
                if (required <= _buffer.Length) return;
                var capacity = _buffer.Length;
                while (capacity < required) capacity = checked(capacity * 2);
                Array.Resize(ref _buffer, capacity);
            }
        }

        private sealed class PayloadReader
        {
            private readonly byte[] _bytes;
            private readonly int _end;
            private int _offset;

            internal PayloadReader(byte[] bytes, int offset, int length)
            {
                _bytes = bytes;
                _offset = offset;
                _end = checked(offset + length);
            }

            internal int Remaining => _end - _offset;

            internal byte ReadByte(string field)
            {
                Require(1, field);
                return _bytes[_offset++];
            }

            internal bool ReadBool(string field)
            {
                var value = ReadByte(field);
                if (value > 1) throw Format(field + " is not a boolean.");
                return value == 1;
            }

            internal ushort ReadUInt16(string field)
            {
                Require(2, field);
                var value = BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(_offset, 2));
                _offset += 2;
                return value;
            }

            internal int ReadInt32(string field)
            {
                Require(4, field);
                var value = BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(_offset, 4));
                _offset += 4;
                return value;
            }

            internal long ReadInt64(string field)
            {
                Require(8, field);
                var value = BinaryPrimitives.ReadInt64LittleEndian(_bytes.AsSpan(_offset, 8));
                _offset += 8;
                return value;
            }

            internal GameplayTag ReadTag(string field) => new GameplayTag(ReadUInt16(field));

            internal BigNum ReadBigNum(string field)
            {
                var mantissa = ReadInt64(field + " mantissa");
                var exponent = ReadInt32(field + " exponent");
                try
                {
                    return BigNum.FromParts(mantissa, exponent);
                }
                catch (Exception exception) when (exception is ArgumentOutOfRangeException
                                                  || exception is BigNumOverflowException)
                {
                    throw new TagCatalogFormatException(field + " is not a representable BigNum.", exception);
                }
            }

            internal Operand ReadOperand(string field)
            {
                var kind = (OperandKind)ReadByte(field + " kind");
                var attributeId = ReadUInt16(field + " attribute id");
                var value = ReadBigNum(field + " value");
                switch (kind)
                {
                    case OperandKind.Constant: return Operand.Constant(value);
                    case OperandKind.Attribute: return Operand.Attribute(attributeId, value);
                    case OperandKind.SourceAttribute: return Operand.SourceAttribute(attributeId, value);
                    case OperandKind.EventMagnitude: return Operand.EventMagnitude(value);
                    default: throw Format(field + " has an unknown operand kind.");
                }
            }

            internal Operand? ReadNullableOperand(string field) =>
                ReadBool(field + " present") ? ReadOperand(field) : (Operand?)null;

            internal BigNum[] ReadBigNums(string field)
            {
                var values = new BigNum[ReadLength(field + " count")];
                for (var i = 0; i < values.Length; i++) values[i] = ReadBigNum(field);
                return values;
            }

            internal BigNum[]? ReadNullableBigNums(string field)
            {
                var count = ReadInt32(field + " count");
                if (count == -1) return null;
                if (count < 0 || count > Remaining) throw Format(field + " count exceeds the payload boundary.");
                var values = new BigNum[count];
                for (var i = 0; i < count; i++) values[i] = ReadBigNum(field);
                return values;
            }

            internal string? ReadString(string field)
            {
                var length = ReadInt32(field + " length");
                if (length == -1) return null;
                Require(length, field);
                string value;
                try
                {
                    value = StrictUtf8.GetString(_bytes, _offset, length);
                }
                catch (DecoderFallbackException exception)
                {
                    throw new TagCatalogFormatException(field + " is not strict UTF-8.", exception);
                }

                _offset += length;
                return value;
            }

            // Element counts are bounded by the remaining bytes (every element costs at least one byte),
            // so a corrupted count cannot make the reader allocate an arbitrary array.
            internal int ReadLength(string field)
            {
                var count = ReadInt32(field);
                if (count < 0 || count > Remaining) throw Format(field + " exceeds the payload boundary.");
                return count;
            }

            private void Require(int length, string field)
            {
                if (length < 0 || length > Remaining)
                {
                    throw Format(field + " extends past the payload boundary.");
                }
            }
        }
    }
}
