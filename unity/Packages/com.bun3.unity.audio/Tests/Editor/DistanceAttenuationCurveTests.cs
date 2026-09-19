using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class DistanceAttenuationCurveTests
    {
        [Test]
        public void AuthoredTangentsArePreservedAndSnapshotsRemainStable()
        {
            var profile = ScriptableObject.CreateInstance<DistanceAttenuationProfile>();
            try
            {
                profile.VolumeByDistance = new AnimationCurve(new Keyframe(0, 1, 0, 0), new Keyframe(10, 0, 0, 0));
                var first = profile.GetSnapshot();
                Assert.That(first.Evaluate(2), Is.EqualTo(.896f).Within(.00001f));
                Assert.That(first.Evaluate(10), Is.Zero);
                Assert.That(first.Evaluate(100), Is.Zero);
                profile.VolumeByDistance.MoveKey(1, new Keyframe(5, 0));
                var second = profile.GetSnapshot();
                Assert.That(second, Is.Not.SameAs(first));
                Assert.That(first.Evaluate(5), Is.EqualTo(.5f).Within(.00001f));
                Assert.That(second.Evaluate(5), Is.Zero);
                Assert.That(profile.GetSnapshot(), Is.SameAs(second));
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100; i++) { profile.GetSnapshot(); second.Evaluate(2); }
                Assert.That(System.GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
            }
            finally { Object.DestroyImmediate(profile); }
        }

        [Test]
        public void OldSerializedEmptyCurveMigratesButExplicitlyClearedCurveStaysSilent()
        {
            var profile = ScriptableObject.CreateInstance<DistanceAttenuationProfile>();
            try
            {
                var serialized = new SerializedObject(profile);
                serialized.FindProperty("volumeByDistance").animationCurveValue = new AnimationCurve();
                serialized.FindProperty("curveInitialized").boolValue = false;
                serialized.FindProperty("MinimumDistance").floatValue = 8;
                serialized.FindProperty("MaximumDistance").floatValue = 15;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(profile.GetSnapshot().Evaluate(8), Is.EqualTo(1));
                Assert.That(profile.GetSnapshot().MaximumDistance, Is.EqualTo(15));
                profile.VolumeByDistance = new AnimationCurve();
                Assert.That(profile.GetSnapshot().Evaluate(0), Is.Zero);
                Assert.That(profile.VolumeByDistance.length, Is.Zero);
            }
            finally { Object.DestroyImmediate(profile); }
        }

        [Test]
        public void LegacyMigrationPreservesExistingTuning()
        {
            foreach (float minimum in new[] { 1f, 3f, 8f })
            {
                var migrated = new DistanceAttenuationCurve(DistanceAttenuationProfile.CreateLegacyCurve(minimum, 15, .2f));
                for (int i = 0; i <= 1500; i++)
                {
                    float distance = i / 100f;
                    Assert.That(migrated.Evaluate(distance), Is.EqualTo(DistanceAttenuationProfile.Evaluate(distance, minimum, 15, .2f)).Within(.0005f));
                }
            }
        }

        [Test]
        public void CurveIsBoundedAndEmptyDataIsSilent()
        {
            var curve = new DistanceAttenuationCurve(AnimationCurve.Linear(0, 2, 10, -1));
            Assert.That(curve.Evaluate(0), Is.EqualTo(1));
            Assert.That(curve.Evaluate(9), Is.Zero);
            Assert.That(curve.Evaluate(float.NaN), Is.Zero);
            Assert.That(new DistanceAttenuationCurve(new AnimationCurve()).Evaluate(0), Is.Zero);
        }
    }
}
