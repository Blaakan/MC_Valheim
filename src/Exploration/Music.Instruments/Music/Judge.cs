using System;
using System.Collections.Generic;

namespace MC.Exploration.MusicInstrumentsMod;

internal enum Judgement : byte
{
    None,
    Perfect,
    Good,
    Miss,   // chart note passed unplayed
    Stray,  // key pressed with no note of that lane near
}

// Me = mini-game judge. Times in seconds of the performance clock (caller own it). Song loops: chart notes are read
// as (loop, index) with time = lead-in + loop * (song length + LoopGap) + chart time. Keeps the last judgements for a
// rolling accuracy (Perfect 1, Good 0.75, Miss and Stray 0) and the run of misses. A stray press counts at most once
// per lane every StrayRepeat seconds (mashing still loses, a shaky double press does not cost twice). Pure C#.
internal sealed class Judge
{
    internal const float PerfectWindow = 0.06f;
    internal const float GoodWindow = 0.13f;
    internal const float LeadIn = 2.5f;
    internal const float LoopGap = 1.5f;
    internal const float GoodWeight = 0.75f;
    internal const int RecentCount = 10;
    internal const int MinRecent = 4;
    internal const float StrayRepeat = 0.15f;

    private readonly Chart _chart;
    private readonly float _loopLength;
    private readonly bool _loop;
    private int _nextLoop;    // first unjudged chart note = (_nextLoop, _nextIndex)
    private int _nextIndex;
    // Hit ahead of the first unjudged one (out of order inside the window): key = loop*(N+1) + index.
    private readonly HashSet<long> _hitAhead = new HashSet<long>();
    private readonly float[] _recent = new float[RecentCount];
    private int _recentCount;
    private int _recentPos;
    private readonly float[] _lastStray = { -99f, -99f, -99f, -99f };

    internal int Hits { get; private set; }
    internal int Perfects { get; private set; }
    internal int Misses { get; private set; }
    internal int Strays { get; private set; }
    internal int Streak { get; private set; }
    internal int BestStreak { get; private set; }
    internal int MissStreak { get; private set; }

    internal Judge(Chart chart, bool loop)
    {
        _chart = chart;
        _loop = loop && chart.Notes.Length > 0;
        _loopLength = chart.Length + LoopGap;
    }

    internal Chart Chart => _chart;

    internal bool Finished => !Valid(_nextLoop, _nextIndex);

    internal float NoteTime(int loop, int index) => LeadIn + loop * _loopLength + _chart.Notes[index].Time;

    // Mean weight of the last RecentCount judgements; -1 while fewer than MinRecent.
    internal float RecentAccuracy
    {
        get
        {
            if (_recentCount < MinRecent)
            {
                return -1f;
            }
            var sum = 0f;
            for (var i = 0; i < _recentCount; i++)
            {
                sum += _recent[i];
            }
            return sum / _recentCount;
        }
    }

    // A chart note within window seconds of now (looping aware): false = long rest, the success meter waits.
    internal bool NoteNear(float now, float window)
    {
        var l = _nextLoop;
        var i = _nextIndex;
        for (var guard = 0; guard < 64 && Valid(l, i); guard++)
        {
            var t = NoteTime(l, i);
            if (t > now + window)
            {
                return false;
            }
            if (t >= now - window)
            {
                return true;
            }
            Step(ref l, ref i);
        }
        return false;
    }

    // Notes that passed their window unplayed = Miss. Call every frame before input. Returns misses this call; each
    // missed (loop, index) added to missed when given.
    internal int Expire(float now, List<KeyValuePair<int, int>> missed = null)
    {
        var count = 0;
        while (Valid(_nextLoop, _nextIndex) && now - NoteTime(_nextLoop, _nextIndex) > GoodWindow)
        {
            if (!_hitAhead.Remove(Key(_nextLoop, _nextIndex)))
            {
                Misses++;
                Streak = 0;
                MissStreak++;
                Record(0f);
                missed?.Add(new KeyValuePair<int, int>(_nextLoop, _nextIndex));
                count++;
            }
            Step(ref _nextLoop, ref _nextIndex);
        }
        return count;
    }

