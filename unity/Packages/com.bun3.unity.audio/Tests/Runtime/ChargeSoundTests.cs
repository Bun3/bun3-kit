using System;
using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class ChargeSoundTests
    {
        private const string NullDefWarning = "SoundSystem.PlayCharge: def or its Loop is null; returning an invalid handle.";
        private const string NotLoopingWarning = "SoundSystem.PlayCharge: Loop definition does not loop; the charge ends with its clip.";

        private static SoundDef LoopDef(float pitch = 1f)
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { AudioClip.Create("charge-loop", 44100, 1, 44100, false) }; // 1 s
            def.Loop = true;
            def.Pitch = new FloatRange(pitch, pitch);
            def.MaxInstances = 1;
            return def;
        }

        private static SoundDef OneShotDef(string name, SpatialMode spatial = SpatialMode.None)
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { AudioClip.Create(name, 4410, 1, 44100, false) }; // 0.1 s
            def.Spatial = spatial;
            return def;
        }

        private static ChargeSoundDef Charge(SoundDef loop, SoundDef start = null, SoundDef complete = null, SoundDef cancel = null)
        {
            var def = ScriptableObject.CreateInstance<ChargeSoundDef>();
            def.Loop = loop;
            def.Start = start;
            def.Complete = complete;
            def.Cancel = cancel;
            return def;
        }

        private static int CountVoices(SoundSystem sys, SoundDef def)
        {
            var count = 0;
            for (var i = 0; i < sys.Table.Slots.Length; i++)
            {
                if (sys.Table.Slots[i].State != VoiceState.Idle && ReferenceEquals(sys.Table.Slots[i].Def, def))
                {
                    count++;
                }
            }
            return count;
        }

        [Test]
        public void PlayCharge_StartsLoopAtCurveZeroAndPlaysStart()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var start = OneShotDef("charge-start");
            var charge = sys.PlayCharge(Charge(LoopDef(), start));

            Assert.That(charge.IsValid, Is.True);
            var slot = charge.Loop.SlotIndex;
            Assert.That(sys.Table.Slots[slot].VolumeScale, Is.EqualTo(0f));
            Assert.That(sys.SourceForTest(slot).volume, Is.EqualTo(0f), "Curve-zero volume must be applied before the source starts.");
            Assert.That(sys.SourcePitchForTest(slot), Is.EqualTo(0.667f).Within(1e-4f));
            Assert.That(CountVoices(sys, start), Is.EqualTo(1));
        }

        [Test]
        public void SetProgress_ScalesLoopVolumeAndRolledPitchByCurves()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var def = Charge(LoopDef(pitch: 1.2f));
            def.VolumeByProgress = AnimationCurve.Linear(0f, 0.2f, 1f, 1f);
            def.PitchByProgress = AnimationCurve.Linear(0f, 0.5f, 1f, 1f);
            var charge = sys.PlayCharge(def);
            var slot = charge.Loop.SlotIndex;

            charge.SetProgress(0.5f);
            Assert.That(sys.Table.Slots[slot].VolumeScale, Is.EqualTo(0.6f).Within(1e-4f));
            Assert.That(sys.SourcePitchForTest(slot), Is.EqualTo(1.2f * 0.75f).Within(1e-4f));

            charge.SetProgress(1f);
            charge.SetProgress(0.25f); // progress may decrease
            Assert.That(sys.Table.Slots[slot].VolumeScale, Is.EqualTo(0.4f).Within(1e-4f));
            Assert.That(sys.SourcePitchForTest(slot), Is.EqualTo(1.2f * 0.625f).Within(1e-4f));
        }

        [Test]
        public void SetProgress_ClampsRangeAndTreatsNaNAsZero()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var def = Charge(LoopDef());
            def.VolumeByProgress = AnimationCurve.Linear(0f, 0.2f, 1f, 1f);
            var charge = sys.PlayCharge(def);
            var slot = charge.Loop.SlotIndex;

            charge.SetProgress(float.NaN);
            Assert.That(sys.Table.Slots[slot].VolumeScale, Is.EqualTo(0.2f).Within(1e-4f));
            charge.SetProgress(-3f);
            Assert.That(sys.Table.Slots[slot].VolumeScale, Is.EqualTo(0.2f).Within(1e-4f));
            charge.SetProgress(7f);
            Assert.That(sys.Table.Slots[slot].VolumeScale, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void NullOrEmptyCurves_AreConstantOne()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var def = Charge(LoopDef(pitch: 1.2f));
            def.VolumeByProgress = null;
            def.PitchByProgress = new AnimationCurve();
            var charge = sys.PlayCharge(def);
            var slot = charge.Loop.SlotIndex;

            charge.SetProgress(0.3f);
            Assert.That(sys.Table.Slots[slot].VolumeScale, Is.EqualTo(1f));
            Assert.That(sys.SourcePitchForTest(slot), Is.EqualTo(1.2f).Within(1e-4f));
        }

        [Test]
        public void PitchWithTimescale_KeepsCurvePitchUnderTimescale()
        {
            var previous = Time.timeScale;
            try
            {
                using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4, PitchWithTimescale = true });
                var def = Charge(LoopDef());
                def.PitchByProgress = AnimationCurve.Linear(0f, 0.5f, 1f, 1f);
                var charge = sys.PlayCharge(def);
                var slot = charge.Loop.SlotIndex;

                Time.timeScale = 0.5f;
                sys.Tick(0.01f);
                Assert.That(sys.SourcePitchForTest(slot), Is.EqualTo(0.5f * 0.5f).Within(1e-4f));

                charge.SetProgress(1f);
                Assert.That(sys.SourcePitchForTest(slot), Is.EqualTo(1f * 0.5f).Within(1e-4f));
            }
            finally
            {
                Time.timeScale = previous;
            }
        }

        [Test]
        public void PlayCharge_LoopWithoutClips_ReturnsInvalidAndSkipsStart()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var loop = ScriptableObject.CreateInstance<SoundDef>();
            loop.Loop = true;
            var start = OneShotDef("charge-start");

            var charge = sys.PlayCharge(Charge(loop, start));

            Assert.That(charge.IsValid, Is.False);
            Assert.That(CountVoices(sys, start), Is.EqualTo(0));
        }

        [Test]
        public void PlayCharge_LoopOnCooldown_ReturnsInvalidAndSkipsStart()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var loop = LoopDef();
            loop.Cooldown = 10f;
            var start = OneShotDef("charge-start");
            var def = Charge(loop, start);

            Assert.That(sys.PlayCharge(def).IsValid, Is.True);
            Assert.That(sys.PlayCharge(def).IsValid, Is.False);
            Assert.That(CountVoices(sys, start), Is.EqualTo(1));
        }

        [Test]
        public void PlayCharge_NullDefOrLoop_WarnsAndReturnsInvalid()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            LogAssert.Expect(LogType.Warning, NullDefWarning);
            Assert.That(sys.PlayCharge(null).IsValid, Is.False);
            LogAssert.Expect(LogType.Warning, NullDefWarning);
            Assert.That(sys.PlayCharge(Charge(null)).IsValid, Is.False);
        }

        [Test]
        public void PlayCharge_NonLoopingLoopDef_WarnsAndStillPlays()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var loop = LoopDef();
            loop.Loop = false;
            LogAssert.Expect(LogType.Warning, NotLoopingWarning);
            Assert.That(sys.PlayCharge(Charge(loop)).IsValid, Is.True);
        }

        [Test]
        public void PlayCharge_OnDisposedSystem_Throws()
        {
            var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            sys.Dispose();
            Assert.Throws<ObjectDisposedException>(() => sys.PlayCharge(Charge(LoopDef())));
        }

        [Test]
        public void DefaultHandle_AllCallsAreNoOps()
        {
            var charge = default(ChargeSoundHandle);
            Assert.That(charge.IsValid, Is.False);
            Assert.That(ChargeSoundHandle.Invalid.IsValid, Is.False);
            Assert.DoesNotThrow(() => charge.SetProgress(1f));
        }
    }
}
