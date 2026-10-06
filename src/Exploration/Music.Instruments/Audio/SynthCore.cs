using System;

namespace MC.Exploration.MusicInstrumentsMod;

// Me = the instrument synthesizer: notes in, mono samples out. One SynthCore per sounding player (emitter). Pure C#,
// no Unity, no allocation after construction (runs on the audio thread; offline tests run me too).
// Notes come with start/end in samples of the emitter's own sample clock; Render(from, count) makes those samples.
// A note that starts inside the block starts on its exact sample. Voices: fixed pool, oldest stolen when full.
// Flute: wavetable tone (soft harmonics) + breath noise + chiff at the start, vibrato after a moment.
// Lyre: plucked string (Karplus-Strong with tuned all-pass, pick position), small wooden body filter.
// Tambourine: skin (pitch-dropping thump or tap) + jingles (noise through metal resonances, rattling bursts).
// Not thread safe: owner feed notes (Schedule) and Render from one thread (the audio thread drains its queue).
internal sealed class SynthCore
{
    internal const int MaxVoices = 8;
    private const int MaxScheduled = 256;
    private const float MasterGain = 0.42f;

    private readonly int _rate;
    private readonly InstrumentKind _kind;
    private readonly Voice[] _voices;
    private readonly Scheduled[] _queue = new Scheduled[MaxScheduled];
    private int _queued;
    private long _age;
    private readonly BodyFilter _body;
    private float _dcX;
    private float _dcY;

    private struct Scheduled
    {
        internal long Start;
        internal long End;
        internal byte Pitch;
        internal byte Velocity;
    }

    internal SynthCore(int sampleRate, InstrumentKind kind)
    {
        _rate = sampleRate > 0 ? sampleRate : 48000;
        _kind = kind;
        _voices = new Voice[MaxVoices];
        for (var i = 0; i < MaxVoices; i++)
        {
            switch (kind)
            {
                case InstrumentKind.Lyre:
                    _voices[i] = new StringVoice(_rate, (uint)(1234567 + i * 7919));
                    break;
                case InstrumentKind.Tambourine:
                    _voices[i] = new TambourineVoice(_rate, (uint)(7654321 + i * 104729));
                    break;
                default:
                    _voices[i] = new FluteVoice(_rate, (uint)(31337 + i * 1009));
                    break;
            }
        }
        _body = new BodyFilter(_rate, kind);
    }

    internal InstrumentKind Kind => _kind;
    internal int SampleRate => _rate;

    internal int ActiveVoices
    {
        get
        {
            var n = 0;
            foreach (var v in _voices)
            {
                if (v.Active)
                {
                    n++;
                }
            }
            return n;
        }
    }

    // Note to start at sample start, released at end (sample clock of this core). Full queue = dropped (false).
    internal bool Schedule(long start, long end, byte pitch, byte velocity)
    {
        if (_queued >= MaxScheduled)
        {
            return false;
        }
        // Keep the queue sorted by start (insertion: few notes, mostly in order).
        var i = _queued;
        while (i > 0 && _queue[i - 1].Start > start)
        {
            _queue[i] = _queue[i - 1];
            i--;
        }
        _queue[i] = new Scheduled { Start = start, End = Math.Max(start + 1, end), Pitch = pitch, Velocity = velocity };
        _queued++;
        return true;
    }

    // Everything silent at once (feature off, performance cut).
    internal void Hush()
    {
        _queued = 0;
        foreach (var v in _voices)
        {
            v.Kill();
        }
    }

    // Every voice let go now (soft: release tails stay).
    internal void ReleaseAll(long now)
    {
        _queued = 0;
        foreach (var v in _voices)
        {
            if (v.Active)
            {
                v.ReleaseAt(now);
            }
        }
    }

    internal bool Silent => _queued == 0 && ActiveVoices == 0;

