using System;
using System.Threading;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.MusicInstrumentsMod;

// Me = one sounding instrument in the world: AudioSource looping the ones clip + this filter, on a child object of a
// player (or alone at a position). OnAudioFilterRead (audio thread) render the synth and multiply each channel by the
// incoming signal, which is the ones clip after Unity's volume, 3D rolloff and panning: so the synth is heard where
// the player is, with the game's SFX volume. Local performer: 2D (centred, spatial blend 0). Others: 3D, linear rolloff
// from MinDistance to the hearing range.
// Main thread -> audio thread: notes go in a small lock-guarded list (start/end in output samples); the audio thread
// moves them into the synth at the start of each buffer. Never throw on the audio thread: any error clears the buffer
// (the ones clip must never reach the speakers as a DC offset) and silences me; the main thread reports it.
// Stop the source before destroying me (Unity crash report: filter of a destroyed component).
internal sealed class Emitter : MonoBehaviour
{
    internal const string ObjectName = "MC_MusicEmitter";
    internal const float MinDistance = 3f;
    private const int MaxPending = 128;
    private const int MonoSize = 4096;

    private struct Pending
    {
        internal long Start;
        internal long End;
        internal byte Pitch;
        internal byte Velocity;
        internal bool ReleaseAll;
        internal bool Hush;
        internal bool EndNote;
    }

    private readonly object _lock = new object();
    private readonly Pending[] _pending = new Pending[MaxPending];
    private int _pendingCount;
    private readonly float[] _mono = new float[MonoSize];
    private SynthCore _synth;
    private AudioSource _source;
    private int _rate;
    private volatile bool _failed;
    private volatile string _error;
    private bool _errorReported;
    private volatile bool _silent = true;
    private long _lastQueued;    // sample until which a hand-off to the audio thread may still be unseen (main thread)
    private long _clockSample;   // audio thread: sample at the start of the current buffer
    private float _range = -1f;  // hearing range last applied (3D)

    // Debug counters (written on the audio thread, read anywhere: approximate is fine).
    internal int Callbacks;
    internal int Channels;
    internal int Frames;
    internal float PeakOut;
    internal double SumSquares;
    internal long SamplesMeasured;
    internal float PeakLeft;
    internal float PeakRight;

    internal InstrumentKind Kind { get; private set; }
    internal bool Local { get; private set; }

    // Nothing queued and every voice done (main thread view, one buffer late at most).
    internal bool Silent
    {
        get
        {
            // Failed: it makes no sound any more (every buffer cleared), so it can be freed.
            if (_failed)
            {
                return true;
            }
            lock (_lock)
            {
                if (_pendingCount > 0)
                {
                    return false;
                }
            }
            return _silent && (long)(AudioKit.DspRaw * _rate) > _lastQueued;
        }
    }

    internal bool Failed => _failed;

    // New emitter under parent (null = world root at position). Null when this game has no sound.
    internal static Emitter Create(Transform parent, Vector3 position, InstrumentKind kind, bool local, float hearingRange)
    {
        if (!AudioKit.Available)
        {
            return null;
        }
        var go = new GameObject(ObjectName);
        if (parent != null)
        {
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
        }
        else
        {
            go.transform.position = position;
        }
        // AudioSource first: a filter applies to the source on its own object, in component order.
        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.clip = AudioKit.Ones;
        source.loop = true;
        source.outputAudioMixerGroup = AudioKit.SfxGroup;
        source.priority = 32;
        source.dopplerLevel = 0f;
        source.bypassReverbZones = true;
        source.bypassEffects = false;
        source.spread = 60f;
        var emitter = go.AddComponent<Emitter>();
        emitter._source = source;
        emitter._rate = AudioKit.SampleRate;
        emitter._synth = new SynthCore(emitter._rate, kind);
        emitter.Kind = kind;
        emitter.Local = local;
        emitter.SetHearingRange(hearingRange);
        emitter.ApplyVolume();
        // JIT the render path now (main thread) so the first audio callback does not compile it.
        var warm = new float[64];
        emitter._synth.Render(warm, 0, warm.Length, 0);
        emitter._synth.Hush();
        source.Play();
        return emitter;
    }

    internal void SetHearingRange(float range)
    {
        if (_source == null)
        {
            return;
        }
        if (Local)
        {
            _source.spatialBlend = 0f;
            return;
        }
        // Called on every batch (server may change the range): Unity properties only written on a change.
        if (range == _range)
        {
            return;
        }
        _range = range;
        _source.spatialBlend = 1f;
        _source.rolloffMode = AudioRolloffMode.Linear;
        _source.minDistance = MinDistance;
        _source.maxDistance = Mathf.Max(MinDistance + 1f, range);
    }

    internal void ApplyVolume()
    {
        if (_source != null)
        {
            _source.volume = Plugin.Volume != null ? Plugin.Volume.Value : 0.8f;
        }
    }

    // Note between two audio-clock times (seconds, AudioKit clock). Past start = plays at the next buffer.
    internal void Schedule(double dspStart, double dspEnd, byte pitch, byte velocity)
    {
        if (_failed)
        {
            return;
        }
        var start = (long)(dspStart * _rate);
        var end = (long)(dspEnd * _rate);
        if (end <= start)
        {
            end = start + _rate / 20;
        }
        lock (_lock)
        {
            if (_pendingCount >= MaxPending)
            {
                return;
            }
            _pending[_pendingCount++] = new Pending { Start = start, End = end, Pitch = pitch, Velocity = velocity };
        }
        _silent = false;
        GuardHandOff();
    }

