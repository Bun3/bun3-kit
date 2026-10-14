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
            Assert.That(charge.Complete().IsValid, Is.False);
            Assert.That(charge.Cancel().IsValid, Is.False);
        }

        [Test]
        public void Complete_FadesLoopAndPlaysCompleteAtLoopPosition()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var complete = OneShotDef("charge-complete", SpatialMode.Positional);
            var def = Charge(LoopDef(), complete: complete);
            var at = new Vector3(3f, 0f, 4f);
            var charge = sys.PlayCharge(def, at);
            var loopSlot = charge.Loop.SlotIndex;

            var done = charge.Complete();

            Assert.That(done.IsValid, Is.True);
            Assert.That(charge.IsValid, Is.False);
            Assert.That(sys.Table.Slots[loopSlot].State, Is.EqualTo(VoiceState.FadingOut));
            Assert.That(sys.SourceForTest(done.SlotIndex).transform.position, Is.EqualTo(at));
            sys.Tick(def.CompleteFadeOut + 0.01f);
            Assert.That(charge.Loop.IsValid, Is.False, "Loop must be released when its CompleteFadeOut finishes.");
        }

        [Test]
        public void Cancel_FollowingCharge_PlaysCancelTrackingSameTransform()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var cancel = OneShotDef("charge-cancel", SpatialMode.Follow);
            var target = new GameObject("charge-target").transform;
            try
            {
                target.position = new Vector3(1f, 2f, 3f);
                var charge = sys.PlayCharge(Charge(LoopDef(), cancel: cancel), target);

                var cancelled = charge.Cancel();

                Assert.That(cancelled.IsValid, Is.True);
                Assert.That(sys.Table.Slots[cancelled.SlotIndex].Follow, Is.SameAs(target));
                target.position = new Vector3(5f, 5f, 5f);
                sys.Tick(0.01f);
                Assert.That(sys.SourceForTest(cancelled.SlotIndex).transform.position, Is.EqualTo(new Vector3(5f, 5f, 5f)));
            }
            finally
            {
                Object.DestroyImmediate(target.gameObject);
            }
        }

        [Test]
        public void Complete_FollowDestroyed_PlaysAtLastTrackedPosition()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var complete = OneShotDef("charge-complete", SpatialMode.Positional);
            var target = new GameObject("charge-target").transform;
            target.position = new Vector3(1f, 2f, 3f);
            var charge = sys.PlayCharge(Charge(LoopDef(), complete: complete), target);
            sys.Tick(0.01f);
            Object.DestroyImmediate(target.gameObject);

            var done = charge.Complete();

            Assert.That(done.IsValid, Is.True);
            Assert.That(sys.Table.Slots[done.SlotIndex].Follow == null, Is.True);
            Assert.That(sys.SourceForTest(done.SlotIndex).transform.position, Is.EqualTo(new Vector3(1f, 2f, 3f)));
        }

        [Test]
        public void EndTwice_PlaysOneStingerAndKeepsFirstFade()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 8 });
            var complete = OneShotDef("charge-complete");
            var cancel = OneShotDef("charge-cancel");
            var def = Charge(LoopDef(), complete: complete, cancel: cancel);
            var charge = sys.PlayCharge(def);
            var loopSlot = charge.Loop.SlotIndex;
            charge.SetProgress(0.5f);

            Assert.That(charge.Complete().IsValid, Is.True);
            Assert.That(charge.Complete().IsValid, Is.False);
            Assert.That(charge.Cancel().IsValid, Is.False);
            charge.SetProgress(1f);

            Assert.That(CountVoices(sys, complete), Is.EqualTo(1));
            Assert.That(CountVoices(sys, cancel), Is.EqualTo(0));
            Assert.That(sys.Table.Slots[loopSlot].VolumeScale, Is.EqualTo(0.5f).Within(1e-4f), "SetProgress after Complete must not touch the fading loop.");
            sys.Tick(def.CompleteFadeOut + 0.01f);
            Assert.That(charge.Loop.IsValid, Is.False, "A second end call must not restart the loop fade with CancelFadeOut.");
        }

        [Test]
        public void Cancel_WithoutCancelSound_StopsLoopAndReturnsInvalid()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var def = Charge(LoopDef());
            var charge = sys.PlayCharge(def);
            var loopSlot = charge.Loop.SlotIndex;

            Assert.That(charge.Cancel().IsValid, Is.False);
            Assert.That(sys.Table.Slots[loopSlot].State, Is.EqualTo(VoiceState.FadingOut));
            sys.Tick(def.CancelFadeOut + 0.01f);
            Assert.That(charge.Loop.IsValid, Is.False);
        }

        [Test]
        public void Replay_StealsPreviousLoop_OldHandleNoOps()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 8 });
            var loop = LoopDef();
            var complete = OneShotDef("charge-complete");
            var def = Charge(loop, complete: complete);
            var first = sys.PlayCharge(def);
            first.Cancel();

            var second = sys.PlayCharge(def);

            Assert.That(first.IsValid, Is.False);
            Assert.That(second.IsValid, Is.True);
            Assert.That(first.Complete().IsValid, Is.False);
            first.SetProgress(1f);
            Assert.That(sys.Table.Slots[second.Loop.SlotIndex].VolumeScale, Is.EqualTo(0f));
            Assert.That(CountVoices(sys, loop), Is.EqualTo(1));
            Assert.That(CountVoices(sys, complete), Is.EqualTo(0));
        }

        [Test]
        public void LoopStolenByPoolPressure_CompletePlaysNothing()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 1 });
            var complete = OneShotDef("charge-complete");
            var charge = sys.PlayCharge(Charge(LoopDef(), complete: complete));

            sys.Play(OneShotDef("other")); // full pool: steals the loop

            Assert.That(charge.IsValid, Is.False);
            Assert.That(charge.Complete().IsValid, Is.False);
            Assert.That(CountVoices(sys, complete), Is.EqualTo(0));
        }

        [Test]
        public void DisposedSystem_ChargeCallsAreNoOps()
        {
            var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var charge = sys.PlayCharge(Charge(LoopDef(), complete: OneShotDef("charge-complete")));
            sys.Dispose();

            Assert.That(charge.IsValid, Is.False);
            Assert.DoesNotThrow(() => charge.SetProgress(1f));
            Assert.That(charge.Complete().IsValid, Is.False);
            Assert.That(charge.Cancel().IsValid, Is.False);
        }

        [Test]
        public void ChargeCycle_DoesNotAllocate()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 8 });
            var def = Charge(LoopDef(), OneShotDef("charge-start"), OneShotDef("charge-complete"));
            sys.PlayCharge(def).Complete(); // warm every lazy path once
            sys.Tick(0.2f);

            Assert.That(() => GC.KeepAlive(new byte[1024]), Is.AllocatingGCMemory(),
                "GC allocation recorder must detect a known allocation before measuring this path.");
            Assert.That(() =>
            {
                var charge = sys.PlayCharge(def);
                charge.SetProgress(0.5f);
                sys.Tick(0.02f);
                charge.SetProgress(1f);
                charge.Complete();
                sys.Tick(0.2f);
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
