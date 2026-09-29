// GameplayCatalogBinary partial — field codecs shared by both records (magnitudes, conditions, tags, seam re-resolution).
#nullable enable
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Catalog
{
    public static partial class GameplayCatalogBinary
    {
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
            if (!reader.ReadBool(field)) return null;
            var magnitudeBase = reader.ReadNullableOperand(field);
            var perLevel = reader.ReadNullableOperand(field);
            var calcTag = reader.ReadTag(field);
            var byLevel = reader.ReadNullableBigNums(field);
            var tail = (LevelTail)reader.ReadByte(field);
            var increment = reader.ReadBigNum(field);
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
            var conditions = new CompiledCondition[reader.ReadLength(field)];
            for (var i = 0; i < conditions.Length; i++)
            {
                conditions[i] = new CompiledCondition(
                    reader.ReadOperand(field),
                    (ComparisonOp)reader.ReadByte(field),
                    reader.ReadOperand(field));
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
            var tags = new GameplayTag[reader.ReadLength(field)];
            for (var i = 0; i < tags.Length; i++) tags[i] = reader.ReadTag(field);
            return tags;
        }
    }
}
