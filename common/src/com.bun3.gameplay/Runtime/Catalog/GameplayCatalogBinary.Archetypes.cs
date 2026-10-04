// GameplayCatalogBinary partial — archetype record codec.
#nullable enable
using System;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;

namespace Bun3.Gameplay.Catalog
{
    public static partial class GameplayCatalogBinary
    {
        private static void WriteArchetype(BufferWriter writer, CompiledArchetype archetype)
        {
            writer.WriteString(archetype.Name);
            writer.WriteInt32(archetype.AttributeIds.Length);
            for (var i = 0; i < archetype.AttributeIds.Length; i++)
            {
                writer.WriteUInt16(archetype.AttributeIds[i]);
                writer.WriteBigNum(archetype.Bases[i]);
            }

            writer.WriteInt32(archetype.AbilityIds.Length);
            for (var i = 0; i < archetype.AbilityIds.Length; i++)
            {
                writer.WriteInt32(archetype.AbilityIds[i]);
                writer.WriteInt32(archetype.AbilityLevels[i]);
            }
        }

        private static CompiledArchetype ReadArchetype(PayloadReader reader, AttributeRegistry attributes)
        {
            var name = reader.ReadString("archetype name") ?? throw Format("Archetype name must not be null.");

            var attributeCount = reader.ReadLength("archetype attribute count");
            var attributeIds = new ushort[attributeCount];
            var bases = new BigNum[attributeCount];
            for (var i = 0; i < attributeCount; i++)
            {
                attributeIds[i] = reader.ReadUInt16("archetype attribute id");
                bases[i] = reader.ReadBigNum("archetype attribute base");
            }

            var abilityCount = reader.ReadLength("archetype ability count");
            var abilityIds = new int[abilityCount];
            var abilityLevels = new int[abilityCount];
            for (var i = 0; i < abilityCount; i++)
            {
                abilityIds[i] = reader.ReadInt32("archetype ability id");
                abilityLevels[i] = reader.ReadInt32("archetype ability level");
            }

            AttributeLayout layout;
            try
            {
                layout = new AttributeLayout(attributes, attributeIds);
            }
            catch (ArgumentException e)
            {
                throw Format($"Archetype '{name}': {e.Message}");
            }

            return new CompiledArchetype(name, layout, attributeIds, bases, abilityIds, abilityLevels);
        }
    }
}