    // Note off (free play: a held flute note's length is not known when it starts): every queued or sounding note of
    // that pitch that started by end and lasts past it ends at end. Later notes of the pitch are left alone.
    internal void EndNote(byte pitch, long end)
    {
        for (var i = 0; i < _queued; i++)
        {
            if (_queue[i].Pitch == pitch && _queue[i].Start <= end && _queue[i].End > end)
            {
                _queue[i].End = Math.Max(_queue[i].Start + 1, end);
            }
        }
        foreach (var v in _voices)
        {
            if (v.Active && v.Pitch == pitch && v.StartSample <= end)
            {
                v.ReleaseAt(end);
            }
        }
    }

    // Add count samples starting at sample clock from into mono[offset..] (mono is cleared by caller).
    internal void Render(float[] mono, int offset, int count, long from)
    {
        var done = 0;
        while (done < count)
        {
            var now = from + done;
            // Start every note due now (also late ones: start at once).
            while (_queued > 0 && _queue[0].Start <= now)
            {
                StartNote(_queue[0], now);
                Array.Copy(_queue, 1, _queue, 0, _queued - 1);
                _queued--;
            }
            // Render until the next note start or the block end.
            var span = count - done;
            if (_queued > 0)
            {
                var until = _queue[0].Start - now;
                if (until < span)
                {
                    span = (int)Math.Max(1, until);
                }
            }
            foreach (var v in _voices)
            {
                if (v.Active)
                {
                    v.Render(mono, offset + done, span, now);
                }
            }
            done += span;
        }
        // Body colour, DC blocker, gentle limiter.
        for (var i = 0; i < count; i++)
        {
            var x = _body.Process(mono[offset + i]) * MasterGain;
            var y = x - _dcX + 0.9975f * _dcY;
            _dcX = x;
            _dcY = y;
            mono[offset + i] = SoftClip(y);
        }
    }

    private void StartNote(Scheduled s, long now)
    {
        // Free voice, else the one that started first (stolen).
        Voice pick = null;
        foreach (var v in _voices)
        {
            if (!v.Active)
            {
                pick = v;
                break;
            }
            if (pick == null || v.Age < pick.Age)
            {
                pick = v;
            }
        }
        if (pick == null)
        {
            return;
        }
        // Stolen voice still sounding go on from its level and phase (no click).
        pick.Pitch = s.Pitch;
        pick.StartSample = now;
        pick.Begin(s.Pitch, s.Velocity, now, s.End, ++_age);
    }

    // Smooth limiter: linear below 0.6, then bends to +-1.
    internal static float SoftClip(float x)
    {
        var a = x < 0f ? -x : x;
        if (a <= 0.6f)
        {
            return x;
        }
        var over = (a - 0.6f) / 0.4f;
        var bent = 0.6f + 0.4f * (over / (1f + over));
        return x < 0f ? -bent : bent;
    }

    // ---------- voices ----------

    private abstract class Voice
    {
        protected readonly int Rate;
        protected uint Seed;
        internal bool Active;
        internal long Age;
        internal byte Pitch;        // note it plays (note off finds it)
        internal long StartSample;  // when it started (note off never ends a later note)
        protected long EndSample;

        protected Voice(int rate, uint seed)
        {
            Rate = rate;
            Seed = seed == 0 ? 1u : seed;
        }

        internal abstract void Begin(byte pitch, byte velocity, long now, long end, long age);

        internal abstract void Render(float[] mono, int offset, int count, long now);

        internal virtual void ReleaseAt(long sample) => EndSample = Math.Min(EndSample, sample);

        internal void Kill() => Active = false;

        // xorshift32 white noise in -1..1.
        protected float Noise()
        {
            var x = Seed;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            Seed = x;
            return (x & 0xFFFFFF) / 8388608f - 1f;
        }
    }

    // Wavetable flute: soft odd/even harmonics, breath noise band-passed near the tone, chiff at the start, vibrato
    // fading in, small pitch scoop up at the start.
    private sealed class FluteVoice : Voice
    {
        private const int TableSize = 2048;
        private static readonly float[] Table = BuildTable();
        private double _phase;
        private double _freq;
        private float _amp;
        private float _env;
        private float _attackStep;
        private float _releaseStep;
        private bool _releasing;
        private long _start;
        // Breath noise band-pass (state variable filter).
        private float _bpLow;
        private float _bpBand;
        private float _bpF;
        private float _vibPhase;

