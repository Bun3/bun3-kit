# Bun3.Unity.Audio 음악 서브시스템 (슬라이스 2) 구현 플랜

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 인트로+무한루프(샘플 정확 `PlayScheduled`), 크로스페이드, pause/resume 스케줄 재계산, UniTask 대기를 갖춘 음악 서브시스템(`SoundSystem.Music.cs`).

**Architecture:** 뮤직 채널 2개(A/B) 고정, 채널당 AudioSource 2개(intro/loop — 이음새 없는 전환은 소스 2개가 필수). 채널 상태는 `MusicChannel` struct, DSP 스케줄 계산은 순수 `MusicMath` 정적 함수(EditMode 테스트 대상), AudioSource 반영과 fade 진행은 SoundSystem의 기존 단일 틱에 통합. 슬라이스 1의 규율 계승: **테이블/채널 변형 먼저 → 유저 신호(TrySetResult)는 맨 마지막**(continuation은 동기 실행됨), 틱은 `unscaledDeltaTime`, 핫패스 무할당.

**Tech Stack:** Unity 6000.3 `AudioSource.PlayScheduled`/`AudioSettings.dspTime`, UniTask `AutoResetUniTaskCompletionSource`, 기존 `com.bun3.unity.audio` 0.1.0 코어.

**Spec:** `docs/superpowers/specs/2026-08-31-unity-audio-design.md` — "음악 서브시스템" 섹션. 스코프 아웃(스펙 명시): 플레이리스트·셔플·마디 동기화 전환.

## 스펙 대비 확정 디비에이션 (플랜 레벨 룰링 — 집행자는 그대로 따른다)

- **`SoundSystemConfig.MusicVoices` 필드는 만들지 않는다.** 뮤직 채널은 내부 고정 2개(A/B). 크로스페이드는 정확히 2채널만 쓰고, 3개 이상의 동시 음악은 플레이리스트 영역(스코프 아웃)이라 2 이외의 값이 무의미한 설정 노브는 API 거짓말이다. 스펙의 Config 예시(`MusicVoices = 2`)는 이 결정으로 대체.
- **`CrossfadeAsync`라는 별도 메서드명은 만들지 않는다.** `PlayMusic(def, float fade = 0f)` 하나로 통일 — 재생 중인 곡이 있으면 fade는 크로스페이드, 없으면 fade-in. async는 `PlayMusicAsync(def, fade, ct)`(전환 완료 시점 완료). 스펙 UniTask 예시의 `CrossfadeAsync(musicDef, 2f)`는 `PlayMusicAsync(musicDef, 2f)`로 대응.
- 음악 pitch는 항상 1 고정(스펙: 타임스케일 pitch 연동에서 음악은 기본 제외 — 슬라이스 3 옵트인도 음악엔 적용하지 않음).

## Global Constraints

- 코드·주석·XML 문서 **영어만**. public 멤버 영어 XML 문서, 빌드 경고 0, 블록 네임스페이스.
- 음악 틱 핫패스 무할당(클로저·LINQ·boxing 금지). `AutoResetUniTaskCompletionSource`는 await 시에만 lazy 생성.
- **2단계 신호 규율**: 틱/PlayMusic 안에서 상태 변형과 소스 조작을 전부 끝낸 뒤 `TrySetResult`를 마지막에 호출한다. continuation은 동기 실행되어 재진입한다(슬라이스 1 최종 리뷰에서 실증).
- 커밋: gitmoji 제목 + 이중 `-m` 트레일러(`Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`).
- 테스트 커맨드(절대 경로 필수 — 맨 이름은 다른 프로젝트로 해석됨):
  `unity test "E:\Projects\orca\workspace\bun3-kit\Bun3-sound-manager\unity" --mode PlayMode --filter "<픽스처|네임스페이스>" --output "<scratch>.xml" --timeout 1200`
  결과 XML의 `total`>0 필수(0이면 필터 미매칭 오탐). Unity 배치 인스턴스 동시 실행 금지. 런 후 패키지 밖 더럽혀진 파일은 `git checkout`.
- 기존 전체 회귀 기준선: `--filter "Bun3.Unity.Audio.Tests"` → 27/27.
- 버전: 0.1.0 Unreleased에 계속 누적(미퍼블리시 상태라 버전 유지).

---

### Task 1: MusicDef + MusicChannel 상태 + MusicMath

**Files:**
- Create: `unity/Packages/com.bun3.unity.audio/Runtime/MusicDef.cs`
- Create: `unity/Packages/com.bun3.unity.audio/Runtime/MusicChannel.cs`
- Modify: `unity/Packages/com.bun3.unity.audio/Runtime/SoundSystemConfig.cs` (MusicGroup 필드 추가)
- Test: `unity/Packages/com.bun3.unity.audio/Tests/Runtime/MusicMathTests.cs`

