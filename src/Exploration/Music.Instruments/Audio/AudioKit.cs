using System;
using MC.Shared;
using UnityEngine;
using UnityEngine.Audio;

namespace MC.Exploration.MusicInstrumentsMod;

// Me = shared audio bits, made on first use on a game with sound: the "ones" clip (every sample 1.0) that each emitter
// loops so Unity's 3D rolloff, panning and volume still apply to synthesized sound (the synth multiply into it: Unity
// apply spatial gains BEFORE custom filters), the game's SFX mixer group (Master and Sound effects sliders, snapshots),
// the output sample rate, and a smooth DSP clock for the main thread. Dedicated server or no audio device = nothing
// made, Available false (notes still relayed by the server).
internal static class AudioKit
{
    private static AudioClip _ones;
    private static AudioMixerGroup _sfx;
    private static bool _sfxSearched;
    private static int _rate;
    private static double _smoothDsp;
    private static double _lastRawDsp = -1;
    private static int _smoothFrame = -1;

    // Sound can be made here: graphics device (not headless), audio running.
    internal static bool Available
    {
        get
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                return false;
            }
            var net = ZNet.instance;
            if (net != null && net.IsDedicated())
            {
                return false;
            }
            return SampleRate > 0;
        }
    }

    internal static int SampleRate
    {
        get
        {
            if (_rate <= 0)
            {
                _rate = AudioSettings.outputSampleRate;
            }
            return _rate;
        }
    }

    // Raw audio clock (seconds); advances in whole DSP buffers. Real time when audio is off (performance clock still runs).
    internal static double DspRaw => SampleRate > 0 ? AudioSettings.dspTime : Time.realtimeSinceStartupAsDouble;

    // Main-thread audio clock, smooth between buffer steps: follows dspTime, filled in with unscaled frame time, never
    // goes back. Same value for every caller within a frame.
    internal static double DspNow
    {
        get
        {
            if (_smoothFrame == Time.frameCount)
            {
                return _smoothDsp;
            }
            _smoothFrame = Time.frameCount;
            var raw = DspRaw;
            if (raw != _lastRawDsp)
            {
                _lastRawDsp = raw;
                // Snap to the new buffer time unless we are already ahead of it (never step back).
                if (raw > _smoothDsp || _smoothDsp - raw > 0.25)
                {
                    _smoothDsp = raw;
                }
            }
            else
            {
                _smoothDsp += Time.unscaledDeltaTime;
                // Do not run more than ~2 buffers ahead of the audio thread.
                if (_smoothDsp > raw + 0.05)
                {
                    _smoothDsp = raw + 0.05;
                }
            }
            return _smoothDsp;
        }
    }

    internal static AudioClip Ones
    {
        get
        {
            if (_ones != null)
            {
                return _ones;
            }
            var rate = SampleRate > 0 ? SampleRate : 48000;
            // Name without the letters c-a-t together: one known mod mutes any clip or object whose name has them.
            // Filled by the reader callback at creation (non-streamed clip): no SetData (its Span overload does not
            // compile against this framework).
            _ones = AudioClip.Create("MC_MusicOnes", rate, 1, rate, false, FillOnes);
            _ones.hideFlags = HideFlags.HideAndDontSave;
            return _ones;
        }
    }

    private static void FillOnes(float[] data)
    {
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = 1f;
        }
    }

    // The game's "SFX" group of the master mixer (exact name: "SFX_LARGE" also exists). Null = none found (sliders
    // then do not apply; warned once).
    internal static AudioMixerGroup SfxGroup
    {
        get
        {
            if (_sfxSearched)
            {
                return _sfx;
            }
            _sfxSearched = true;
            try
            {
                var mixer = AudioMan.instance != null ? AudioMan.instance.m_masterMixer : null;
                if (mixer != null)
                {
                    foreach (var group in mixer.FindMatchingGroups(string.Empty))
                    {
                        if (group != null && group.name == "SFX")
                        {
                            _sfx = group;
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Debug("Could not search the game's mixer groups: " + e.Message);
            }
            if (_sfx == null)
            {
                Log.Warning("The game's sound effects mixer group was not found: instruments ignore the Master and Sound "
                            + "effects volume sliders (the mod's own Volume setting still applies).");
            }
            return _sfx;
        }
    }

#if DEBUG
    // Self test: every group name of the master mixer.
    internal static string DescribeMixer()
    {
        var mixer = AudioMan.instance != null ? AudioMan.instance.m_masterMixer : null;
        if (mixer == null)
        {
            return "no master mixer";
        }
        var names = new System.Collections.Generic.List<string>();
        foreach (var group in mixer.FindMatchingGroups(string.Empty))
        {
            names.Add(group != null ? group.name : "null");
        }
        AudioSettings.GetDSPBufferSize(out var length, out var count);
        return $"mixer {mixer.name}: {string.Join(", ", names.ToArray())}; rate {AudioSettings.outputSampleRate}, "
               + $"buffer {length} x {count}, speakers {AudioSettings.speakerMode}";
    }
#endif
}