        internal FluteVoice(int rate, uint seed) : base(rate, seed)
        {
        }

        private static float[] BuildTable()
        {
            var t = new float[TableSize + 1];
            for (var i = 0; i <= TableSize; i++)
            {
                var p = i / (double)TableSize * Math.PI * 2.0;
                t[i] = (float)(Math.Sin(p) + 0.30 * Math.Sin(2 * p + 0.3) + 0.11 * Math.Sin(3 * p + 0.9)
                               + 0.045 * Math.Sin(4 * p + 1.7) + 0.02 * Math.Sin(5 * p + 2.1)) / 1.25f;
            }
            return t;
        }

        internal override void Begin(byte pitch, byte velocity, long now, long end, long age)
        {
            // Still sounding (also fading out): go on from its level and phase, never restart from 0 (click).
            var wasSounding = Active;
            _freq = MusicMath.PitchToHz(pitch);
            _amp = (float)Math.Pow(velocity / 127.0, 0.7) * 0.62f;
            _attackStep = 1f / (0.035f * Rate);
            _releaseStep = 1f / (0.07f * Rate);
            _releasing = false;
            _start = now;
            EndSample = end;
            Age = age;
            if (!wasSounding)
            {
                _env = 0f;
                _phase = 0.0;
                _vibPhase = 0f;
            }
            var f = (float)(2.0 * Math.Sin(Math.PI * Math.Min(_freq * 2.0, Rate * 0.2) / Rate));
            _bpF = f;
            Active = true;
        }

        internal override void Render(float[] mono, int offset, int count, long now)
        {
            var dt = 1f / Rate;
            for (var i = 0; i < count; i++)
            {
                var sample = now + i;
                if (!_releasing && sample >= EndSample)
                {
                    _releasing = true;
                }
                if (_releasing)
                {
                    _env -= _releaseStep;
                    if (_env <= 0f)
                    {
                        _env = 0f;
                        Active = false;
                        return;
                    }
                }
                else if (_env < 1f)
                {
                    _env = Math.Min(1f, _env + _attackStep);
                }
                var age = (sample - _start) * dt;
                // Scoop: start 25 cents flat, up in 50 ms. Vibrato: 5.3 Hz, fades in from 0.25 s to 0.6 s.
                var scoop = age < 0.05f ? -0.0145f * (1f - age / 0.05f) : 0f;
                var vibDepth = age < 0.25f ? 0f : Math.Min(1f, (age - 0.25f) / 0.35f) * 0.0045f;
                _vibPhase += 5.3f * dt;
                if (_vibPhase > 1f)
                {
                    _vibPhase -= 1f;
                }
                var vib = (float)Math.Sin(_vibPhase * Math.PI * 2.0) * vibDepth;
                _phase += _freq * (1.0 + scoop + vib) / Rate;
                if (_phase >= 1.0)
                {
                    _phase -= 1.0;
                }
                var pos = _phase * TableSize;
                var idx = (int)pos;
                var frac = (float)(pos - idx);
                var tone = Table[idx] + (Table[idx + 1] - Table[idx]) * frac;

                // Breath: band-passed noise, stronger at the start (chiff).
                var n = Noise();
                _bpLow += _bpF * _bpBand;
                var high = n - _bpLow - 0.9f * _bpBand;
                _bpBand += _bpF * high;
                var chiff = age < 0.04f ? 1f - age / 0.04f : 0f;
                var breath = _bpBand * (0.10f + 0.55f * chiff) + n * 0.012f;

                mono[offset + i] += (tone + breath) * _env * _amp;
            }
        }
    }

    // Plucked string: delay line (Karplus-Strong) with all-pass fine tuning, pick-position comb on the pluck, decay
    // set by pitch; damped after the note length (at least MinRing).
    private sealed class StringVoice : Voice
    {
        private const int BufferSize = 4096;
        private const float MinRing = 0.5f;
        private readonly float[] _line = new float[BufferSize];
        private int _length;    // whole delay
        private float _apC;     // all-pass coefficient (fractional delay)
        private float _apX;
        private float _apY;
        private float _last;
        private int _write;
        private float _gain;    // loop gain (ring time)
        private float _dampGain;
        private bool _damped;
        private long _dampAt;
        private float _level;
        private int _silentCount;