**Interfaces:**
- Consumes: 없음 (새 타입)
- Produces (Task 2~5가 소비):
  - `public sealed class MusicDef : ScriptableObject { AudioClip Intro; AudioClip Loop; float Volume = 1f; float DefaultFade = 2f; }`
  - `internal enum MusicState : byte { Idle, FadingIn, Playing, FadingOut }`
  - `internal struct MusicChannel { MusicState State; MusicDef Def; bool Paused; bool LoopScheduled; double LoopStartDsp; float FadeElapsed, FadeDuration, FadeFrom, FadeTo, FadeFactor; AutoResetUniTaskCompletionSource Completion; }`
  - `internal static class MusicMath { double ClipSeconds(AudioClip); double RemainingSeconds(int timeSamples, int totalSamples, int frequency); }`
  - `SoundSystemConfig.MusicGroup` (`AudioMixerGroup`, null 허용)

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
using Bun3.Unity.Audio;
using NUnit.Framework;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class MusicMathTests
    {
        [Test]
        public void RemainingSeconds_AtStart_IsFullLength()
            => Assert.That(
                MusicMath.RemainingSeconds(timeSamples: 0, totalSamples: 44100, frequency: 44100),
                Is.EqualTo(1.0).Within(1e-9));

        [Test]
        public void RemainingSeconds_Midway_IsHalf()
            => Assert.That(
                MusicMath.RemainingSeconds(22050, 44100, 44100),
                Is.EqualTo(0.5).Within(1e-9));

        [Test]
        public void RemainingSeconds_PastEnd_ClampsToZero()
            => Assert.That(
                MusicMath.RemainingSeconds(44100, 44100, 44100),
                Is.EqualTo(0.0));
    }
}
```

- [ ] **Step 2: 컴파일 실패 확인** (MusicMath 미정의 — 배치 런 exit 6)

- [ ] **Step 3: 구현**

`MusicDef.cs`:

```csharp
using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>
    /// Designer-tuned music definition: an optional intro that plays once, then a loop
    /// that repeats until stopped. The asset reference itself is the runtime key.
    /// </summary>
    [CreateAssetMenu(menuName = "Bun3/Audio/Music Def", fileName = "MusicDef")]
    public sealed class MusicDef : ScriptableObject
    {
        /// <summary>Optional intro clip played once before the loop; null starts on the loop.</summary>
        public AudioClip Intro;

        /// <summary>Loop clip repeated until the track is stopped or replaced. Required.</summary>
        public AudioClip Loop;

        /// <summary>Track volume multiplier applied on top of fades.</summary>
        public float Volume = 1f;

        /// <summary>Default fade seconds used when PlayMusic is called without an explicit fade.</summary>
        public float DefaultFade = 2f;
    }
}
```

`MusicChannel.cs` (`MusicState`·`MusicMath`는 밀접한 형제 — 같은 파일):

```csharp
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>Lifecycle state of a music channel.</summary>
    internal enum MusicState : byte
    {
        Idle,
        FadingIn,
        Playing,
        FadingOut,
    }

    /// <summary>
    /// Per-channel music state driven by the SoundSystem tick — no coroutines.
    /// Never touches audio APIs; SoundSystem.Music.cs mirrors it onto the channel's
    /// intro/loop AudioSource pair.
    /// </summary>
    internal struct MusicChannel
    {
        public MusicState State;
        public MusicDef Def;
        public bool Paused;
        public bool LoopScheduled;
        public double LoopStartDsp;
        public float FadeElapsed;
        public float FadeDuration;
        public float FadeFrom;
        public float FadeTo;
        public float FadeFactor;
        public AutoResetUniTaskCompletionSource Completion;
    }

    /// <summary>Pure DSP-schedule arithmetic, kept engine-free for direct testing.</summary>
    internal static class MusicMath
    {
        /// <summary>Exact clip length in seconds from sample count (float clip.length loses precision).</summary>
        public static double ClipSeconds(AudioClip clip)
            => (double)clip.samples / clip.frequency;

        /// <summary>Seconds of playback left given the current playhead; clamps at zero.</summary>
        public static double RemainingSeconds(int timeSamples, int totalSamples, int frequency)
        {
            var remaining = (double)(totalSamples - timeSamples) / frequency;
            return remaining > 0.0 ? remaining : 0.0;
        }
    }
}
```

`SoundSystemConfig.cs` — `SfxGroup` 아래에 추가:

```csharp
        /// <summary>Mixer group music routes to; null leaves music unrouted.</summary>
        public AudioMixerGroup MusicGroup;
