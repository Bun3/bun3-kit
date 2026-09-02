using System.Collections;
using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class RuntimeClipsTests
    {
        [UnityTest]
        public IEnumerator RuntimeClips_TakePrecedenceOverDirectClips()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { AudioClip.Create("direct", 4410, 1, 44100, false) };
            var runtime = AudioClip.Create("runtime", 4410, 1, 44100, false);
            def.RuntimeClips = new[] { runtime };
            var h = sys.Play(def);
            Assert.IsTrue(h.IsValid);
            Assert.That(sys.SourceForTest(0).clip, Is.SameAs(runtime));
            yield break;
        }

        [UnityTest]
        public IEnumerator NoClipsAnywhere_ReturnsInvalidHandle()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var def = ScriptableObject.CreateInstance<SoundDef>(); // Clips null, RuntimeClips null
            LogAssert.Expect(LogType.Warning,
                "SoundSystem.Play: def has no loaded clips (assign Clips or preload AddressableClips); returning an invalid handle.");
            var h = sys.Play(def);
            Assert.IsFalse(h.IsValid);
            yield break;
        }
    }
}