        internal StringVoice(int rate, uint seed) : base(rate, seed)
        {
        }

        internal override void Begin(byte pitch, byte velocity, long now, long end, long age)
        {
            var f = MusicMath.PitchToHz(pitch);
            // Loop = delay + 0.5 (averaging filter) + all-pass fraction.
            var total = Rate / f - 0.5;
            var whole = (int)Math.Floor(total - 0.1);
            if (whole < 2)
            {
                whole = 2;
            }
            if (whole > BufferSize - 2)
            {
                whole = BufferSize - 2;
            }
            var frac = (float)(total - whole);
            _length = whole;
            _apC = (1f - frac) / (1f + frac);
            _apX = 0f;
            _apY = 0f;
            _last = 0f;
            _write = 0;
            // Ring time: low strings ~3.5 s, high ~1.2 s (T60).
            var t60 = 3.6 - (pitch - 45) * 0.06;
            t60 = Math.Max(1.1, Math.Min(3.6, t60));
            _gain = (float)Math.Pow(0.001, 1.0 / (t60 * f));
            _dampGain = (float)Math.Pow(0.001, 1.0 / (0.35 * f));
            _damped = false;
            _dampAt = Math.Max(end, now + (long)(MinRing * Rate));
            EndSample = _dampAt;
            Age = age;
            _silentCount = 0;

            // Pluck: string pulled at 18 % of its length (triangle shape: strong low harmonics, a full tone), plus a
            // little noise for the finger; softer pluck = rounder (low-passed) shape.
            var v = velocity / 127f;
            _level = 0.35f + 0.65f * v;
            var pick = Math.Max(1, (int)(_length * 0.18f));
            for (var i = 0; i < _length; i++)
            {
                var shape = i < pick ? i / (float)pick : (_length - i) / (float)(_length - pick);
                _line[i] = shape + Noise() * 0.12f;
            }
            var smooth = 0.35f + 0.5f * v;
            var lp = _line[0];
            for (var pass = 0; pass < 2; pass++)
            {
                for (var i = 0; i < _length; i++)
                {
                    lp += smooth * (_line[i] - lp);
                    _line[i] = lp;
                }
            }
            // Remove the offset of the burst (a DC step would thump).
            var mean = 0f;
            for (var i = 0; i < _length; i++)
            {
                mean += _line[i];
            }
            mean /= _length;
            var peak = 0.0001f;
            for (var i = 0; i < _length; i++)
            {
                _line[i] -= mean;
                peak = Math.Max(peak, Math.Abs(_line[i]));
            }
            var norm = _level / peak;
            for (var i = 0; i < _length; i++)
            {
                _line[i] *= norm;
            }
            Active = true;
        }

        internal override void ReleaseAt(long sample)
        {
            _dampAt = Math.Min(_dampAt, sample);
            EndSample = _dampAt;
        }

        internal override void Render(float[] mono, int offset, int count, long now)
        {
            for (var i = 0; i < count; i++)
            {
                if (!_damped && now + i >= _dampAt)
                {
                    _damped = true;
                }
                var x = _line[_write];
                // Loop: two-point average (string loss), ring gain, all-pass for the exact pitch.
                var avg = 0.5f * (x + _last);
                _last = x;
                var y = avg * (_damped ? _dampGain : _gain);
                var ap = _apC * y + _apX - _apC * _apY;
                _apX = y;
                _apY = ap;
                _line[_write] = ap;
                _write++;
                if (_write >= _length)
                {
                    _write = 0;
                }
                mono[offset + i] += x * 0.58f;
                if (x < 0.0005f && x > -0.0005f)
                {
                    if (++_silentCount > _length * 4)
                    {
                        Active = false;
                        return;
                    }
                }
                else
                {
                    _silentCount = 0;
                }
            }
        }
    }

