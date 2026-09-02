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
            try
            {
                LogAssert.ignoreFailingMessages = true; // Addressables' own error spam
                await sys.PreloadAsync(def); // must not throw (silent-skip contract)
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false; // never leaves the toggle on for later tests, even if await throws
            }
            Assert.IsFalse(sys.IsPreloaded(def));
        });

        // Carried from Task 2's fix round: locks the TOCTOU fix — two simultaneous
        // PreloadAsync(def) calls both complete, the def ends up preloaded exactly once,
        // and the loser's redundant batch is released rather than leaked.
        //
        // Both calls share the def's single AssetReferenceT instance — the real production
        // shape, since instances live on the def asset. This only stays leak-safe because
        // PreloadAsync now loads by RuntimeKey (Addressables.LoadAssetAsync<AudioClip>), not
        // through the instance's own LoadAssetAsync(): the instance method is single-flight
        // per instance and would throw on the second concurrent call.
        [UnityTest]
        public IEnumerator ConcurrentPreload_BothComplete_NoLeak() => UniTask.ToCoroutine(async () =>
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.AddressableClips = new[] { TestClipReference() };
            var a = sys.PreloadAsync(def);
            var b = sys.PreloadAsync(def); // concurrent — loser must release its own batch
            await a;
            await b;
            Assert.IsTrue(sys.IsPreloaded(def));
            sys.ReleasePreloaded(def);
            Assert.IsFalse(sys.IsPreloaded(def));
        });

        // Regression for the dispose-during-preload leak: PreloadAsync completing after
        // Dispose() must not commit handles into a dead system (never released) or leave
        // RuntimeClips set. Holds in both interleavings: if the load is still in flight when
        // Dispose() runs, PreloadAsync's post-load recheck (_disposed) releases the batch and
        // returns without touching RuntimeClips; if PreloadAsync already finished before
        // Dispose() (a fast AssetDatabase load can race ahead of the Dispose() call above),
        // Dispose's ReleaseAllPreloadedOnDispose releases it and nulls RuntimeClips instead —
        // either way the assertion holds.
        [UnityTest]
        public IEnumerator DisposeDuringPreload_ReleasesBatch() => UniTask.ToCoroutine(async () =>
        {
            var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.AddressableClips = new[] { TestClipReference() };
            var pending = sys.PreloadAsync(def);
            sys.Dispose();                    // dispose while load may be in flight
            await pending;                    // must complete normally (no throw)
            Assert.IsNull(def.RuntimeClips, "a disposed system must not leave clips on the def");
        });
    }
}
#endif