    // Key of a lane pressed at now. Hit = (loop, index) of the chart note; Stray = nothing of that lane near.
    internal Judgement Press(float now, int lane, out int loop, out int index)
    {
        loop = -1;
        index = -1;
        var bestDt = float.MaxValue;
        var l = _nextLoop;
        var i = _nextIndex;
        for (var guard = 0; guard < 64 && Valid(l, i); guard++)
        {
            var dt = NoteTime(l, i) - now;
            if (dt > GoodWindow)
            {
                break;
            }
            if (_chart.Notes[i].Lane == lane && !_hitAhead.Contains(Key(l, i)) && Math.Abs(dt) <= GoodWindow
                && Math.Abs(dt) < bestDt)
            {
                bestDt = Math.Abs(dt);
                loop = l;
                index = i;
            }
            Step(ref l, ref i);
        }
        if (index < 0)
        {
            if (lane >= 0 && lane < _lastStray.Length)
            {
                if (now - _lastStray[lane] < StrayRepeat)
                {
                    return Judgement.Stray; // same lane pressed again at once: counted already
                }
                _lastStray[lane] = now;
            }
            Strays++;
            Streak = 0;
            MissStreak++;
            Record(0f);
            return Judgement.Stray;
        }
        _hitAhead.Add(Key(loop, index));
        // Hit the first unjudged one: move on now (and past any already hit after it).
        while (Valid(_nextLoop, _nextIndex) && _hitAhead.Remove(Key(_nextLoop, _nextIndex)))
        {
            Step(ref _nextLoop, ref _nextIndex);
        }
        Hits++;
        Streak++;
        MissStreak = 0;
        BestStreak = Math.Max(BestStreak, Streak);
        if (bestDt <= PerfectWindow)
        {
            Perfects++;
            Record(1f);
            return Judgement.Perfect;
        }
        Record(GoodWeight);
        return Judgement.Good;
    }

    // Chart notes to draw: from the first unjudged one while their time is before until (loop-aware).
    internal void Visible(float until, List<KeyValuePair<int, int>> into)
    {
        into.Clear();
        var l = _nextLoop;
        var i = _nextIndex;
        for (var guard = 0; guard < 256 && Valid(l, i); guard++)
        {
            if (NoteTime(l, i) > until)
            {
                break;
            }
            if (!_hitAhead.Contains(Key(l, i)))
            {
                into.Add(new KeyValuePair<int, int>(l, i));
            }
            Step(ref l, ref i);
        }
    }

    private bool Valid(int loop, int index) => index < _chart.Notes.Length && (_loop || loop == 0);

    private void Step(ref int loop, ref int index)
    {
        index++;
        if (index >= _chart.Notes.Length && _loop)
        {
            index = 0;
            loop++;
        }
    }

    private long Key(int loop, int index) => (long)loop * (_chart.Notes.Length + 1) + index;

    private void Record(float weight)
    {
        _recent[_recentPos] = weight;
        _recentPos = (_recentPos + 1) % RecentCount;
        if (_recentCount < RecentCount)
        {
            _recentCount++;
        }
    }
}

// Me = the "played well long enough" meter. Fills by real playing time while the player is in flow (rolling accuracy
// of the last judgements at least the target, fewer than MaxMissStreak misses in a row), drains twice as fast out of
// flow, waits during a long rest (no chart note near). Full (Seconds) = success: true once, meter empty again (the
// next full meter renews the effect). Pure C#.
internal sealed class SuccessMeter
{
    internal const int MaxMissStreak = 3;
    internal const float DrainRate = 2f;
    internal const float RestWindow = 1.5f; // no chart note within this many seconds = long rest

    internal float Seconds = 20f;   // to fill
    internal float Accuracy = 0.7f; // rolling accuracy needed
    internal float Filled;          // seconds filled
    internal bool InFlow { get; private set; }
    internal bool Waiting { get; private set; }

    internal float Fraction => Seconds <= 0f ? 1f : MusicMath.Clamp(Filled / Seconds, 0f, 1f);

    // dt: real seconds since last call (0 while paused). accuracy: Judge.RecentAccuracy (-1 = too few yet).
    internal bool Update(float dt, float accuracy, int missStreak, bool noteNear)
    {
        Waiting = !noteNear;
        InFlow = accuracy >= 0f && accuracy >= Accuracy && missStreak < MaxMissStreak;
        if (Waiting || dt <= 0f)
        {
            return false;
        }
        if (InFlow)
        {
            Filled += dt;
            if (Filled >= Seconds)
            {
                Filled = 0f;
                return true;
            }
        }
        else if (accuracy >= 0f)
        {
            Filled = Math.Max(0f, Filled - DrainRate * dt);
        }
        return false;
    }
}