    // Tambourine hit: skin part (sine with pitch drop + noise click) and jingle part (noise through three metal
    // resonances + ringing partials, retriggered in small bursts = jingles hitting each other).
    private sealed class TambourineVoice : Voice
    {
        private static readonly float[] PartialHz = { 3950f, 5230f, 6620f, 7810f, 9470f, 11300f };
        private readonly float[] _partialPhase = new float[6];
        private readonly float[] _partialStep = new float[6];
        private readonly float[] _bpLow = new float[3];
        private readonly float[] _bpBand = new float[3];
        private readonly float[] _bpF = new float[3];
        private TambourineHit _hit;
        private long _start;
        private float _amp;
        private float _skinPhase;
        private float _jingleEnv;
        private float _jingleDecay;
        private long _nextBurst;
        private int _burstsLeft;
        private float _skinEnv;
        private float _skinDecay;
        private float _skinHz;
        private float _skinHzEnd;
        private float _clickEnv;

        internal TambourineVoice(int rate, uint seed) : base(rate, seed)
        {
            var centres = new[] { 5200f, 7400f, 10200f };
            for (var b = 0; b < 3; b++)
            {
                _bpF[b] = (float)(2.0 * Math.Sin(Math.PI * Math.Min(centres[b], rate * 0.22) / rate));
            }
        }

        internal override void Begin(byte pitch, byte velocity, long now, long end, long age)
        {
            _hit = pitch <= 3 ? (TambourineHit)pitch : TambourineHit.Jingle;
            _start = now;
            EndSample = Math.Max(end, now + Rate / 10);
            Age = age;
            _amp = (float)Math.Pow(velocity / 127.0, 0.8);
            for (var p = 0; p < PartialHz.Length; p++)
            {
                var detune = 1f + Noise() * 0.03f;
                _partialStep[p] = PartialHz[p] * detune / Rate;
                _partialPhase[p] = (Noise() + 1f) * 0.5f;
            }
            _skinPhase = 0f;
            _jingleEnv = 0f;
            _skinEnv = 0f;
            _clickEnv = 0f;
            switch (_hit)
            {
                case TambourineHit.Thump:
                    _skinEnv = 1f;
                    _skinHz = 150f;
                    _skinHzEnd = 92f;
                    _skinDecay = Decay(0.16f);
                    _clickEnv = 0.6f;
                    _burstsLeft = 2;
                    _jingleDecay = Decay(0.11f);
                    break;
                case TambourineHit.Hit:
                    _skinEnv = 0.55f;
                    _skinHz = 330f;
                    _skinHzEnd = 260f;
                    _skinDecay = Decay(0.06f);
                    _clickEnv = 1f;
                    _burstsLeft = 3;
                    _jingleDecay = Decay(0.16f);
                    break;
                case TambourineHit.Shake:
                    _burstsLeft = 1000; // until released
                    _jingleDecay = Decay(0.09f);
                    break;
                default:
                    _burstsLeft = 2;
                    _jingleDecay = Decay(0.10f);
                    break;
            }
            _nextBurst = now;
            Active = true;
        }

        // Per-sample factor that falls to 1/1000 in t60 seconds.
        private float Decay(float t60) => (float)Math.Pow(0.001, 1.0 / (t60 * Rate));

