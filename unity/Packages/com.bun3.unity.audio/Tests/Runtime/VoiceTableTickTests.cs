using System.Collections.Generic;
using Bun3.Unity.Audio;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class VoiceTableTickTests
    {
        private static SoundDef NewDef(bool loop = false)
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Loop = loop;
            return def;
        }

        private readonly List<(int Slot, AutoResetUniTaskCompletionSource Completion)> _completed = new();

        [SetUp]
        public void SetUp() => _completed.Clear();

        [Test]
        public void Tick_CompletesVoiceAfterClipLength()
        {
            var table = new VoiceTable(2);
            table.TryAllocate(NewDef(), clipLength: 0.5f, out var slot, out _, out _);
            table.Tick(0.4f, _completed);
            Assert.IsEmpty(_completed);
            table.Tick(0.2f, _completed);
            Assert.That(_completed.Count, Is.EqualTo(1));
            Assert.That(_completed[0].Slot, Is.EqualTo(slot));
            Assert.That(table.Slots[slot].State, Is.EqualTo(VoiceState.Idle));
        }

        [Test]
        public void Tick_LoopingVoiceNeverCompletes()
        {
            var table = new VoiceTable(2);
            table.TryAllocate(NewDef(loop: true), 0.5f, out _, out _, out _);
            table.Tick(10f, _completed);
            Assert.IsEmpty(_completed);
        }

        [Test]
        public void FadeIn_RampsFactorFromZeroToOne()
        {
            var table = new VoiceTable(2);
            table.TryAllocate(NewDef(loop: true), 1f, out var slot, out _, out _);
            table.BeginFadeIn(slot, 1f);
            Assert.That(table.Slots[slot].FadeFactor, Is.EqualTo(0f));
            table.Tick(0.5f, _completed);
            Assert.That(table.Slots[slot].FadeFactor, Is.EqualTo(0.5f).Within(0.001f));
            table.Tick(0.5f, _completed);
            Assert.That(table.Slots[slot].FadeFactor, Is.EqualTo(1f));
            Assert.That(table.Slots[slot].State, Is.EqualTo(VoiceState.Playing));
        }

        [Test]
        public void FadeOut_CompletesAndReleases()
        {
            var table = new VoiceTable(2);
            table.TryAllocate(NewDef(loop: true), 1f, out var slot, out _, out _);
            table.BeginFadeOut(slot, 0.5f);
            table.Tick(0.25f, _completed);
            Assert.That(table.Slots[slot].FadeFactor, Is.EqualTo(0.5f).Within(0.001f));
            table.Tick(0.25f, _completed);
            Assert.That(_completed.Count, Is.EqualTo(1));
            Assert.That(_completed[0].Slot, Is.EqualTo(slot));
            Assert.That(table.Slots[slot].State, Is.EqualTo(VoiceState.Idle));
        }

        [Test]
        public void FadeOut_DuringFadeIn_StartsFromCurrentFactor()
        {
            var table = new VoiceTable(2);
            table.TryAllocate(NewDef(loop: true), 1f, out var slot, out _, out _);
            table.BeginFadeIn(slot, 1f);
            table.Tick(0.5f, _completed);
            table.BeginFadeOut(slot, 1f);
            Assert.That(table.Slots[slot].FadeFrom, Is.EqualTo(0.5f).Within(0.001f));
        }
    }
}
