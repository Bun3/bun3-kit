#if BUN3_ADDRESSABLES
using System.Collections;
using Bun3.Unity.Audio;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class PreloadGuardTests
    {
        [UnityTest]
        public IEnumerator Preload_NoAddressableClips_CompletesImmediately() => UniTask.ToCoroutine(async () =>
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var def = ScriptableObject.CreateInstance<SoundDef>();
            await sys.PreloadAsync(def); // AddressableClips null → no-op
            Assert.IsFalse(sys.IsPreloaded(def));
        });

        [UnityTest]
        public IEnumerator Release_NotPreloaded_IsNoOp() => UniTask.ToCoroutine(async () =>
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var def = ScriptableObject.CreateInstance<SoundDef>();
            sys.ReleasePreloaded(def); // must not throw
            Assert.IsFalse(sys.IsPreloaded(def));
            await UniTask.Yield();
        });

        [UnityTest]
        public IEnumerator UnpreloadedAddressableDef_PlayReturnsInvalid() => UniTask.ToCoroutine(async () =>
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.AddressableClips = new[] { new UnityEngine.AddressableAssets.AssetReferenceT<AudioClip>(System.Guid.NewGuid().ToString("N")) };
            LogAssert.Expect(LogType.Warning,
                "SoundSystem.Play: def has no loaded clips (assign Clips or preload AddressableClips); returning an invalid handle.");
            var h = sys.Play(def);
            Assert.IsFalse(h.IsValid);
            await UniTask.Yield();
        });
    }
}
#endif