```

- [ ] **Step 4: 테스트 통과 확인** (`--filter "MusicMathTests"`, total=3)

- [ ] **Step 5: 커밋** — `✨ MusicDef asset + music channel state and DSP math`

---

### Task 2: PlayMusic 코어 — 인트로+루프 스케줄링·정지·틱 통합

**Files:**
- Create: `unity/Packages/com.bun3.unity.audio/Runtime/SoundSystem.Music.cs`
- Modify: `unity/Packages/com.bun3.unity.audio/Runtime/SoundSystem.cs` (생성자: 뮤직 소스 워밍 / Dispose: 뮤직 신호)
- Modify: `unity/Packages/com.bun3.unity.audio/Runtime/SoundSystem.Tick.cs` (`TickMusic(dt)` 호출 추가)
- Test: `unity/Packages/com.bun3.unity.audio/Tests/Runtime/SoundSystemMusicTests.cs`

**Interfaces:**
- Consumes: Task 1의 `MusicDef`/`MusicChannel`/`MusicState`/`MusicMath`; 기존 `_root`, `_config`, `_disposed`, `_rng` 없음(음악은 랜덤 없음), `AudioMath` 없음(볼륨은 선형 곱).
- Produces (Task 3~5가 소비):
  - `public void PlayMusic(MusicDef def, float fade = -1f)` — fade<0 → `def.DefaultFade`. 이 태스크에선 "아무것도 재생 중이 아닐 때" 경로만(fade-in / 즉시). 크로스페이드는 Task 3.
  - `public void StopMusic(float fadeOut = 0f)`
  - `public bool IsMusicPlaying { get; }`
  - internal: `MusicChannel[] MusicChannels`(2), `AudioSource[] MusicIntroSources`, `AudioSource[] MusicLoopSources`, `int ActiveMusic`(-1=없음), `void TickMusic(float dt)`
  - 상수 `MusicScheduleHeadroom = 0.05`(초) — PlayScheduled 여유

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
using System.Collections;
using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundSystemMusicTests
    {
        internal static MusicDef Def(bool withIntro, float defaultFade = 0f)
        {
            var def = ScriptableObject.CreateInstance<MusicDef>();
            if (withIntro)
            {
                def.Intro = AudioClip.Create("intro", 4410, 1, 44100, false); // 0.1 s
            }
            def.Loop = AudioClip.Create("loop", 4410, 1, 44100, false);
            def.DefaultFade = defaultFade;
            return def;
        }

        [UnityTest]
        public IEnumerator PlayMusic_WithIntro_HandsOffToLoop()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            sys.PlayMusic(Def(withIntro: true));
            Assert.IsTrue(sys.IsMusicPlaying);
            var ch = sys.ActiveMusic;
            Assert.IsTrue(sys.MusicChannels[ch].LoopScheduled);
            // 0.1 s intro + headroom + margin: by 0.5 s the loop source must be playing.
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.IsTrue(sys.MusicLoopSources[ch].isPlaying);
            Assert.IsFalse(sys.MusicIntroSources[ch].isPlaying);
            Assert.IsTrue(sys.IsMusicPlaying); // loop never completes on its own
        }

        [UnityTest]
        public IEnumerator PlayMusic_NoIntro_StartsOnLoop()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            sys.PlayMusic(Def(withIntro: false));
            var ch = sys.ActiveMusic;
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.IsTrue(sys.MusicLoopSources[ch].isPlaying);
            Assert.IsFalse(sys.MusicIntroSources[ch].isPlaying);
        }

        [UnityTest]
        public IEnumerator StopMusic_WithFade_EndsTrack()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            sys.PlayMusic(Def(withIntro: false));
            yield return null;
            sys.StopMusic(fadeOut: 0.1f);
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.IsFalse(sys.IsMusicPlaying);
            Assert.IsFalse(sys.MusicLoopSources[0].isPlaying);
        }

        [UnityTest]
        public IEnumerator PlayMusic_FadeIn_RampsFactor()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            sys.PlayMusic(Def(withIntro: false), fade: 1f);
            var ch = sys.ActiveMusic;
            sys.TickMusic(0.5f);
            Assert.That(sys.MusicChannels[ch].FadeFactor, Is.EqualTo(0.5f).Within(0.01f));
            sys.TickMusic(0.6f);
            Assert.That(sys.MusicChannels[ch].State, Is.EqualTo(MusicState.Playing));
            yield break;
        }
    }
}
```

- [ ] **Step 2: 컴파일 실패 확인**

- [ ] **Step 3: 구현**

`SoundSystem.Music.cs`:

