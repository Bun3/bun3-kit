#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Bun3.Unity.Audio;
using UnityEngine;

/// <summary>
/// Self-contained demo of <see cref="SoundSystem"/>: generates every clip procedurally
/// (no imported audio assets needed) and maps keys to exercise music (intro+loop,
/// crossfade, pause/resume, stop) and SFX (pitch-varied one-shots). Drop this on any
/// GameObject in an empty scene and press Play.
///
/// [1] play intro+loop music — THE ear-verification for the sample-accurate DSP seam:
///     listen for no click or gap where the 440Hz intro hands off to the 330Hz loop.
/// [2] crossfade to a second tone track (550Hz loop, no intro).
/// [3] SFX blip (880Hz) with per-play pitch variation.
/// [P] pause/resume music.
/// [S] stop music (1s fade-out).
/// </summary>
public sealed class AudioDemo : MonoBehaviour
{
    private const int SampleRate = 44100;

    private SoundSystem _sound;
    private MusicDef _introLoopDef;
    private MusicDef _crossfadeDef;
    private SoundDef _sfxDef;

    private void Start()
    {
        _sound = new SoundSystem(new SoundSystemConfig { SfxVoices = 8 });

        _introLoopDef = ScriptableObject.CreateInstance<MusicDef>();
        _introLoopDef.Intro = CreateToneClip("DemoIntro", 440f, 2f);
        _introLoopDef.Loop = CreateToneClip("DemoLoop", 330f, 2f);
        _introLoopDef.Volume = 1f;
        _introLoopDef.DefaultFade = 1f;

        _crossfadeDef = ScriptableObject.CreateInstance<MusicDef>();
        _crossfadeDef.Loop = CreateToneClip("DemoCrossfadeLoop", 550f, 2f);
        _crossfadeDef.Volume = 1f;
        _crossfadeDef.DefaultFade = 1.5f;

        _sfxDef = ScriptableObject.CreateInstance<SoundDef>();
        _sfxDef.Clips = new[] { CreateToneClip("DemoBlip", 880f, 0.2f) };
        _sfxDef.Volume = new FloatRange(0.8f, 1f);
        _sfxDef.Pitch = new FloatRange(0.85f, 1.3f);

        Debug.Log("AudioDemo ready — [1] intro+loop music, [2] crossfade, [3] SFX blip, [P] pause/resume, [S] stop.");
    }

    private void Update()
    {
        if (_sound == null)
        {
            return;
        }

#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }
        var playMusic = keyboard.digit1Key.wasPressedThisFrame;
        var crossfade = keyboard.digit2Key.wasPressedThisFrame;
        var playSfx = keyboard.digit3Key.wasPressedThisFrame;
        var togglePause = keyboard.pKey.wasPressedThisFrame;
        var stopMusic = keyboard.sKey.wasPressedThisFrame;
#else
        var playMusic = Input.GetKeyDown(KeyCode.Alpha1);
        var crossfade = Input.GetKeyDown(KeyCode.Alpha2);
        var playSfx = Input.GetKeyDown(KeyCode.Alpha3);
        var togglePause = Input.GetKeyDown(KeyCode.P);
        var stopMusic = Input.GetKeyDown(KeyCode.S);
#endif

        if (playMusic)
        {
            _sound.PlayMusic(_introLoopDef);
        }
        if (crossfade)
        {
            _sound.PlayMusic(_crossfadeDef);
        }
        if (playSfx)
        {
            _sound.Play(_sfxDef);
        }
        if (togglePause)
        {
            if (_sound.IsMusicPaused)
            {
                _sound.ResumeMusic();
            }
            else
            {
                _sound.PauseMusic();
            }
        }
        if (stopMusic)
        {
            _sound.StopMusic(1f);
        }
    }

    private void OnDestroy()
    {
        _sound?.Dispose();
    }

    /// <summary>
    /// Generates a mono sine-wave clip. The sample count is rounded to a whole number of
    /// cycles so the clip starts and ends at a zero crossing — click-free both when it
    /// loops on itself and when the music subsystem hands off between clips.
    /// </summary>
    private static AudioClip CreateToneClip(string name, float frequency, float duration)
    {
        var cycles = Mathf.Max(1, Mathf.RoundToInt(frequency * duration));
        var sampleCount = Mathf.RoundToInt(cycles / frequency * SampleRate);
        var data = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            data[i] = Mathf.Sin(2f * Mathf.PI * frequency * i / SampleRate) * 0.5f;
        }
        var clip = AudioClip.Create(name, sampleCount, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