    // The audio thread may drain pending items and only report the synth state a buffer later: Silent waits a quarter
    // second after the last hand-off. Queued notes (also far ahead) and ringing tails then show in the synth's own state
    // (a note off that ends a held note frees the source soon, never 8 s + 4 s later).
    private void GuardHandOff()
    {
        var guard = (long)(AudioKit.DspRaw * _rate) + _rate / 4;
        if (guard > _lastQueued)
        {
            _lastQueued = guard;
        }
    }

    // Note off at an audio-clock time (seconds): notes of that pitch sounding past it end there (free play).
    internal void EndNote(double dspEnd, byte pitch)
    {
        if (_failed)
        {
            return;
        }
        var end = (long)(dspEnd * _rate);
        lock (_lock)
        {
            if (_pendingCount >= MaxPending)
            {
                return;
            }
            _pending[_pendingCount++] = new Pending { Start = end, End = end, Pitch = pitch, EndNote = true };
        }
        GuardHandOff();
    }

    // Every note let go now (tails ring) and queued ones dropped.
    internal void ReleaseAll()
    {
        lock (_lock)
        {
            _pendingCount = 0;
            _pending[_pendingCount++] = new Pending { ReleaseAll = true };
        }
        GuardHandOff();
    }

    // Silence now (feature off, world exit).
    internal void Hush()
    {
        lock (_lock)
        {
            _pendingCount = 0;
            _pending[_pendingCount++] = new Pending { Hush = true };
        }
    }

    // Main thread, each frame by the owner (Performance / Listeners): report an audio-thread error once.
    internal void CheckHealth()
    {
        if (_failed && !_errorReported)
        {
            _errorReported = true;
            PatchGuard.Report("Emitter.OnAudioFilterRead", new InvalidOperationException(_error ?? "audio error"));
        }
    }

    // Stop the source first, then destroy (never leave a playing source without its filter).
    internal void Destroy()
    {
        try
        {
            if (_source != null)
            {
                _source.Stop();
            }
        }
        catch (Exception)
        {
            // Object may be half gone at quit.
        }
        if (this != null && gameObject != null)
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        try
        {
            if (_source != null)
            {
                _source.Stop();
            }
        }
        catch (Exception)
        {
            // Quit order: source may be gone already.
        }
    }

    // Audio thread.
    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (_failed || _synth == null || channels <= 0)
        {
            Array.Clear(data, 0, data.Length);
            return;
        }
        try
        {
            var frames = data.Length / channels;
            var bufferStart = (long)(AudioSettings.dspTime * _rate);
            if (bufferStart < _clockSample)
            {
                bufferStart = _clockSample; // dspTime never goes back; guard anyway
            }
            Drain(bufferStart);
            var done = 0;
            var peak = 0f;
            var peakL = 0f;
            var peakR = 0f;
            double sum = 0;
            while (done < frames)
            {
                var n = Math.Min(MonoSize, frames - done);
                Array.Clear(_mono, 0, n);
                _synth.Render(_mono, 0, n, bufferStart + done);
                for (var i = 0; i < n; i++)
                {
                    var s = _mono[i];
                    var baseIndex = (done + i) * channels;
                    for (var c = 0; c < channels; c++)
                    {
                        var v = data[baseIndex + c] * s;
                        data[baseIndex + c] = v;
                        var a = v < 0f ? -v : v;
                        if (a > peak)
                        {
                            peak = a;
                        }
                        if (c == 0 && a > peakL)
                        {
                            peakL = a;
                        }
                        else if (c == 1 && a > peakR)
                        {
                            peakR = a;
                        }
                    }
                    sum += s * s;
                }
                done += n;
            }
            _clockSample = bufferStart + frames;
            _silent = _synth.Silent;
            Callbacks++;
            Channels = channels;
            Frames = frames;
            PeakOut = peak;
            PeakLeft = peakL;
            PeakRight = peakR;
            SumSquares += sum;
            SamplesMeasured += frames;
        }
        catch (Exception e)
        {
            Array.Clear(data, 0, data.Length);
            _error = e.GetType().Name + ": " + e.Message;
            _failed = true;
        }
    }

    // Audio thread: queued notes into the synth (sorted by the synth itself).
    private void Drain(long now)
    {
        if (!Monitor.TryEnter(_lock))
        {
            return; // main thread adding right now: next buffer takes them (21 ms)
        }
        try
        {
            for (var i = 0; i < _pendingCount; i++)
            {
                var p = _pending[i];
                if (p.Hush)
                {
                    _synth.Hush();
                }
                else if (p.ReleaseAll)
                {
                    _synth.ReleaseAll(now);
                }
                else if (p.EndNote)
                {
                    _synth.EndNote(p.Pitch, Math.Max(now, p.End)); // late note off: ends it now
                }
                else if (p.End > now && p.Start >= now - _rate / 10)
                {
                    // Notes more than 0.1 s late (the main thread stalled) are dropped, never piled on one sample.
                    _synth.Schedule(p.Start, p.End, p.Pitch, p.Velocity);
                }
            }
            _pendingCount = 0;
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

#if DEBUG
    // Self test: root mean square of the synth output since the last reset (0 = silent).
    internal float Rms => SamplesMeasured > 0 ? (float)Math.Sqrt(SumSquares / SamplesMeasured) : 0f;

    internal void ResetMeasure()
    {
        SumSquares = 0;
        SamplesMeasured = 0;
        PeakOut = 0f;
        PeakLeft = 0f;
        PeakRight = 0f;
    }
#endif
}
