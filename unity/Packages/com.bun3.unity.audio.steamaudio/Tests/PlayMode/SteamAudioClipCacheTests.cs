using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.SteamAudio.Tests
{
    public sealed class SteamAudioClipCacheTests
    {
        [Test]
        public void NonpositionalDefinitionBypassesPcmBudgetUsingSharedSpatialSettings()
        {
            var clip = AudioClip.Create("cache-ui", 300000, 1, 48000, false);
            var definition = ScriptableObject.CreateInstance<SoundDef>();
            var spatial = ScriptableObject.CreateInstance<SoundSpatialProfile>();
            try
            {
                definition.Clips = new[] { clip };
                definition.Spatial = SpatialMode.Positional;
                spatial.Spatial = SpatialMode.None;
                definition.SpatialProfile = spatial;
                using var cache = new SteamAudioClipCache(32);
                Assert.That(cache.IsDefinitionPrepared(definition), Is.True);
                Assert.DoesNotThrow(() => cache.PrepareDefinition(definition));
                Assert.That(cache.DecodedBytes, Is.Zero);
                Assert.That(cache.IsPrepared(clip), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
                UnityEngine.Object.DestroyImmediate(spatial);
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void DirectDefinitionAndReplacementClipsRequireExplicitPreparation()
        {
            var first = AudioClip.Create("cache-direct", 8, 1, 48000, false);
            var replacement = AudioClip.Create("cache-replacement", 8, 1, 48000, false);
            var definition = ScriptableObject.CreateInstance<SoundDef>();
            try
            {
                definition.Spatial = SpatialMode.Positional;
                definition.Clips = new[] { null, first, first };
                using var cache = new SteamAudioClipCache(64);
                Assert.That(cache.IsDefinitionPrepared(definition), Is.False);
                cache.PrepareDefinition(definition);
                Assert.That(cache.IsDefinitionPrepared(definition), Is.True);
                Assert.That(cache.IsPrepared(first), Is.True);
                Assert.That(cache.DecodedBytes, Is.EqualTo(32));
                definition.Clips = new[] { replacement };
                Assert.That(cache.IsDefinitionPrepared(definition), Is.False);
                cache.PrepareDefinition(definition);
                Assert.That(cache.IsDefinitionPrepared(definition), Is.True);
                Assert.That(cache.DecodedBytes, Is.EqualTo(64));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(replacement);
            }
        }

        [Test]
        public void DefinitionPreparationUsesPreloadedRuntimeClips()
        {
            var serialized = AudioClip.Create("cache-serialized", 32, 1, 48000, false);
            var loaded = AudioClip.Create("cache-loaded", 8, 1, 48000, false);
            var definition = ScriptableObject.CreateInstance<SoundDef>();
            try
            {
                definition.Spatial = SpatialMode.Follow;
                definition.Clips = new[] { serialized };
                definition.RuntimeClips = new[] { loaded };
                using var cache = new SteamAudioClipCache(32);
                cache.PrepareDefinition(definition);
                Assert.That(cache.IsDefinitionPrepared(definition), Is.True);
                Assert.That(cache.IsPrepared(loaded), Is.True);
                Assert.That(cache.IsPrepared(serialized), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
                UnityEngine.Object.DestroyImmediate(serialized);
                UnityEngine.Object.DestroyImmediate(loaded);
            }
        }

        [Test]
        public void FailedDefinitionPreparationLeavesExistingCacheAndOtherDefinitionsUsable()
        {
            var existing = AudioClip.Create("cache-existing", 8, 1, 48000, false);
            var next = AudioClip.Create("cache-next", 8, 1, 48000, false);
            var overflow = AudioClip.Create("cache-overflow", 8, 1, 48000, false);
            var definition = ScriptableObject.CreateInstance<SoundDef>();
            try
            {
                definition.Spatial = SpatialMode.Positional;
                using var cache = new SteamAudioClipCache(64);
                cache.PrepareClips(new[] { existing });
                definition.Clips = new[] { next, overflow };
                Assert.Throws<InvalidOperationException>(() => cache.PrepareDefinition(definition));
                Assert.That(cache.DecodedBytes, Is.EqualTo(32));
                Assert.That(cache.IsPrepared(existing), Is.True);
                Assert.That(cache.IsPrepared(next), Is.False);
                Assert.That(cache.IsPrepared(overflow), Is.False);
                Assert.That(cache.IsDefinitionPrepared(definition), Is.False);
                definition.Clips = new[] { next };
                cache.PrepareDefinition(definition);
                Assert.That(cache.IsDefinitionPrepared(definition), Is.True);
                Assert.That(cache.DecodedBytes, Is.EqualTo(64));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
                UnityEngine.Object.DestroyImmediate(existing);
                UnityEngine.Object.DestroyImmediate(next);
                UnityEngine.Object.DestroyImmediate(overflow);
            }
        }

        [Test]
        public void ColdCacheCountsMonoStereoPcmOnceAndRejectsBudgetOverflowTransactionally()
        {
            var mono = AudioClip.Create("cache-mono", 8, 1, 48000, false);
            var stereo = AudioClip.Create("cache-stereo", 8, 2, 48000, false);
            try
            {
                using var cache = new SteamAudioClipCache(96);
                cache.PrepareClips(new[] { mono, stereo, mono });
                Assert.That(cache.DecodedBytes, Is.EqualTo(96));
                cache.PrepareClips(new[] { mono, stereo });
                Assert.That(cache.DecodedBytes, Is.EqualTo(96));
                using var tooSmall = new SteamAudioClipCache(95);
                Assert.Throws<InvalidOperationException>(() => tooSmall.PrepareClips(new[] { mono, stereo }));
                Assert.That(tooSmall.DecodedBytes, Is.Zero);
            }
            finally { UnityEngine.Object.DestroyImmediate(mono); UnityEngine.Object.DestroyImmediate(stereo); }
        }

        [Test]
        public void StreamingInputIsRejectedWithAnExplicitColdPathDiagnostic()
        {
            var clip = AudioClip.Create("cache-streaming", 480, 1, 48000, true);
            try
            {
                using var cache = new SteamAudioClipCache(10000);
                // Runtime-created streaming clips report DecompressOnLoad on this Unity version.
                // GetData is the public readability probe and emits this exact engine diagnostic before returning false.
                LogAssert.Expect(LogType.Error, "Can't get data from streamed samples for audio clip \"cache-streaming\". If you created the audio clip with AudioClip.Create, set the 'stream' argument to false, so you can access the data with 'AudioClip.GetData'. For a disk-based audio clip, change the load type to 'DecompressOnLoad', so you can access the data with 'AudioClip.GetData'.");
                var error = Assert.Throws<ArgumentException>(() => cache.PrepareClips(new[] { clip }));
                StringAssert.Contains("stream", error.Message.ToLowerInvariant());
                Assert.That(cache.DecodedBytes, Is.Zero);
            }
            finally { UnityEngine.Object.DestroyImmediate(clip); }
        }
    }
}