```csharp
// Music subsystem: two fixed channels (A/B), each an intro+loop AudioSource pair.
// Intro→loop handoff is sample-accurate via PlayScheduled on the DSP clock.
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.Audio
{
    public sealed partial class SoundSystem
    {
        private const int MusicChannelCount = 2;
        private const double MusicScheduleHeadroom = 0.05;

        internal readonly MusicChannel[] MusicChannels = new MusicChannel[MusicChannelCount];
        internal readonly AudioSource[] MusicIntroSources = new AudioSource[MusicChannelCount];
        internal readonly AudioSource[] MusicLoopSources = new AudioSource[MusicChannelCount];

        /// <summary>Channel currently owning the foreground track; -1 when silent.</summary>
        internal int ActiveMusic { get; private set; } = -1;

        /// <summary>True while any music channel is audible (fading counts).</summary>
        public bool IsMusicPlaying => ActiveMusic >= 0;

        /// <summary>
        /// Plays a music track. A negative <paramref name="fade"/> uses the def's DefaultFade.
        /// When another track is playing, the fade crossfades the two (see Task 3);
        /// when silent, it fades the new track in (0 = instant).
        /// </summary>
        public void PlayMusic(MusicDef def, float fade = -1f)
        {
            if (_disposed)
            {
                throw new System.ObjectDisposedException(nameof(SoundSystem));
            }
            if (def == null || def.Loop == null)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.LogWarning("SoundSystem.PlayMusic: def has no loop clip; ignored.");
#endif
                return;
            }
            if (fade < 0f)
            {
                fade = def.DefaultFade;
            }

            var channel = 0; // silent path: always channel 0. Task 3 picks the free channel.
            StartMusicOnChannel(channel, def, fade);
            ActiveMusic = channel;
        }

        /// <summary>Stops the current track, optionally fading out first.</summary>
        public void StopMusic(float fadeOut = 0f)
        {
            if (_disposed || ActiveMusic < 0)
            {
                return;
            }
            BeginMusicFadeOut(ActiveMusic, fadeOut);
            ActiveMusic = -1;
        }

        private void StartMusicOnChannel(int channel, MusicDef def, float fadeIn)
        {
            ref var ch = ref MusicChannels[channel];
            ch.State = fadeIn > 0f ? MusicState.FadingIn : MusicState.Playing;
            ch.Def = def;
            ch.Paused = false;
            ch.FadeElapsed = 0f;
            ch.FadeDuration = fadeIn;
            ch.FadeFrom = 0f;
            ch.FadeTo = 1f;
            ch.FadeFactor = fadeIn > 0f ? 0f : 1f;
            ch.Completion = null;

            var introSource = MusicIntroSources[channel];
            var loopSource = MusicLoopSources[channel];
            var startDsp = AudioSettings.dspTime + MusicScheduleHeadroom;

            loopSource.clip = def.Loop;
            loopSource.loop = true;
            if (def.Intro != null)
            {
                introSource.clip = def.Intro;
                introSource.PlayScheduled(startDsp);
                ch.LoopStartDsp = startDsp + MusicMath.ClipSeconds(def.Intro);
                loopSource.PlayScheduled(ch.LoopStartDsp);
                ch.LoopScheduled = true;
            }
            else
            {
                introSource.clip = null;
                loopSource.PlayScheduled(startDsp);
                ch.LoopStartDsp = startDsp;
                ch.LoopScheduled = true;
            }
            ApplyMusicVolume(channel);
        }

        private void BeginMusicFadeOut(int channel, float duration)
        {
            ref var ch = ref MusicChannels[channel];
            if (ch.State == MusicState.Idle)
            {
                return;
            }
            if (duration <= 0f)
            {
                SilenceMusicChannel(channel);
                return;
            }
            ch.FadeFrom = ch.FadeFactor;
            ch.FadeTo = 0f;
            ch.FadeElapsed = 0f;
            ch.FadeDuration = duration;
            ch.State = MusicState.FadingOut;
        }

        // Stops both sources and frees the channel. Does NOT signal Completion —
        // callers collect it first and fire signals last (two-phase discipline).
        private AutoResetUniTaskCompletionSource SilenceMusicChannel(int channel)
        {
            ref var ch = ref MusicChannels[channel];
            var completion = ch.Completion;
            MusicIntroSources[channel].Stop();
            MusicLoopSources[channel].Stop();
            ch.State = MusicState.Idle;
            ch.Def = null;
            ch.Paused = false;
            ch.LoopScheduled = false;
            ch.Completion = null;
            return completion;
        }

        private void ApplyMusicVolume(int channel)
        {
            ref var ch = ref MusicChannels[channel];
            var volume = (ch.Def != null ? ch.Def.Volume : 0f) * ch.FadeFactor;
            MusicIntroSources[channel].volume = volume;
            MusicLoopSources[channel].volume = volume;
        }

        internal void TickMusic(float dt)
        {
            // Phase 1: advance state; collect at most one signal per channel.
            AutoResetUniTaskCompletionSource signal0 = null;
            AutoResetUniTaskCompletionSource signal1 = null;
            for (var i = 0; i < MusicChannelCount; i++)
            {
                ref var ch = ref MusicChannels[i];
                if (ch.State == MusicState.Idle || ch.Paused)
                {
                    continue;
                }

                if (ch.FadeDuration > 0f)
                {
                    ch.FadeElapsed += dt;
                    var t = Mathf.Clamp01(ch.FadeElapsed / ch.FadeDuration);
                    ch.FadeFactor = Mathf.Lerp(ch.FadeFrom, ch.FadeTo, t);
                    if (t >= 1f)
                    {
                        ch.FadeDuration = 0f;
                        if (ch.State == MusicState.FadingOut)
                        {
                            var completion = SilenceMusicChannel(i);
                            if (i == 0) { signal0 = completion; } else { signal1 = completion; }
                            continue;
                        }
                        // Fade-in finished: signal the awaiter (PlayMusicAsync, Task 5).
                        ch.State = MusicState.Playing;
                        if (i == 0) { signal0 = ch.Completion; } else { signal1 = ch.Completion; }
                        ch.Completion = null;
                    }
                }
                ApplyMusicVolume(i);
            }

            // Phase 2: user signals last — continuations run inline and may re-enter.
            signal0?.TrySetResult();
            signal1?.TrySetResult();
        }
    }
}
```

`SoundSystem.cs` 생성자 — SFX 소스 워밍 루프 아래에 추가:

```csharp
            for (var i = 0; i < MusicChannelCount; i++)
            {
                MusicIntroSources[i] = CreateMusicSource("MusicIntro");
                MusicLoopSources[i] = CreateMusicSource("MusicLoop");
            }
```

같은 파일에 private 헬퍼 추가:

```csharp
        private AudioSource CreateMusicSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.outputAudioMixerGroup = _config.MusicGroup;
            return source;
        }
```