        internal override void Render(float[] mono, int offset, int count, long now)
        {
            var dt = 1f / Rate;
            for (var i = 0; i < count; i++)
            {
                var sample = now + i;
                // Jingle bursts: first now, the next few 12-30 ms apart (shake: steady 20-28 per second until let go).
                if (_burstsLeft > 0 && sample >= _nextBurst)
                {
                    if (_hit == TambourineHit.Shake && sample >= EndSample)
                    {
                        _burstsLeft = 0;
                    }
                    else
                    {
                        var strength = _hit == TambourineHit.Shake
                            ? 0.45f + 0.25f * Noise()
                            : _burstsLeft >= 2 ? 1f : 0.55f;
                        _jingleEnv = Math.Max(_jingleEnv, strength);
                        _burstsLeft--;
                        var gap = _hit == TambourineHit.Shake ? 0.036f + 0.012f * Noise() : 0.016f + 0.01f * (Noise() + 1f);
                        _nextBurst = sample + (long)(gap * Rate);
                    }
                }
                var outSample = 0f;
                if (_jingleEnv > 0.0005f)
                {
                    var n = Noise();
                    var metal = 0f;
                    for (var b = 0; b < 3; b++)
                    {
                        _bpLow[b] += _bpF[b] * _bpBand[b];
                        var high = n - _bpLow[b] - 0.25f * _bpBand[b];
                        _bpBand[b] += _bpF[b] * high;
                        metal += _bpBand[b];
                    }
                    var ring = 0f;
                    for (var p = 0; p < PartialHz.Length; p++)
                    {
                        _partialPhase[p] += _partialStep[p];
                        if (_partialPhase[p] >= 1f)
                        {
                            _partialPhase[p] -= 1f;
                        }
                        ring += Tri(_partialPhase[p]);
                    }
                    outSample += (metal * 0.5f + ring * 0.09f) * _jingleEnv;
                    _jingleEnv *= _jingleDecay;
                }
                if (_skinEnv > 0.0005f)
                {
                    var age = (sample - _start) * dt;
                    var hz = _skinHzEnd + (_skinHz - _skinHzEnd) * (float)Math.Exp(-age / 0.03f);
                    _skinPhase += hz * dt;
                    if (_skinPhase >= 1f)
                    {
                        _skinPhase -= 1f;
                    }
                    outSample += (float)Math.Sin(_skinPhase * Math.PI * 2.0) * _skinEnv * 0.75f;
                    _skinEnv *= _skinDecay;
                }
                if (_clickEnv > 0.0005f)
                {
                    outSample += Noise() * _clickEnv * 0.35f;
                    _clickEnv *= 0.992f;
                }
                mono[offset + i] += outSample * _amp;
                if (_burstsLeft <= 0 && _jingleEnv <= 0.0005f && _skinEnv <= 0.0005f && _clickEnv <= 0.0005f)
                {
                    Active = false;
                    return;
                }
            }
        }

        // Triangle (softer than a square, brighter than a sine) from phase 0..1.
        private static float Tri(float p) => p < 0.5f ? p * 4f - 1f : 3f - p * 4f;
    }

    // Small fixed colour per instrument: lyre = wooden body (low bump, top cut); flute = slight top cut; tambourine
    // = flat (jingles need the highs).
    private sealed class BodyFilter
    {
        private readonly bool _bypass;
        private readonly float _b0, _b1, _b2, _a1, _a2;   // peaking biquad
        private float _x1, _x2, _y1, _y2;
        private readonly float _lp;
        private float _lpState;

        internal BodyFilter(int rate, InstrumentKind kind)
        {
            float peakHz, gainDb, q, lpHz;
            switch (kind)
            {
                case InstrumentKind.Lyre:
                    peakHz = 230f;
                    gainDb = 5f;
                    q = 1.1f;
                    lpHz = 5200f;
                    break;
                case InstrumentKind.Flute:
                    peakHz = 900f;
                    gainDb = 1.5f;
                    q = 0.8f;
                    lpHz = 7500f;
                    break;
                default:
                    _bypass = true;
                    peakHz = 1000f;
                    gainDb = 0f;
                    q = 1f;
                    lpHz = 20000f;
                    break;
            }
            // RBJ cookbook peaking EQ.
            var a = Math.Pow(10.0, gainDb / 40.0);
            var w = 2.0 * Math.PI * peakHz / rate;
            var alpha = Math.Sin(w) / (2.0 * q);
            var cos = Math.Cos(w);
            var a0 = 1.0 + alpha / a;
            _b0 = (float)((1.0 + alpha * a) / a0);
            _b1 = (float)(-2.0 * cos / a0);
            _b2 = (float)((1.0 - alpha * a) / a0);
            _a1 = (float)(-2.0 * cos / a0);
            _a2 = (float)((1.0 - alpha / a) / a0);
            _lp = (float)(1.0 - Math.Exp(-2.0 * Math.PI * Math.Min(lpHz, rate * 0.45) / rate));
        }

        internal float Process(float x)
        {
            if (_bypass)
            {
                return x;
            }
            var y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1;
            _x1 = x;
            _y2 = _y1;
            _y1 = y;
            _lpState += _lp * (y - _lpState);
            return _lpState;
        }
    }
}
