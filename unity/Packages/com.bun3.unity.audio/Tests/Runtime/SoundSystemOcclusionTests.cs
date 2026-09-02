using System.Collections;
using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundSystemOcclusionTests
    {
        private sealed class FakeProvider : IOcclusionProvider
        {
            public float Value;
            public int Calls;
            public float Evaluate(in Vector3 listenerPos, in Vector3 sourcePos)
            {
                Calls++;
                return Value;
            }
        }

        private static SoundDef OccludedDef()
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { AudioClip.Create("occ", 44100, 1, 44100, false) }; // 1 s
            def.Spatial = SpatialMode.Positional;
            def.Occlusion = true;
            return def;
        }

        private static GameObject ListenerGo()
        {
            var go = new GameObject("listener");
            go.AddComponent<AudioListener>();
            return go;
        }

        [UnityTest]
        public IEnumerator OccludedVoice_TargetsOne_VolumeAndFilterFollow()
        {
            var listener = ListenerGo();
            var provider = new FakeProvider { Value = 1f };
            using var sys = new SoundSystem(new SoundSystemConfig
            {
                SfxVoices = 2,
                OcclusionProvider = provider,
                Listener = listener.transform,
                OcclusionSmoothingSeconds = 0.1f,
                OcclusionVolumeAtFull = 0.5f,
            });
            var h = sys.Play(OccludedDef(), new Vector3(0f, 0f, 5f));
            sys.EvaluateOcclusion();
            Assert.That(provider.Calls, Is.GreaterThanOrEqualTo(1));
            sys.Tick(0.2f); // smoothing reaches 1
            var slot = 0;
            Assert.That(sys.Table.Slots[slot].OcclusionCurrent, Is.EqualTo(1f));
            Assert.That(sys.OcclusionVolumeMultiplier(slot), Is.EqualTo(0.5f).Within(0.001f));
            Object.Destroy(listener);
            yield break;
        }

        [UnityTest]
        public IEnumerator NonOccludedDef_NeverEvaluated()
        {
            var listener = ListenerGo();
            var provider = new FakeProvider { Value = 1f };
            using var sys = new SoundSystem(new SoundSystemConfig
            {
                SfxVoices = 2,
                OcclusionProvider = provider,
                Listener = listener.transform,
            });
            var def = OccludedDef();
            def.Occlusion = false;
            sys.Play(def, new Vector3(0f, 0f, 5f));
            sys.EvaluateOcclusion();
            sys.EvaluateOcclusion();
            Assert.That(provider.Calls, Is.EqualTo(0));
            Object.Destroy(listener);
            yield break;
        }

        [UnityTest]
        public IEnumerator RoundRobin_HonorsPerFrameBudget()
        {
            var listener = ListenerGo();
            var provider = new FakeProvider { Value = 0f };
            using var sys = new SoundSystem(new SoundSystemConfig
            {
                SfxVoices = 8,
                OcclusionProvider = provider,
                Listener = listener.transform,
                OcclusionChecksPerFrame = 2,
            });
            for (var i = 0; i < 6; i++)
            {
                sys.Play(OccludedDef(), new Vector3(i, 0f, 5f));
            }
            sys.EvaluateOcclusion();
            Assert.That(provider.Calls, Is.EqualTo(2), "budget caps evaluations per frame");
            sys.EvaluateOcclusion();
            sys.EvaluateOcclusion();
            Assert.That(provider.Calls, Is.EqualTo(6), "cursor advances across frames");
            Object.Destroy(listener);
            yield break;
        }
    }
}
