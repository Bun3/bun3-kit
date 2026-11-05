using System.Collections.Generic;
using Bun3.Unity.Audio.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundDefOverridesTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void Cleanup()
        {
            foreach (var created in _created)
            {
                Undo.ClearUndo(created);
                Object.DestroyImmediate(created);
            }
            _created.Clear();
        }

        [Test]
        public void Override_CopiesProfileValueThenFlags()
        {
            var profile = Create<SoundPlaybackProfile>();
            profile.Volume = new FloatRange(0.2f, 0.2f);
            var def = Create<SoundDef>();
            def.PlaybackProfile = profile;
            def.Volume = new FloatRange(0.7f, 0.7f);

            SoundDefOverrides.Override(new SerializedObject(def), SoundDefField.Volume);

            Assert.That(def.Volume.Min, Is.EqualTo(0.2f));
            Assert.That(def.IsOverridden(SoundDefField.Volume), Is.True);
            Assert.That(def.EffectiveVolume.Min, Is.EqualTo(0.2f));
        }

        [Test]
        public void Revert_ClearsFlagAndKeepsLocalValue()
        {
            var profile = Create<SoundPlaybackProfile>();
            profile.Volume = new FloatRange(0.2f, 0.2f);
            var def = Create<SoundDef>();
            def.PlaybackProfile = profile;
            def.Volume = new FloatRange(0.7f, 0.7f);
            var serialized = new SerializedObject(def);
            serialized.FindProperty("_overrides").intValue = (int)SoundDefField.Volume;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            SoundDefOverrides.Revert(serialized, SoundDefField.Volume);

            Assert.That(def.IsOverridden(SoundDefField.Volume), Is.False);
            Assert.That(def.Volume.Min, Is.EqualTo(0.7f));
            Assert.That(def.EffectiveVolume.Min, Is.EqualTo(0.2f));
        }

        [Test]
        public void UseLocal_CopiesWholeGroupAndClearsProfile()
        {
            var profile = Create<SoundConcurrencyProfile>();
            profile.MaxInstances = 3;
            profile.Cooldown = 0.5f;
            profile.Priority = 20;
            var def = Create<SoundDef>();
            def.ConcurrencyProfile = profile;
            def.MaxInstances = 9;
            def.Cooldown = 2f;
            def.Priority = 200;
            var serialized = new SerializedObject(def);
            serialized.FindProperty("_overrides").intValue = (int)SoundDefField.Volume;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            SoundDefOverrides.UseLocal(serialized, SoundProfileGroup.Concurrency);

            Assert.That(def.ConcurrencyProfile, Is.Null);
            Assert.That(def.MaxInstances, Is.EqualTo(3));
            Assert.That(def.Cooldown, Is.EqualTo(0.5f));
            Assert.That(def.Priority, Is.EqualTo(20));
            Assert.That(def.IsOverridden(SoundDefField.MaxInstances | SoundDefField.Cooldown | SoundDefField.Priority), Is.False);
            Assert.That(def.IsOverridden(SoundDefField.Volume), Is.True, "Other groups keep their flags.");
        }

        [Test]
        public void UseLocal_KeepsOverriddenLocalValue()
        {
            var profile = Create<SoundConcurrencyProfile>();
            profile.Priority = 20;
            var def = Create<SoundDef>();
            def.ConcurrencyProfile = profile;
            def.Priority = 200;
            var serialized = new SerializedObject(def);
            serialized.FindProperty("_overrides").intValue = (int)SoundDefField.Priority;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            SoundDefOverrides.UseLocal(serialized, SoundProfileGroup.Concurrency);

            Assert.That(def.EffectivePriority, Is.EqualTo(200), "Detaching must not change what plays.");
            Assert.That(def.IsOverridden(SoundDefField.Priority), Is.False);
        }

        [Test]
        public void Override_WithoutProfile_ChangesNothing()
        {
            var def = Create<SoundDef>();
            def.Volume = new FloatRange(0.7f, 0.7f);

            SoundDefOverrides.Override(new SerializedObject(def), SoundDefField.Volume);

            Assert.That(def.Volume.Min, Is.EqualTo(0.7f));
            Assert.That(def.IsOverridden(SoundDefField.Volume), Is.False);
        }

        [Test]
        public void OverrideAcoustic_CopiesProfileSetting()
        {
            var profile = Create<SoundAcousticProfile>();
            profile.Settings.MaxDistance = 50f;
            var def = Create<SoundDef>();
            def.Acoustics.Profile = profile;
            def.Acoustics.Local = SoundAcousticSettings.Default;

            SoundDefOverrides.OverrideAcoustic(new SerializedObject(def).FindProperty("_acoustics"), SoundAcousticField.MaxDistance);

            Assert.That(def.Acoustics.Local.MaxDistance, Is.EqualTo(50f));
            Assert.That(def.Acoustics.Overrides, Is.EqualTo(SoundAcousticField.MaxDistance));
            Assert.That(def.Acoustics.Resolve().MaxDistance, Is.EqualTo(50f));
        }

        [Test]
        public void UseLocalAcoustics_CopiesSettingsAndClearsProfile()
        {
            var profile = Create<SoundAcousticProfile>();
            profile.Settings.DistanceAttenuation = false;
            profile.Settings.MinDistance = 4f;
            profile.Settings.MaxDistance = 50f;
            profile.Settings.MonoDistance = 2f;
            var def = Create<SoundDef>();
            def.Acoustics.Profile = profile;
            var local = SoundAcousticSettings.Default;
            local.MonoDistance = 8f;
            def.Acoustics.Local = local;
            def.Acoustics.Overrides = SoundAcousticField.MonoDistance;

            SoundDefOverrides.UseLocalAcoustics(new SerializedObject(def).FindProperty("_acoustics"));

            Assert.That(def.Acoustics.Profile, Is.Null);
            Assert.That(def.Acoustics.Overrides, Is.EqualTo(SoundAcousticField.None));
            Assert.That(def.Acoustics.Local.DistanceAttenuation, Is.False);
            Assert.That(def.Acoustics.Local.MinDistance, Is.EqualTo(4f));
            Assert.That(def.Acoustics.Local.MaxDistance, Is.EqualTo(50f));
            Assert.That(def.Acoustics.Local.MonoDistance, Is.EqualTo(8f), "An overridden field keeps its local value.");
        }

        [Test]
        public void UseLocalAcoustics_MultiTarget_CopiesEachTarget()
        {
            var profile = Create<SoundAcousticProfile>();
            profile.Settings.MinDistance = 4f;
            profile.Settings.MaxDistance = 50f;
            var first = Create<SoundSpatialProfile>();
            var second = Create<SoundSpatialProfile>();
            first.Acoustics.Profile = profile;
            second.Acoustics.Profile = profile;
            var local = SoundAcousticSettings.Default;
            local.MaxDistance = 25f;
            second.Acoustics.Local = local;
            second.Acoustics.Overrides = SoundAcousticField.MaxDistance;
            var serialized = new SerializedObject(new Object[] { first, second });

            SoundDefOverrides.UseLocalAcoustics(serialized.FindProperty("_acoustics"));

            Assert.That(first.Acoustics.Profile, Is.Null);
            Assert.That(second.Acoustics.Profile, Is.Null);
            Assert.That(first.Acoustics.Local.MaxDistance, Is.EqualTo(50f));
            Assert.That(second.Acoustics.Local.MaxDistance, Is.EqualTo(25f), "Each target keeps its own overridden value.");
            Assert.That(second.Acoustics.Local.MinDistance, Is.EqualTo(4f));
            Assert.That(second.Acoustics.Overrides, Is.EqualTo(SoundAcousticField.None));
        }

        private T Create<T>() where T : ScriptableObject
        {
            var created = ScriptableObject.CreateInstance<T>();
            _created.Add(created);
            return created;
        }
    }
}
