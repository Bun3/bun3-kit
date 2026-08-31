using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class VoiceTableStealTests
    {
        private static SoundDef NewDef(int maxInstances = 0, float cooldown = 0f)
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.MaxInstances = maxInstances;
            def.Cooldown = cooldown;
            return def;
        }

        [Test]
        public void MaxInstances_StealsOldestOfSameDef()
        {
            var table = new VoiceTable(8);
            var def = NewDef(maxInstances: 2);
            table.TryAllocate(def, 1f, out var first, out _);
            table.AdvanceTime(0.1f);
            table.TryAllocate(def, 1f, out _, out _);
            table.AdvanceTime(0.1f);
            Assert.IsTrue(table.TryAllocate(def, 1f, out var third, out var stolen));
            Assert.That(stolen, Is.EqualTo(first));
            Assert.That(third, Is.EqualTo(first));
        }

        [Test]
        public void FullTable_StealsGlobalOldest()
        {
            var table = new VoiceTable(2);
            var defA = NewDef();
            var defB = NewDef();
            table.TryAllocate(defA, 1f, out var oldest, out _);
            table.AdvanceTime(0.1f);
            table.TryAllocate(defA, 1f, out _, out _);
            table.AdvanceTime(0.1f);
            Assert.IsTrue(table.TryAllocate(defB, 1f, out var slot, out var stolen));
            Assert.That(stolen, Is.EqualTo(oldest));
            Assert.That(slot, Is.EqualTo(oldest));
        }

        [Test]
        public void Cooldown_BlocksRetrigger_ThenAllows()
        {
            var table = new VoiceTable(4);
            var def = NewDef(cooldown: 0.5f);
            Assert.IsTrue(table.TryAllocate(def, 1f, out _, out _));
            Assert.IsFalse(table.TryAllocate(def, 1f, out _, out _));
            table.AdvanceTime(0.6f);
            Assert.IsTrue(table.TryAllocate(def, 1f, out _, out _));
        }
    }
}
