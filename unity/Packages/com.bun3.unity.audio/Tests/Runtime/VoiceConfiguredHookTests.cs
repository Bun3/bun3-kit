using System.Collections;
using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class VoiceConfiguredHookTests
    {
        private static SoundDef ClipDef()
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { AudioClip.Create("hook", 4410, 1, 44100, false) };
            return def;
        }

        [UnityTest]
        public IEnumerator Hook_InvokedPerPlay_WithConfiguredSource()
        {
            var calls = 0;
            AudioSource seen = null;
            SoundDef seenDef = null;
            var config = new SoundSystemConfig { SfxVoices = 2 };
            config.OnVoiceConfigured = (source, def) => { calls++; seen = source; seenDef = def; };
            using var sys = new SoundSystem(config);
            var played = ClipDef();
            sys.Play(played, new Vector3(1f, 0f, 0f));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(seenDef, Is.SameAs(played));
            Assert.IsNotNull(seen);
            Assert.That(seen.clip, Is.Not.Null, "source must be fully configured when the hook runs");
            Assert.That(seen.transform.position.x, Is.EqualTo(1f).Within(0.001f));
            sys.Play(ClipDef());
            Assert.That(calls, Is.EqualTo(2));
            yield break;
        }

        [UnityTest]
        public IEnumerator Hook_NotInvokedForMusic()
        {
            var calls = 0;
            var config = new SoundSystemConfig { SfxVoices = 2 };
            config.OnVoiceConfigured = (_, _) => calls++;
            using var sys = new SoundSystem(config);
            sys.PlayMusic(SoundSystemMusicTests.Def(withIntro: false));
            yield return null;
            Assert.That(calls, Is.EqualTo(0));
        }
    }
}
