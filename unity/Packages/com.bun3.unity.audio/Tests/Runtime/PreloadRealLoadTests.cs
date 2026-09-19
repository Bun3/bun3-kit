#if BUN3_ADDRESSABLES && UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Audio.Tests
{
    // Real Addressables operations with a delayed provider: no consumer-project asset or catalog entry required.
    public sealed class PreloadRealLoadTests
    {
        private ResourceLocationMap _locator;
        private ClipProvider _provider;
        private SoundSystem _system;
        private SoundDef _definition;
        private readonly List<Task> _pending = new List<Task>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            var initialization = Addressables.InitializeAsync(false);
            yield return initialization;
            var status = initialization.Status;
            Addressables.Release(initialization);
            Assert.That(status, Is.EqualTo(AsyncOperationStatus.Succeeded));

            var key = Guid.NewGuid().ToString("N");
            _provider = new ClipProvider();
            _provider.Initialize(key, null);
            _locator = new ResourceLocationMap(key);
            _locator.Add(key, new ResourceLocationBase(key, key, _provider.ProviderId, typeof(AudioClip)));
            Addressables.ResourceManager.ResourceProviders.Add(_provider);
            Addressables.AddResourceLocator(_locator);
            _system = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            _definition = ScriptableObject.CreateInstance<SoundDef>();
            _definition.AddressableClips = new[] { new AssetReferenceT<AudioClip>(key) };
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try
            {
                // Drain every started operation before removing its provider, including after a failed assertion.
                _provider?.CompletePending();
                foreach (var task in _pending)
                {
                    while (!task.IsCompleted)
                        yield return null;
                    _ = task.Exception;
                }
            }
            finally
            {
                _pending.Clear();
                _system?.Dispose();
                if (_definition != null)
                    Object.DestroyImmediate(_definition);
                if (_locator != null)
                    Addressables.RemoveResourceLocator(_locator);
                if (_provider != null)
                {
                    Addressables.ResourceManager.ResourceProviders.Remove(_provider);
                    _provider.Dispose();
                }
                _system = null;
                _definition = null;
                _locator = null;
                _provider = null;
            }
        }

        [UnityTest]
        public IEnumerator Preload_Play_Release_RoundTrip()
        {
            var pending = StartPreload();
            _provider.CompletePending();
            yield return Observe(pending);
            Assert.IsTrue(_system.IsPreloaded(_definition));
            var clip = _definition.RuntimeClips[0];
            var handle = _system.Play(_definition);
            Assert.IsTrue(handle.IsValid, "A preloaded Addressable definition must play synchronously.");
            handle.Stop();
            _system.Tick(0.05f);
            _system.ReleasePreloaded(_definition);
            Assert.IsFalse(_system.IsPreloaded(_definition));
            Assert.IsNull(_definition.RuntimeClips);
            Assert.IsTrue(clip == null, "Releasing the preload must release its final Addressables reference.");
            Assert.That(_provider.ReleaseCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Preload_InvalidGuid_WarnsAndStaysUnpreloaded()
        {
            _definition.AddressableClips = new[] { new AssetReferenceT<AudioClip>(Guid.NewGuid().ToString("N")) };
            LogAssert.Expect(LogType.Error, new Regex("InvalidKeyException"));
            LogAssert.Expect(LogType.Warning, new Regex("SoundSystem.PreloadAsync: failed to load"));
            yield return Observe(StartPreload());
            Assert.IsFalse(_system.IsPreloaded(_definition));
            Assert.IsNull(_definition.RuntimeClips);
        }

        [UnityTest]
        public IEnumerator Preload_LaterClipFails_ReleasesEarlierClips()
        {
            var failedKey = Guid.NewGuid().ToString("N");
            var failedLocation = new ResourceLocationBase(failedKey, failedKey, _provider.ProviderId, typeof(AudioClip));
            failedLocation.Data = "fail";
            _locator.Add(failedKey, failedLocation);
            _definition.AddressableClips = new[]
            {
                _definition.AddressableClips[0],
                new AssetReferenceT<AudioClip>(failedKey),
            };
            LogAssert.Expect(LogType.Error, new Regex("Controlled preload failure"));
            LogAssert.Expect(LogType.Warning, new Regex("SoundSystem.PreloadAsync: failed to load"));
            var pending = StartPreload();
            _provider.CompletePending();
            yield return Observe(pending);
            Assert.IsFalse(_system.IsPreloaded(_definition));
            Assert.IsNull(_definition.RuntimeClips);
            Assert.That(_provider.ReleaseCount, Is.EqualTo(1), "A failed later clip must release the earlier successful load.");
            Assert.IsTrue(_provider.Clip == null);
        }

        [UnityTest]
        public IEnumerator ConcurrentPreload_BothComplete_NoLeak()
        {
            var first = StartPreload();
            var second = StartPreload();
            var bothPending = !first.IsCompleted && !second.IsCompleted;
            _provider.CompletePending();
            // WhenAll observes both operations even if either one faults.
            yield return Observe(Task.WhenAll(first, second));
            Assert.IsTrue(bothPending, "Both calls must overlap while loading the same AssetReference instance.");
            Assert.IsTrue(_system.IsPreloaded(_definition));
            var clip = _definition.RuntimeClips[0];
            Assert.IsTrue(clip != null, "The losing preload must not release the winning preload's reference.");
            Assert.That(_provider.ReleaseCount, Is.Zero);
            _system.ReleasePreloaded(_definition);
            Assert.IsFalse(_system.IsPreloaded(_definition));
            Assert.IsTrue(clip == null, "No redundant concurrent-preload reference may survive release.");
            Assert.That(_provider.ReleaseCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator DisposeDuringPreload_ReleasesBatch()
        {
            var pending = StartPreload();
            var wasPending = !pending.IsCompleted;
            _system.Dispose();
            _provider.CompletePending();
            yield return Observe(pending);
            Assert.IsTrue(wasPending, "Dispose must race an in-flight load, not a completed preload.");
            Assert.IsNull(_definition.RuntimeClips, "A disposed system must not leave clips on the definition.");
            Assert.IsFalse(_system.IsPreloaded(_definition));
            Assert.That(_provider.ReleaseCount, Is.EqualTo(1), "Completion after disposal must release its batch.");
            Assert.IsTrue(_provider.Clip == null);
        }

        [UnityTest]
        public IEnumerator CancellationDuringPreload_ReleasesBatch()
        {
            using var cancellation = new CancellationTokenSource();
            var pending = StartPreload(cancellation.Token);
            cancellation.Cancel();
            _provider.CompletePending();
            while (!pending.IsCompleted)
                yield return null;
            Assert.Catch<OperationCanceledException>(() => pending.GetAwaiter().GetResult());
            Assert.IsNull(_definition.RuntimeClips);
            Assert.IsFalse(_system.IsPreloaded(_definition));
            Assert.That(_provider.ReleaseCount, Is.EqualTo(1));
            Assert.IsTrue(_provider.Clip == null);
        }

        private Task StartPreload(CancellationToken cancellation = default)
        {
            var pending = _system.PreloadAsync(_definition, cancellation).AsTask();
            _pending.Add(pending);
            return pending;
        }

        private static IEnumerator Observe(Task pending)
        {
            while (!pending.IsCompleted)
                yield return null;
            pending.GetAwaiter().GetResult();
        }

        private sealed class ClipProvider : ResourceProviderBase, IDisposable
        {
            private readonly List<ProvideHandle> _pending = new List<ProvideHandle>();
            internal AudioClip Clip { get; private set; }
            internal int ReleaseCount { get; private set; }

            public override Type GetDefaultType(IResourceLocation location) => typeof(AudioClip);

            public override void Provide(ProvideHandle provideHandle)
            {
                if (Equals(provideHandle.Location.Data, "fail"))
                {
                    provideHandle.Complete<AudioClip>(null, false, new InvalidOperationException("Controlled preload failure"));
                    return;
                }
                Clip = AudioClip.Create("Addressables preload fixture", 4800, 1, 48000, false);
                _pending.Add(provideHandle);
            }

            internal void CompletePending()
            {
                foreach (var handle in _pending)
                    handle.Complete(Clip, true, null);
                _pending.Clear();
            }

            public override void Release(IResourceLocation location, object asset)
            {
                ReleaseCount++;
                Object.DestroyImmediate((AudioClip)asset);
            }

            public void Dispose()
            {
                if (Clip != null)
                    Object.DestroyImmediate(Clip);
            }
        }
    }
}
#endif