`SoundSystem.cs` `Dispose()` — 보이스 신호 수집과 같은 2단계에 뮤직 채널 합류: 활성 뮤직 채널의 `SilenceMusicChannel(i)` 반환값을 로컬에 모아 **기존 완료 신호 발화 지점에서 함께** `TrySetResult()` (`ActiveMusic = -1`도 설정).

`SoundSystem.Tick.cs` `Tick(float dt)` 끝에 추가:

```csharp
            TickMusic(dt);
```

- [ ] **Step 4: 테스트 통과 확인** (`--filter "SoundSystemMusicTests"`, total=4) + 기존 회귀 (`--filter "Bun3.Unity.Audio.Tests"` — 27+새 테스트 전부 그린)

- [ ] **Step 5: 커밋** — `✨ Music core: sample-accurate intro+loop, stop, tick fades`

---

### Task 3: 크로스페이드 + 최신 요청 우선 스틸

**Files:**
- Modify: `unity/Packages/com.bun3.unity.audio/Runtime/SoundSystem.Music.cs` (`PlayMusic` 채널 선택 교체)
- Test: `unity/Packages/com.bun3.unity.audio/Tests/Runtime/SoundSystemCrossfadeTests.cs`

**Interfaces:**
- Consumes: Task 2 전부
- Produces: `PlayMusic`의 완전한 시맨틱 — ① 무음이면 fade-in, ② 재생 중이면 현재 곡 fade-out + 새 곡 반대 채널에서 fade-in 병행(같은 duration), ③ 크로스페이드 중 재호출이면 **FadingOut 채널을 즉시 침묵시키고(스틸) 그 채널에서 새 곡 시작**, 기존 FadingIn/Playing 채널이 fade-out으로 전환. 스틸된 채널의 Completion은 정상 완료 신호(2단계 규율).

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
using System.Collections;
using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundSystemCrossfadeTests
    {
        [UnityTest]
        public IEnumerator PlayMusic_WhilePlaying_CrossfadesOnOtherChannel()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            sys.PlayMusic(SoundSystemMusicTests.Def(withIntro: false));
            yield return null;
            var first = sys.ActiveMusic;
            sys.PlayMusic(SoundSystemMusicTests.Def(withIntro: false), fade: 1f);
            var second = sys.ActiveMusic;
            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(sys.MusicChannels[first].State, Is.EqualTo(MusicState.FadingOut));
            Assert.That(sys.MusicChannels[second].State, Is.EqualTo(MusicState.FadingIn));
            sys.TickMusic(1.1f);
            Assert.That(sys.MusicChannels[first].State, Is.EqualTo(MusicState.Idle));
            Assert.That(sys.MusicChannels[second].State, Is.EqualTo(MusicState.Playing));
        }

        [UnityTest]
        public IEnumerator PlayMusic_DuringCrossfade_StealsFadingOutChannel()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var a = SoundSystemMusicTests.Def(withIntro: false);
            var b = SoundSystemMusicTests.Def(withIntro: false);
            var c = SoundSystemMusicTests.Def(withIntro: false);
            sys.PlayMusic(a);
            yield return null;
            sys.PlayMusic(b, fade: 1f);           // A fading out, B fading in
            var fadingOut = 1 - sys.ActiveMusic;   // A's channel
            sys.PlayMusic(c, fade: 1f);            // steal: C takes A's channel, B fades out
            Assert.That(sys.ActiveMusic, Is.EqualTo(fadingOut));
            Assert.That(sys.MusicChannels[sys.ActiveMusic].Def, Is.SameAs(c));
            Assert.That(sys.MusicChannels[1 - sys.ActiveMusic].State, Is.EqualTo(MusicState.FadingOut));
        }

        [UnityTest]
        public IEnumerator PlayMusic_ZeroFade_SwapsInstantly()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            sys.PlayMusic(SoundSystemMusicTests.Def(withIntro: false));
            yield return null;
            var first = sys.ActiveMusic;
            sys.PlayMusic(SoundSystemMusicTests.Def(withIntro: false), fade: 0f);
            Assert.That(sys.MusicChannels[first].State, Is.EqualTo(MusicState.Idle));
            Assert.IsTrue(sys.IsMusicPlaying);
            yield break;
        }
    }
}
```

주의: `SoundSystemMusicTests.Def`를 쓰므로 Task 2의 헬퍼가 `internal static`이어야 한다(이미 그렇게 명시됨).

- [ ] **Step 2: 테스트 실패 확인** (Task 2 구현은 항상 채널 0 사용 → 두 번째 PlayMusic이 같은 채널을 덮어 실패)

- [ ] **Step 3: 구현** — `PlayMusic`의 채널 선택 로직 교체

```csharp
            int channel;
            AutoResetUniTaskCompletionSource stolen = null;
            if (ActiveMusic < 0)
            {
                channel = 0;
            }
            else
            {
                var other = 1 - ActiveMusic;
                if (MusicChannels[other].State == MusicState.FadingOut)
                {
                    // Third request mid-crossfade: newest wins — cut the dying track now.
                    stolen = SilenceMusicChannel(other);
                }
                channel = other;
                BeginMusicFadeOut(ActiveMusic, fade);
            }
            StartMusicOnChannel(channel, def, fade);
            ActiveMusic = channel;
            stolen?.TrySetResult(); // last, after all state mutation (two-phase)
