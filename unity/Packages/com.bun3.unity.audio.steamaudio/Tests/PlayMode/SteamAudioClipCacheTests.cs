using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.SteamAudio.Tests
{
    public sealed class SteamAudioClipCacheTests
    {
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
