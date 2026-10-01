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
    //   targetMode, targetRequiredTags, targetBlockedTags, targetConditions, costs, cooldownEffectId,
    //   effects (each: effectId, origin, selectorTag, selectorParams, levelRule, fixedLevel).
    //
    // Seam contracts are never stored: only the tag they were resolved from travels, and Load
    // re-resolves them through the SeamRegistry it is handed. Name-to-id maps are not stored either
    // — records are written in id order, so ids are their positions.

    /// <summary>
    /// Reads and writes the shipping compiled-catalog artifact: an <see cref="EffectCatalog"/> and the
    /// <see cref="AbilityCatalog"/> built on top of it, in one file. Loading it replaces authoring-JSON
    /// parsing, level-table evaluation, and validation at startup; the JSON authoring data stays the
    /// source of truth and this artifact is a derived build output. Composed of this file (layout, Write,
    /// Load) plus <c>GameplayCatalogBinary.Effects.cs</c>, <c>.Abilities.cs</c>, <c>.Fields.cs</c> (magnitudes,
    /// conditions, tags, seam re-resolution) and <c>.Buffers.cs</c> (byte writer and payload reader).
    /// </summary>
    public static partial class GameplayCatalogBinary
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
            for (var id = 0; id < effects.Count; id++) WriteEffect(writer, effects.GetEffect(id));

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

            var bytes = ArtifactBytes.ReadToEnd(input, MaximumArtifactBytes, "Compiled gameplay catalog input exceeds the allowed size.");
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

            ArtifactBytes.ValidateChecksum(bytes, ChecksumOffset, "Compiled gameplay catalog checksum mismatch.");

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
            var effects = new CompiledEffect[reader.ReadLength("effect count")];
            var effectNameToId = new Dictionary<string, int>(effects.Length, StringComparer.Ordinal);
            for (var id = 0; id < effects.Length; id++)
            {
                effects[id] = ReadEffect(reader, seams);
                if (!effectNameToId.TryAdd(effects[id].Name, id)) throw Format("Duplicate effect name.");
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
                new EffectCatalog(effectNameToId, effects, warnings),
                new AbilityCatalog(abilities, abilityNameToId));
        }

        private static TagCatalogFormatException Format(string message) => new TagCatalogFormatException(message);
    }
}
