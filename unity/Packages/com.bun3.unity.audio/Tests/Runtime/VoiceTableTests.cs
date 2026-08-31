using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class VoiceTableTests
    {
        private static SoundDef NewDef(int maxInstances = 0, float cooldown = 0f)
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.MaxInstances = maxInstances;
            def.Cooldown = cooldown;
            return def;
        }

        [Test]
        public void Allocate_ReturnsValidSlot()
        {
            var table = new VoiceTable(4);
            Assert.IsTrue(table.TryAllocate(NewDef(), 1f, out var slot, out _));
            Assert.IsTrue(table.IsValid(slot, table.Slots[slot].Generation));
        }

        [Test]
        public void Release_InvalidatesOldGeneration()
        {
            var table = new VoiceTable(4);
            table.TryAllocate(NewDef(), 1f, out var slot, out _);
            var gen = table.Slots[slot].Generation;
            table.Release(slot);
            Assert.IsFalse(table.IsValid(slot, gen));
        }

        [Test]
        public void ReusedSlot_OldHandleStaysInvalid()
        {
            var table = new VoiceTable(1);
            table.TryAllocate(NewDef(), 1f, out var slot, out _);
            var oldGen = table.Slots[slot].Generation;
            table.Release(slot);
            table.TryAllocate(NewDef(), 1f, out var slot2, out _);
            Assert.That(slot2, Is.EqualTo(slot));
            Assert.IsFalse(table.IsValid(slot, oldGen));
            Assert.IsTrue(table.IsValid(slot2, table.Slots[slot2].Generation));
        }
    }
}
