using System.Collections.Generic;
using Bun3.Unity.Audio;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class OcclusionSmoothingTests
    {
        private readonly List<(int Slot, AutoResetUniTaskCompletionSource Completion)> _scratch = new();

        private static SoundDef LoopingDef()
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Loop = true;
            def.Occlusion = true;
            return def;
        }

        [Test]
        public void Tick_MovesCurrentTowardTarget()
        {
            var table = new VoiceTable(2, occlusionSmoothingSeconds: 0.2f);
            table.TryAllocate(LoopingDef(), 1f, out var slot, out _, out _);
            table.Slots[slot].OcclusionTarget = 1f;
            table.Tick(0.1f, _scratch);
            Assert.That(table.Slots[slot].OcclusionCurrent, Is.EqualTo(0.5f).Within(0.01f));
            table.Tick(0.2f, _scratch);
            Assert.That(table.Slots[slot].OcclusionCurrent, Is.EqualTo(1f));
        }

        [Test]
        public void Allocate_ResetsOcclusionState()
        {
            var table = new VoiceTable(1, occlusionSmoothingSeconds: 0.2f);
            table.TryAllocate(LoopingDef(), 1f, out var slot, out _, out _);
            table.Slots[slot].OcclusionTarget = 1f;
            table.Tick(1f, _scratch);
            table.Release(slot);
            table.TryAllocate(LoopingDef(), 1f, out var slot2, out _, out _);
            Assert.That(table.Slots[slot2].OcclusionCurrent, Is.EqualTo(0f));
            Assert.That(table.Slots[slot2].OcclusionTarget, Is.EqualTo(0f));
        }

        [Test]
        public void Provider_BinaryContract()
        {
            var provider = new RaycastOcclusionProvider(~0);
            // No colliders in a fresh test scene: line is clear → 0.
            Assert.That(provider.Evaluate(Vector3.zero, new Vector3(0f, 0f, 10f)), Is.EqualTo(0f));
        }
    }
}