```

(`BeginMusicFadeOut(ActiveMusic, 0f)`은 즉시 침묵 경로를 이미 처리 — zero-fade 스왑이 여기서 나온다. 단 `BeginMusicFadeOut`의 즉시 경로가 반환하는 Completion이 버려지지 않도록, `BeginMusicFadeOut`을 `AutoResetUniTaskCompletionSource` 반환으로 바꾸고(내부에서 `SilenceMusicChannel` 반환값 전달, fade 경로는 null 반환) `StopMusic`/`PlayMusic`에서 수집해 마지막에 신호한다.)

- [ ] **Step 4: 테스트 통과 확인** (`--filter "SoundSystemCrossfadeTests"`, total=3) + 음악 픽스처 회귀

- [ ] **Step 5: 커밋** — `✨ Music crossfade with newest-wins channel stealing`

---

### Task 4: Pause/Resume + 루프 스케줄 재계산

**Files:**
- Modify: `unity/Packages/com.bun3.unity.audio/Runtime/SoundSystem.Music.cs`
- Test: `unity/Packages/com.bun3.unity.audio/Tests/Runtime/SoundSystemMusicPauseTests.cs`

**Interfaces:**
- Consumes: Task 1~3
- Produces:
  - `public void PauseMusic()` — 전 채널: 두 소스 `Pause()`. **루프가 예약만 되고 아직 시작 전이면**(`LoopScheduled && dspTime < LoopStartDsp`) 예약을 취소(`loopSource.Stop()`, `LoopScheduled = false`) — DSP 클록은 pause 중에도 흐르므로 예약을 살려두면 pause 중에 루프가 터진다. `Paused = true`(틱이 fade 진행도 동결).
  - `public void ResumeMusic()` — `Paused = false`, 두 소스 `UnPause()`. 취소했던 루프 예약이 있으면 인트로 잔여 시간으로 재예약: `remaining = MusicMath.RemainingSeconds(introSource.timeSamples, def.Intro.samples, def.Intro.frequency)` → `loopSource.PlayScheduled(dspTime + remaining)`, `LoopStartDsp` 갱신, `LoopScheduled = true`.
  - `public bool IsMusicPaused { get; }` — 활성 채널의 Paused.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
using System.Collections;
using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundSystemMusicPauseTests
    {
        [UnityTest]
        public IEnumerator PauseDuringIntro_CancelsLoopSchedule_ResumeReschedules()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            // 0.5 s intro so the pause lands safely inside it.
            var def = ScriptableObject.CreateInstance<MusicDef>();
            def.Intro = AudioClip.Create("intro", 22050, 1, 44100, false);
            def.Loop = AudioClip.Create("loop", 4410, 1, 44100, false);
            sys.PlayMusic(def);
            var ch = sys.ActiveMusic;
            yield return new WaitForSecondsRealtime(0.15f); // inside intro
            sys.PauseMusic();
            Assert.IsTrue(sys.IsMusicPaused);
            Assert.IsFalse(sys.MusicChannels[ch].LoopScheduled, "pause must cancel the pending loop schedule");
            yield return new WaitForSecondsRealtime(0.6f);  // long past original loop start
            Assert.IsFalse(sys.MusicLoopSources[ch].isPlaying, "loop must not fire while paused");
            sys.ResumeMusic();
            Assert.IsTrue(sys.MusicChannels[ch].LoopScheduled, "resume must reschedule the loop");
            yield return new WaitForSecondsRealtime(0.6f);  // remaining intro (~0.35 s) + margin
            Assert.IsTrue(sys.MusicLoopSources[ch].isPlaying, "loop must start after the remaining intro");
        }

        [UnityTest]
        public IEnumerator PauseDuringLoop_ResumeContinues()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            sys.PlayMusic(SoundSystemMusicTests.Def(withIntro: false));
            var ch = sys.ActiveMusic;
            yield return new WaitForSecondsRealtime(0.2f); // loop running
            sys.PauseMusic();
            yield return null;
            Assert.IsFalse(sys.MusicLoopSources[ch].isPlaying);
            sys.ResumeMusic();
            yield return null;
            Assert.IsTrue(sys.MusicLoopSources[ch].isPlaying);
            Assert.IsFalse(sys.IsMusicPaused);
        }

        [UnityTest]
        public IEnumerator Pause_FreezesFade()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            sys.PlayMusic(SoundSystemMusicTests.Def(withIntro: false), fade: 1f);
            var ch = sys.ActiveMusic;
            sys.TickMusic(0.5f);
            sys.PauseMusic();
            var frozen = sys.MusicChannels[ch].FadeFactor;
            sys.TickMusic(10f);
            Assert.That(sys.MusicChannels[ch].FadeFactor, Is.EqualTo(frozen));
            yield break;
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인** (PauseMusic 미정의 — 컴파일 실패)

- [ ] **Step 3: 구현**

```csharp
        /// <summary>Whether the foreground track is paused.</summary>
        public bool IsMusicPaused
            => ActiveMusic >= 0 && MusicChannels[ActiveMusic].Paused;

        /// <summary>
        /// Pauses all music channels. A loop that is scheduled but not yet started is
        /// cancelled (the DSP clock keeps running while paused; a live schedule would
        /// fire mid-pause) and rescheduled from the intro's remaining time on resume.
        /// </summary>
        public void PauseMusic()
        {
            if (_disposed)
            {
                return;
            }
            for (var i = 0; i < MusicChannelCount; i++)
            {
                ref var ch = ref MusicChannels[i];
                if (ch.State == MusicState.Idle || ch.Paused)
                {
                    continue;
                }
                if (ch.LoopScheduled && AudioSettings.dspTime < ch.LoopStartDsp)
                {
                    MusicLoopSources[i].Stop();
                    ch.LoopScheduled = false;
                }
                MusicIntroSources[i].Pause();
                MusicLoopSources[i].Pause();
                ch.Paused = true;
            }
        }

        /// <summary>Resumes paused music, rescheduling a cancelled loop from the intro's remaining time.</summary>
        public void ResumeMusic()
        {
            if (_disposed)
            {
                return;
            }
            for (var i = 0; i < MusicChannelCount; i++)
            {
                ref var ch = ref MusicChannels[i];
                if (ch.State == MusicState.Idle || !ch.Paused)
                {
                    continue;
                }
                ch.Paused = false;
                MusicIntroSources[i].UnPause();
                MusicLoopSources[i].UnPause();
                if (!ch.LoopScheduled && ch.Def != null && ch.Def.Intro != null)
                {
                    var intro = ch.Def.Intro;
                    var remaining = MusicMath.RemainingSeconds(
                        MusicIntroSources[i].timeSamples, intro.samples, intro.frequency);
                    ch.LoopStartDsp = AudioSettings.dspTime + remaining;
                    MusicLoopSources[i].PlayScheduled(ch.LoopStartDsp);
                    ch.LoopScheduled = true;
                }
            }
        }
