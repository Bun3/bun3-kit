using System;
using System.Collections.Generic;
using Bun3.Unity.Audio;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class VoiceTableStealTests
    {
        private static SoundDef NewDef(int maxInstances = 0, float cooldown = 0f, int priority = 128)
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.MaxInstances = maxInstances;
            def.Cooldown = cooldown;
            def.Priority = priority;
            return def;
        }

        private readonly List<(int Slot, uint Generation, AutoResetUniTaskCompletionSource Completion, Action<SoundHandle> Callback)> _scratch = new();

        [Test]
        public void MaxInstances_StealsOldestOfSameDef()
        {
            var table = new VoiceTable(8);
            var def = NewDef(maxInstances: 2);
            table.TryAllocate(def, 1f, out var first, out _, out _);
            table.Tick(0.1f, _scratch);
            table.TryAllocate(def, 1f, out _, out _, out _);
            table.Tick(0.1f, _scratch);
            Assert.IsTrue(table.TryAllocate(def, 1f, out var third, out var stolen, out _));
            Assert.That(stolen, Is.EqualTo(first));
            Assert.That(third, Is.EqualTo(first));
        }

        [Test]
        public void FullTable_StealsGlobalOldest()
        {
            var table = new VoiceTable(2);
            var defA = NewDef();
            var defB = NewDef();
            table.TryAllocate(defA, 1f, out var oldest, out _, out _);
            table.Tick(0.1f, _scratch);
            table.TryAllocate(defA, 1f, out _, out _, out _);
            table.Tick(0.1f, _scratch);
            Assert.IsTrue(table.TryAllocate(defB, 1f, out var slot, out var stolen, out _));
            Assert.That(stolen, Is.EqualTo(oldest));
            Assert.That(slot, Is.EqualTo(oldest));
        }

        [Test]
        public void FullTable_StealsLeastImportantBeforeOldest()
        {
            var table = new VoiceTable(2);
            table.TryAllocate(NewDef(priority: 10), 1f, out var important, out _, out _);
            table.Tick(0.1f, _scratch);
            table.TryAllocate(NewDef(), 1f, out var normal, out _, out _);
            table.Tick(0.1f, _scratch);
            Assert.IsTrue(table.TryAllocate(NewDef(), 1f, out var slot, out var stolen, out _));
            Assert.That(stolen, Is.EqualTo(normal));
            Assert.That(slot, Is.EqualTo(normal));
            Assert.That(table.Slots[important].Priority, Is.EqualTo(10));
        }

        [Test]
        public void FullTable_RefusesPlayLessImportantThanEveryVoice()
        {
            var table = new VoiceTable(1);
            table.TryAllocate(NewDef(priority: 10), 1f, out var slot, out _, out _);
            var generation = table.Slots[slot].Generation;
            var cooled = NewDef(cooldown: 1f, priority: 200);
            Assert.IsFalse(table.TryAllocate(cooled, 1f, out _, out var stolen, out _));
            Assert.That(stolen, Is.EqualTo(-1));
            Assert.IsTrue(table.IsValid(slot, generation));
            table.Release(slot);
            Assert.IsTrue(table.TryAllocate(cooled, 1f, out _, out _, out _), "a refused play must not start its cooldown");
        }

        [Test]
        public void Cooldown_BlocksRetrigger_ThenAllows()
        {
            var table = new VoiceTable(4);
            var def = NewDef(cooldown: 0.5f);
            Assert.IsTrue(table.TryAllocate(def, 1f, out _, out _, out _));
            Assert.IsFalse(table.TryAllocate(def, 1f, out _, out _, out _));
            table.Tick(0.6f, _scratch);
            Assert.IsTrue(table.TryAllocate(def, 1f, out _, out _, out _));
        }
    }
}
