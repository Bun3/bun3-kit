using System;
using System.Text.RegularExpressions;
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
        private const string NullLoopWarning = "SoundSystem.PlayCharge: loop definition is null; returning an invalid handle.";
        private const string NotLoopingWarning = "SoundSystem.PlayCharge: Loop definition does not loop; the charge ends with its clip.";
        private static readonly Regex UninitializedWarning = new("before SoundSystem.Initialize");

        private SoundCatalog _catalog;

        [TearDown]
        public void TearDown()
        {
            SoundSystem.Shutdown();
            if (_catalog != null) Object.DestroyImmediate(_catalog);
        }

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

        private static SoundDef EmptyDef() => ScriptableObject.CreateInstance<SoundDef>();

        private static ChargeSoundHandle Play(SoundSystem sys, SoundDef loop, SoundDef start = null, SoundDef complete = null, SoundDef cancel = null)
            => sys.PlayChargeCore(loop, start, complete, cancel, Vector3.zero, null);

        private SoundSystem Initialize(params (string Key, SoundDef Def)[] entries)
        {
            var mapped = new SoundCatalogEntry[entries.Length];
            for (var i = 0; i < entries.Length; i++) mapped[i] = new SoundCatalogEntry(entries[i].Key, entries[i].Def);
            _catalog = ScriptableObject.CreateInstance<SoundCatalog>();
            _catalog.SetEntries(mapped);
            SoundSystem.Initialize(_catalog, new SoundSystemConfig { SfxVoices = 8 });
            return SoundSystem.Current;
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
            var charge = Play(sys, LoopDef(), start);

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
            var loop = LoopDef(pitch: 1.2f);
            loop.Charge.VolumeByProgress = AnimationCurve.Linear(0f, 0.2f, 1f, 1f);
            loop.Charge.PitchByProgress = AnimationCurve.Linear(0f, 0.5f, 1f, 1f);
            var charge = Play(sys, loop);
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
            var loop = LoopDef();
            loop.Charge.VolumeByProgress = AnimationCurve.Linear(0f, 0.2f, 1f, 1f);
            var charge = Play(sys, loop);
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
            var loop = LoopDef(pitch: 1.2f);
            loop.Charge.VolumeByProgress = null;
            loop.Charge.PitchByProgress = new AnimationCurve();
            var charge = Play(sys, loop);
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
                var loop = LoopDef();
                loop.Charge.PitchByProgress = AnimationCurve.Linear(0f, 0.5f, 1f, 1f);
                var charge = Play(sys, loop);
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
            var loop = EmptyDef();
            loop.Loop = true;
            var start = OneShotDef("charge-start");

            var charge = Play(sys, loop, start);

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

            Assert.That(Play(sys, loop, start).IsValid, Is.True);
            Assert.That(Play(sys, loop, start).IsValid, Is.False);
            Assert.That(CountVoices(sys, start), Is.EqualTo(1));
        }

        [Test]
        public void PlayCharge_NullLoop_WarnsAndReturnsInvalid()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            LogAssert.Expect(LogType.Warning, NullLoopWarning);
            Assert.That(Play(sys, null).IsValid, Is.False);
        }

        [Test]
        public void PlayCharge_NonLoopingLoopDef_WarnsAndStillPlays()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var loop = LoopDef();
            loop.Loop = false;
            LogAssert.Expect(LogType.Warning, NotLoopingWarning);
            Assert.That(Play(sys, loop).IsValid, Is.True);
        }

        [Test]
        public void PlayCharge_OnDisposedSystem_Throws()
        {
            var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            sys.Dispose();
            Assert.Throws<ObjectDisposedException>(() => Play(sys, LoopDef()));
        }

        [Test]
        public void DefaultHandle_AllCallsAreNoOps()
        {
            var charge = default(ChargeSoundHandle);
            Assert.That(charge.IsValid, Is.False);
            Assert.That(ChargeSoundHandle.Invalid.IsValid, Is.False);
            Assert.DoesNotThrow(() => charge.SetProgress(1f));
            Assert.DoesNotThrow(() => charge.Stop());
            Assert.That(charge.Complete().IsValid, Is.False);
            Assert.That(charge.Cancel().IsValid, Is.False);
        }

        [Test]
        public void Complete_FadesLoopAndPlaysCompleteAtLoopPosition()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var complete = OneShotDef("charge-complete", SpatialMode.Positional);
            var loop = LoopDef();
            var at = new Vector3(3f, 0f, 4f);
            var charge = sys.PlayChargeCore(loop, null, complete, null, at, null);
            var loopSlot = charge.Loop.SlotIndex;

            var done = charge.Complete();

            Assert.That(done.IsValid, Is.True);
            Assert.That(charge.IsValid, Is.False);
            Assert.That(sys.Table.Slots[loopSlot].State, Is.EqualTo(VoiceState.FadingOut));
            Assert.That(sys.SourceForTest(done.SlotIndex).transform.position, Is.EqualTo(at));
            sys.Tick(loop.Charge.CompleteFadeOut + 0.01f);
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
                var charge = sys.PlayChargeCore(LoopDef(), null, null, cancel, target.position, target);

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
            var charge = sys.PlayChargeCore(LoopDef(), null, complete, null, target.position, target);
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
            var loop = LoopDef();
            var charge = Play(sys, loop, complete: complete, cancel: cancel);
            var loopSlot = charge.Loop.SlotIndex;
            charge.SetProgress(0.5f);

            Assert.That(charge.Complete().IsValid, Is.True);
            Assert.That(charge.Complete().IsValid, Is.False);
            Assert.That(charge.Cancel().IsValid, Is.False);
            charge.SetProgress(1f);

            Assert.That(CountVoices(sys, complete), Is.EqualTo(1));
            Assert.That(CountVoices(sys, cancel), Is.EqualTo(0));
            Assert.That(sys.Table.Slots[loopSlot].VolumeScale, Is.EqualTo(0.5f).Within(1e-4f), "SetProgress after Complete must not touch the fading loop.");
            sys.Tick(loop.Charge.CompleteFadeOut + 0.01f);
            Assert.That(charge.Loop.IsValid, Is.False, "A second end call must not restart the loop fade with CancelFadeOut.");
        }

        [Test]
        public void Cancel_WithoutCancelSound_StopsLoopAndReturnsInvalid()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var loop = LoopDef();
            var charge = Play(sys, loop);
            var loopSlot = charge.Loop.SlotIndex;

            Assert.That(charge.Cancel().IsValid, Is.False);
            Assert.That(sys.Table.Slots[loopSlot].State, Is.EqualTo(VoiceState.FadingOut));
            sys.Tick(loop.Charge.CancelFadeOut + 0.01f);
            Assert.That(charge.Loop.IsValid, Is.False);
        }

        [Test]
        public void Replay_StealsPreviousLoop_OldHandleNoOps()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 8 });
            var loop = LoopDef();
            var complete = OneShotDef("charge-complete");
            var first = Play(sys, loop, complete: complete);
            first.Cancel();

            var second = Play(sys, loop, complete: complete);

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
            var charge = Play(sys, LoopDef(), complete: complete);

            sys.Play(OneShotDef("other")); // full pool: steals the loop

            Assert.That(charge.IsValid, Is.False);
            Assert.That(charge.Complete().IsValid, Is.False);
            Assert.That(CountVoices(sys, complete), Is.EqualTo(0));
        }

        [Test]
        public void DisposedSystem_ChargeCallsAreNoOps()
        {
            var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var charge = Play(sys, LoopDef(), complete: OneShotDef("charge-complete"));
            sys.Dispose();

            Assert.That(charge.IsValid, Is.False);
            Assert.DoesNotThrow(() => charge.SetProgress(1f));
            Assert.That(charge.Complete().IsValid, Is.False);
            Assert.That(charge.Cancel().IsValid, Is.False);
        }

        [Test]
        public void PlayCharge_ResolvesPartsByKeySuffix()
        {
            SoundDef loop = LoopDef(), start = OneShotDef("start"), complete = OneShotDef("complete"), cancel = OneShotDef("cancel");
            var sys = Initialize(("x.hold", loop), ("x.hold.start", start), ("x.hold.complete", complete), ("x.hold.cancel", cancel));

            var charge = SoundSystem.PlayCharge("x.hold");

            Assert.That(charge.IsValid, Is.True);
            Assert.That(CountVoices(sys, loop), Is.EqualTo(1));
            Assert.That(CountVoices(sys, start), Is.EqualTo(1));
            Assert.That(charge.Complete().IsValid, Is.True);
            Assert.That(CountVoices(sys, complete), Is.EqualTo(1));
            Assert.That(CountVoices(sys, cancel), Is.EqualTo(0));
        }

        [Test]
        public void PlayCharge_FallsBackPerPart()
        {
            SoundDef xLoop = LoopDef(), xStart = EmptyDef(), xComplete = OneShotDef("x-complete");
            SoundDef tLoop = LoopDef(), tStart = OneShotDef("t-start"), tComplete = OneShotDef("t-complete");
            var yStart = OneShotDef("y-start");
            var sys = Initialize(
                ("x.hold", xLoop), ("x.hold.start", xStart), ("x.hold.complete", xComplete),
                ("t.hold", tLoop), ("t.hold.start", tStart), ("t.hold.complete", tComplete),
                ("y.hold.start", yStart));

            var charge = SoundSystem.PlayCharge("x.hold", "t.hold");

            Assert.That(CountVoices(sys, xLoop), Is.EqualTo(1));
            Assert.That(CountVoices(sys, tLoop), Is.EqualTo(0));
            Assert.That(CountVoices(sys, tStart), Is.EqualTo(1), "An empty start part falls back to the fallback key's start.");
            charge.Complete();
            Assert.That(CountVoices(sys, xComplete), Is.EqualTo(1));
            Assert.That(CountVoices(sys, tComplete), Is.EqualTo(0));

            SoundSystem.PlayCharge("y.hold", "t.hold");

            Assert.That(CountVoices(sys, tLoop), Is.EqualTo(1), "A missing loop falls back to the fallback key's loop.");
            Assert.That(CountVoices(sys, yStart), Is.EqualTo(1), "The key's own parts still win over the fallback's.");
            Assert.That(CountVoices(sys, tStart), Is.EqualTo(1));
        }

        [Test]
        public void PlayCharge_EmptyOrUnknownKey_ReturnsInvalidSilently()
        {
            var start = OneShotDef("start");
            var sys = Initialize(("empty.hold", EmptyDef()), ("empty.hold.start", start));

            Assert.That(SoundSystem.PlayCharge("empty.hold", "missing.hold").IsValid, Is.False);
            Assert.That(SoundSystem.PlayCharge("missing.hold").IsValid, Is.False);
            Assert.That(SoundSystem.PlayCharge(null).IsValid, Is.False);
            Assert.That(SoundSystem.PlayChargeAt("empty.hold", null, Vector3.one).IsValid, Is.False);
            Assert.That(SoundSystem.PlayCharge("missing.hold", "empty.hold", null).IsValid, Is.False);
            Assert.That(CountVoices(sys, start), Is.EqualTo(0), "Parts never play without a loop.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void PlayCharge_BeforeInitialize_WarnsOnceAndReturnsInvalid()
        {
            LogAssert.Expect(LogType.Warning, UninitializedWarning);
            Assert.That(SoundSystem.PlayCharge("x.hold").IsValid, Is.False);
            Assert.That(SoundSystem.PlayCharge("x.hold", "t.hold").IsValid, Is.False);
            Assert.That(SoundSystem.PlayChargeAt("x.hold", "t.hold", Vector3.one).IsValid, Is.False);
            Assert.That(SoundSystem.PlayCharge("x.hold", "t.hold", null).IsValid, Is.False);
            LogAssert.NoUnexpectedReceived();
            Assert.That(SoundSystem.Current, Is.Null);
        }

        [Test]
        public void PlayCharge_IsAlwaysTwoD()
        {
            var loop = LoopDef();
            loop.Spatial = SpatialMode.Positional;
            var complete = OneShotDef("complete", SpatialMode.Positional);
            var sys = Initialize(("x.hold", loop), ("x.hold.complete", complete));

            var charge = SoundSystem.PlayCharge("x.hold");
            var done = charge.Complete();

            Assert.That(sys.SourceForTest(charge.Loop.SlotIndex).spatialBlend, Is.EqualTo(0f));
            Assert.That(sys.SourceForTest(done.SlotIndex).spatialBlend, Is.EqualTo(0f));
        }

        [Test]
        public void Stop_EndsLoopWithoutStinger()
        {
            SoundDef loop = LoopDef(), complete = OneShotDef("complete"), cancel = OneShotDef("cancel");
            var sys = Initialize(("x.hold", loop), ("x.hold.complete", complete), ("x.hold.cancel", cancel));
            var charge = SoundSystem.PlayCharge("x.hold");

            charge.Stop();

            Assert.That(charge.IsValid, Is.False);
            Assert.That(charge.Complete().IsValid, Is.False);
            Assert.That(charge.Cancel().IsValid, Is.False);
            Assert.That(CountVoices(sys, complete) + CountVoices(sys, cancel), Is.EqualTo(0));
            sys.Tick(0.001f);
            Assert.That(charge.Loop.IsValid, Is.False, "Stop must release the loop on the next tick, without a fade.");
        }

        [Test]
        public void SetProgress_DoesNotAllocate()
        {
            var sys = Initialize(
                ("x.hold", LoopDef()), ("x.hold.start", OneShotDef("start")),
                ("t.hold", LoopDef()), ("t.hold.complete", OneShotDef("complete")));
            TestDelegate cycle = () =>
            {
                var charge = SoundSystem.PlayCharge("x.hold", "t.hold");
                charge.SetProgress(0.5f);
                sys.Tick(0.02f);
                charge.SetProgress(1f);
                charge.Complete();
                sys.Tick(0.2f);
            };
            cycle(); // warm every lazy path and the delegate's interned literals once

            Assert.That(() => GC.KeepAlive(new byte[1024]), Is.AllocatingGCMemory(),
                "GC allocation recorder must detect a known allocation before measuring this path.");
            Assert.That(cycle, Is.Not.AllocatingGCMemory());
            Assert.That(SoundSystem.PlayCharge("x.hold", "t.hold").IsValid, Is.True, "The measured path must actually start a charge.");
        }
    }
}