```

- [ ] **Step 4: 테스트 통과 확인** (`--filter "SoundSystemMusicPauseTests"`, total=3) + 음악 픽스처 전체 회귀

- [ ] **Step 5: 커밋** — `✨ Music pause/resume with loop schedule recomputation`

---

### Task 5: PlayMusicAsync + 문서·무할당·최종 회귀

**Files:**
- Modify: `unity/Packages/com.bun3.unity.audio/Runtime/SoundSystem.Music.cs` (async 진입점)
- Modify: `unity/Packages/com.bun3.unity.audio/README.md`, `CHANGELOG.md`
- Test: `unity/Packages/com.bun3.unity.audio/Tests/Runtime/SoundSystemMusicAsyncTests.cs`
- Modify: `unity/Packages/com.bun3.unity.audio/Tests/Runtime/AllocationTests.cs` (음악 틱 무할당 어설션 추가)

**Interfaces:**
- Consumes: Task 1~4 전부, 기존 `WithCancellation` 패턴(SoundSystem.Async.cs — 취소는 콜드 패스, 할당 허용·문서화)
- Produces:
  - `public UniTask PlayMusicAsync(MusicDef def, float fade = -1f, CancellationToken ct = default)` — **전환 완료 시점**(fade-in 종료; fade=0이면 즉시) 완료. ct 취소 = `StopMusic()` + await 취소.
  - `public UniTask StopMusicAsync(float fadeOut, CancellationToken ct = default)` — fade-out 종료 시점 완료.
  - 크로스페이드 중 스틸/교체된 트랙의 awaiter는 **정상 완료**(예외 없음 — 슬라이스 1 계약과 동일).

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
using System.Collections;
using Bun3.Unity.Audio;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundSystemMusicAsyncTests
    {
        [UnityTest]
        public IEnumerator PlayMusicAsync_CompletesWhenFadeInEnds() => UniTask.ToCoroutine(async () =>
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var started = Time.realtimeSinceStartup;
            await sys.PlayMusicAsync(SoundSystemMusicTests.Def(withIntro: false), fade: 0.2f);
            Assert.That(Time.realtimeSinceStartup - started, Is.GreaterThanOrEqualTo(0.15f));
            Assert.IsTrue(sys.IsMusicPlaying);
        });

        [UnityTest]
        public IEnumerator PlayMusicAsync_ZeroFade_CompletesImmediately() => UniTask.ToCoroutine(async () =>
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            await sys.PlayMusicAsync(SoundSystemMusicTests.Def(withIntro: false), fade: 0f);
            Assert.IsTrue(sys.IsMusicPlaying);
        });

        [UnityTest]
        public IEnumerator ReplacedTrack_AwaiterCompletesNormally() => UniTask.ToCoroutine(async () =>
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var waiting = sys.PlayMusicAsync(SoundSystemMusicTests.Def(withIntro: false), fade: 5f);
            sys.PlayMusic(SoundSystemMusicTests.Def(withIntro: false), fade: 0f); // instant replace
            await waiting; // must complete (normally), not hang
        });

        [UnityTest]
        public IEnumerator StopMusicAsync_CompletesAfterFadeOut() => UniTask.ToCoroutine(async () =>
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            sys.PlayMusic(SoundSystemMusicTests.Def(withIntro: false));
            await sys.StopMusicAsync(0.1f);
            Assert.IsFalse(sys.IsMusicPlaying);
        });
    }
}
```

- [ ] **Step 2: 컴파일 실패 확인**

- [ ] **Step 3: 구현** — `SoundSystem.Music.cs`에 추가

