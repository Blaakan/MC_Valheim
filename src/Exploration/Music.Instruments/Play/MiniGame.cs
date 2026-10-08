using System.Collections.Generic;
using UnityEngine;

namespace MC.Exploration.MusicInstrumentsMod;

// Me = one mini-game run (state only; Performance drives me, MiniGameHud draws me). Clock = performance seconds since
// the start (lead-in included, frozen while the game is paused in single player). Chart notes fall in four lanes; a
// lane key on time plays the chart note's group of song notes (Performance schedule them); the success meter fills
// while the player is in flow and gives an Encore when full.
internal sealed class MiniGame
{
    internal const float LookAheadBase = 1.8f; // seconds of notes shown above the hit line at NoteSpeed 1
    internal const float Behind = 0.25f;       // seconds a missed note stays visible below the line

    internal readonly Chart Chart;
    internal readonly Judge Judge;
    internal readonly SuccessMeter Meter;
    internal readonly InstrumentKind Kind;
    internal readonly string Title;
    internal float Clock;
    internal int Encores;
    internal float LastEncoreAt = -99f;
    internal Judgement LastJudgement;
    internal float LastJudgementAt = -99f;
    internal int LastJudgementLane = -1;
    internal readonly float[] LanePressedAt = { -99f, -99f, -99f, -99f };
    internal readonly float[] LaneHitAt = { -99f, -99f, -99f, -99f };
    internal readonly List<KeyValuePair<int, int>> Visible = new List<KeyValuePair<int, int>>(64);
    internal readonly List<KeyValuePair<int, int>> MissedThisFrame = new List<KeyValuePair<int, int>>(8);
    // Missed notes still drawn a moment below the line: (loop, index) -> miss time.
    internal readonly List<KeyValuePair<KeyValuePair<int, int>, float>> RecentMisses = new List<KeyValuePair<KeyValuePair<int, int>, float>>(16);

    internal MiniGame(Chart chart, InstrumentKind kind, string title, float successSeconds, float successAccuracy)
    {
        Chart = chart;
        Kind = kind;
        Title = title;
        Judge = new Judge(chart, loop: true);
        Meter = new SuccessMeter { Seconds = successSeconds, Accuracy = successAccuracy };
    }

    internal float NoteSpeed => Plugin.NoteSpeedNow;

    // Seconds of notes shown above the hit line (faster notes = shorter window: same screen distance per second).
    internal float LookAhead => LookAheadBase / NoteSpeed;

    internal float NoteTime(int loop, int index) => Judge.NoteTime(loop, index);

    internal int Lane(int index) => Chart.Notes[index].Lane;

    internal float Accuracy => Judge.RecentAccuracy;

    internal float LeadInLeft => Mathf.Max(0f, Judge.LeadIn - Clock);

    internal string LaneLabel(int lane) => LaneKeys.Label(lane);

    // Visible list for the HUD: notes from Behind under the line up to LookAhead above it.
    internal void RefreshVisible()
    {
        Judge.Visible(Clock + LookAhead, Visible);
        for (var i = RecentMisses.Count - 1; i >= 0; i--)
        {
            if (Clock - RecentMisses[i].Value > Behind)
            {
                RecentMisses.RemoveAt(i);
            }
        }
    }
}
