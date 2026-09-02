#if BUN3_ADDRESSABLES && UNITY_EDITOR
using System.Collections;
using Bun3.Unity.Audio;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    // AssetDatabase-only (editor); real Addressables load against a committed test clip +
    // AddressableAssetsData authoring, run in AssetDatabase ("fast") mode — see task-3-report.md.
    public sealed class PreloadRealLoadTests
    {
        private const string TestClipAddress = "bun3-test-clip";
        private const string TestClipAssetPath = "Assets/Bun3AudioTestAssets/bun3-test-clip.wav";

        private static AssetReferenceT<AudioClip> TestClipReference() =>
            new AssetReferenceT<AudioClip>(UnityEditor.AssetDatabase.AssetPathToGUID(TestClipAssetPath));

        [UnityTest]
        public IEnumerator Preload_Play_Release_RoundTrip() => UniTask.ToCoroutine(async () =>
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.AddressableClips = new[] { TestClipReference() };
            await sys.PreloadAsync(def);
            Assert.IsTrue(sys.IsPreloaded(def));
            var h = sys.Play(def);
            Assert.IsTrue(h.IsValid, "preloaded addressable def must play synchronously");
            h.Stop();
            sys.Tick(0.05f);
            sys.ReleasePreloaded(def);
            Assert.IsFalse(sys.IsPreloaded(def));
        });

        [UnityTest]
        public IEnumerator Preload_InvalidGuid_WarnsAndStaysUnpreloaded() => UniTask.ToCoroutine(async () =>
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.AddressableClips = new[]
            {
                new AssetReferenceT<AudioClip>(System.Guid.NewGuid().ToString("N")),
            };
            LogAssert.ignoreFailingMessages = true; // Addressables' own error spam
            await sys.PreloadAsync(def); // must not throw (silent-skip contract)
            LogAssert.ignoreFailingMessages = false;
            Assert.IsFalse(sys.IsPreloaded(def));
        });

        // Carried from Task 2's fix round: locks the TOCTOU fix — two simultaneous
        // PreloadAsync(def) calls both complete, the def ends up preloaded exactly once,
        // and the loser's redundant batch is released rather than leaked.
        //
        // Each call gets its own AssetReferenceT instance (same GUID) rather than sharing
        // one: AssetReferenceT.LoadAssetAsync() is itself single-flight per instance (throws
        // an Addressables Error log if called again before release — see AssetReference.cs),
        // so reusing one instance across the two calls would test that guard, not this one.
        // PreloadAsync reads def.AddressableClips synchronously before its first await, so
        // reassigning between the two calls still races both loads against the same def.
        [UnityTest]
        public IEnumerator ConcurrentPreload_BothComplete_NoLeak() => UniTask.ToCoroutine(async () =>
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.AddressableClips = new[] { TestClipReference() };
            var a = sys.PreloadAsync(def);
            def.AddressableClips = new[] { TestClipReference() };
            var b = sys.PreloadAsync(def); // concurrent — loser must release its own batch
            await a;
            await b;
            Assert.IsTrue(sys.IsPreloaded(def));
            sys.ReleasePreloaded(def);
            Assert.IsFalse(sys.IsPreloaded(def));
        });
    }
}
#endif