```csharp
        /// <summary>
        /// Plays a track and completes when the transition finishes (fade-in end; immediately
        /// when the effective fade is 0). Cancelling stops the music. Cancellation is a cold
        /// path and may allocate.
        /// </summary>
        public UniTask PlayMusicAsync(MusicDef def, float fade = -1f, System.Threading.CancellationToken ct = default)
        {
            PlayMusic(def, fade);
            if (ActiveMusic < 0)
            {
                return UniTask.CompletedTask; // rejected def (no loop clip)
            }
            ref var ch = ref MusicChannels[ActiveMusic];
            if (ch.State == MusicState.Playing)
            {
                return UniTask.CompletedTask; // zero-fade path: already done
            }
            ch.Completion ??= AutoResetUniTaskCompletionSource.Create();
            var task = ch.Completion.Task;
            return ct.CanBeCanceled ? WithMusicCancellation(task, ct) : task;
        }

        /// <summary>Fades the current track out and completes when it is silent.</summary>
        public UniTask StopMusicAsync(float fadeOut, System.Threading.CancellationToken ct = default)
        {
            if (_disposed || ActiveMusic < 0)
            {
                return UniTask.CompletedTask;
            }
            ref var ch = ref MusicChannels[ActiveMusic];
            ch.Completion ??= AutoResetUniTaskCompletionSource.Create();
            var task = ch.Completion.Task;
            StopMusic(fadeOut);
            return ct.CanBeCanceled ? WithMusicCancellation(task, ct) : task;
        }

        private async UniTask WithMusicCancellation(UniTask task, System.Threading.CancellationToken ct)
        {
            try
            {
                await task.AttachExternalCancellation(ct);
            }
            catch (System.OperationCanceledException)
            {
                StopMusic();
                throw;
            }
        }
```

주의(집행자 확인 사항): `StopMusicAsync`는 Completion을 **BeginMusicFadeOut 전에** 만들어야 한다 — fade=0 경로에서 `StopMusic` → `SilenceMusicChannel`이 Completion을 회수해 즉시 신호하므로 순서가 맞다(위 코드가 그 순서). `ReplacedTrack` 테스트는 크로스페이드 스틸/즉시 교체 경로가 Completion을 신호하는지(Task 3의 `BeginMusicFadeOut`/`SilenceMusicChannel` 수집 경로) 검증한다.

- [ ] **Step 4: AllocationTests에 음악 틱 어설션 추가**

기존 `PlayAndTick_DoNotAllocate`와 같은 파일에:

```csharp
        [Test]
        public void MusicTick_DoesNotAllocate()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var def = ScriptableObject.CreateInstance<MusicDef>();
            def.Loop = AudioClip.Create("loop", 4410, 1, 44100, false);
            sys.PlayMusic(def, fade: 0.5f);   // warm: channel active, fading
            sys.TickMusic(0.1f);

            Assert.That(() =>
            {
                sys.TickMusic(0.1f);          // mid-fade tick
                sys.TickMusic(1f);            // fade completes (no awaiter → no signal)
                sys.TickMusic(0.1f);          // steady-state tick
            }, Is.Not.AllocatingGCMemory());
        }
```

- [ ] **Step 5: README/CHANGELOG 갱신**

README: Music 사용 예 추가(PlayMusic 인트로+루프, 크로스페이드, PlayMusicAsync, Pause/Resume — 실제 시그니처 그대로). CHANGELOG 0.1.0 Added에:

```markdown
- Music subsystem: sample-accurate intro+loop via `PlayScheduled`, crossfade with
  newest-wins channel stealing, pause/resume with loop-schedule recomputation,
  and awaitable transitions (`PlayMusicAsync`/`StopMusicAsync`).
```

- [ ] **Step 6: 전체 회귀** — `--filter "Bun3.Unity.Audio.Tests"` 단일 런, 전부 그린(27 + Task1~5 신규 = 41개 예상; total 값을 XML로 확인). 빌드 경고 0 확인.

- [ ] **Step 7: 커밋** — `✨ Awaitable music transitions + docs + music tick alloc guard`

---

## Self-Review 결과

- 스펙 커버리지: 음악 섹션 5개 항목(뮤직 보이스 2·인트로+루프 PlayScheduled·크로스페이드 최신 우선·pause 재계산·스코프 아웃) 전부 태스크 매핑. UniTask 절의 PlayMusicAsync/CrossfadeAsync는 디비에이션 섹션의 API 통일 룰링으로 대응.
- 플레이스홀더: 없음 — 전 스텝 실코드.
- 타입 일관성: `MusicChannels/MusicIntroSources/MusicLoopSources/ActiveMusic/TickMusic`이 Task 2 Produces와 3·4·5 사용처 일치. `SoundSystemMusicTests.Def`는 internal static으로 3·4·5가 재사용. `BeginMusicFadeOut`의 반환형 변경(Task 3 주의)이 Task 5의 StopMusicAsync 서술과 정합.
- 리스크 명시: PlayMode 실시간 대기 테스트(0.5~0.6s 마진)는 배치 러너에서 프레임 드랍 시 플레이크 가능 — 마진을 클립 길이의 3배 이상으로 잡아 완화했고, 집행자는 실패 시 마진만 늘리는 조정을 기계적 수정으로 허용(리포트에 기록).
