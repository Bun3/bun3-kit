using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using NUnit.Framework;
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
                Assert.That(() => System.GC.KeepAlive(new byte[1024]),
                    UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                    "GC allocation recorder must detect a known allocation before measuring this path.");
                Assert.That(() =>
                {
                    for (int i = 0; i < 100; i++) { profile.GetSnapshot(); second.Evaluate(2); }
                }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
            }
            finally { Object.DestroyImmediate(profile); }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        public void InPlaceKeyAndWrapEditsRefreshSnapshotWithoutMutatingPriorSnapshot(int edit)
        {
            var profile = ScriptableObject.CreateInstance<DistanceAttenuationProfile>();
            try
            {
                profile.VolumeByDistance = new AnimationCurve(new Keyframe(0, 1, 0, 0), new Keyframe(10, 0, 0, 0));
                var first = profile.GetSnapshot();
                var curve = profile.VolumeByDistance;
                var key = curve[0];
                switch (edit)
                {
                    case 0: key.time = 1; break;
                    case 1: key.value = .5f; break;
                    case 2: key.inTangent = .3f; break;
                    case 3: key.outTangent = -.3f; break;
                    case 4: key.inWeight = .2f; break;
                    case 5: key.outWeight = .2f; break;
                    case 6: key.weightedMode = WeightedMode.Both; break;
                    case 7: curve.preWrapMode = WrapMode.PingPong; break;
                    case 8: curve.postWrapMode = WrapMode.Loop; break;
                }
                if (edit < 7) curve.MoveKey(0, key);
                var second = profile.GetSnapshot();
                Assert.That(second, Is.Not.SameAs(first));
                Assert.That(profile.GetSnapshot(), Is.SameAs(second));
                Assert.That(first.Evaluate(2), Is.EqualTo(.896f).Within(.00001f));
            }
            finally { Object.DestroyImmediate(profile); }
        }

        [Test]
        public void NewProfilesUseVoiceDefaultsAndExplicitlyClearedCurvesStaySilent()
        {
            var profile = ScriptableObject.CreateInstance<DistanceAttenuationProfile>();
            try
            {
                Assert.That(profile.GetSnapshot().Evaluate(1), Is.EqualTo(1));
                Assert.That(profile.GetSnapshot().MaximumDistance, Is.EqualTo(15));
                profile.VolumeByDistance = new AnimationCurve();
                Assert.That(profile.GetSnapshot().Evaluate(0), Is.Zero);
                Assert.That(profile.VolumeByDistance.length, Is.Zero);
            }
            finally { Object.DestroyImmediate(profile); }
        }

        [Test]
        public void InverseDistanceCurveMatchesScalarEvaluation()
        {
            foreach (float minimum in new[] { 1f, 3f, 8f })
            {
                var curve = new DistanceAttenuationCurve(DistanceAttenuationProfile.CreateInverseDistanceCurve(minimum, 15, .2f));
                for (int i = 0; i <= 1500; i++)
                {
                    float distance = i / 100f;
                    Assert.That(curve.Evaluate(distance), Is.EqualTo(DistanceAttenuationProfile.Evaluate(distance, minimum, 15, .2f)).Within(.0005f));
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
