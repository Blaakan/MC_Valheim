using System;
using UnityEngine;

namespace MC.Exploration.MusicInstrumentsMod;

// Me = free play keyboard, by place on the keyboard, like a piano (FreePlayMap key index = semitone):
//   lower octave  white Z X C V B N M, black S D (C# D#) and G H J (F# G# A#) on the row above
//   upper octave  white Q W E R T Y U I O P, black 2 3, 5 6 7, 9 0 on the number row
// Space held = octave up. ZInput read keys through the Input System, whose keys are places (US names), so an AZERTY or
// other layout keep the piano shape; labels show the player's own key names. Raw ZInput key calls (exact frame, not the
// player's rebinds); the game's own keys are held back by KeyCapture while free play runs.
internal static class FreePlayKeys
{
    private static readonly KeyCode[] Keys =
    {
        KeyCode.Z, KeyCode.S, KeyCode.X, KeyCode.D, KeyCode.C, KeyCode.V, KeyCode.G, KeyCode.B, KeyCode.H, KeyCode.N,
        KeyCode.J, KeyCode.M,
        KeyCode.Q, KeyCode.Alpha2, KeyCode.W, KeyCode.Alpha3, KeyCode.E, KeyCode.R, KeyCode.Alpha5, KeyCode.T,
        KeyCode.Alpha6, KeyCode.Y, KeyCode.Alpha7, KeyCode.U,
        KeyCode.I, KeyCode.Alpha9, KeyCode.O, KeyCode.Alpha0, KeyCode.P,
    };

    private static readonly string[] Labels = new string[FreePlayMap.KeyCount];

#if DEBUG
    // Self test: press / let go / hold a key without a keyboard (read once by Down / Up).
    internal static readonly bool[] TestDown = new bool[FreePlayMap.KeyCount];
    internal static readonly bool[] TestUp = new bool[FreePlayMap.KeyCount];
    internal static readonly bool[] TestHeld = new bool[FreePlayMap.KeyCount];
    internal static bool TestOctave;

    internal static void ClearTest()
    {
        Array.Clear(TestDown, 0, TestDown.Length);
        Array.Clear(TestUp, 0, TestUp.Length);
        Array.Clear(TestHeld, 0, TestHeld.Length);
        TestOctave = false;
    }
#endif

    internal static KeyCode Key(int key) => Keys[key];

    internal static bool Down(int key)
    {
#if DEBUG
        if (TestDown[key])
        {
            TestDown[key] = false;
            TestHeld[key] = true;
            return true;
        }
#endif
        return ZInput.GetKeyDown(Keys[key], false);
    }

    internal static bool Up(int key)
    {
#if DEBUG
        if (TestUp[key])
        {
            TestUp[key] = false;
            TestHeld[key] = false;
            return true;
        }
#endif
        return ZInput.GetKeyUp(Keys[key], false);
    }

    internal static bool Held(int key)
    {
#if DEBUG
        if (TestHeld[key])
        {
            return true;
        }
#endif
        return ZInput.GetKey(Keys[key], false);
    }

    // Space held: everything an octave up.
    internal static bool OctaveUp
    {
        get
        {
#if DEBUG
            if (TestOctave)
            {
                return true;
            }
#endif
            return ZInput.GetKey(KeyCode.Space, false);
        }
    }

    // Player's own name of the key ("Z" on QWERTY, "W" on AZERTY), built once.
    internal static string Label(int key)
    {
        var label = Labels[key];
        if (label != null)
        {
            return label;
        }
        var code = Keys[key];
        string name = null;
        try
        {
            name = ZInput.KeyCodeToDisplayName(code);
        }
        catch (Exception)
        {
            // Plain enum name below.
        }
        if (string.IsNullOrEmpty(name) || name.StartsWith("$", StringComparison.Ordinal))
        {
            name = code >= KeyCode.Alpha0 && code <= KeyCode.Alpha9 ? ((int)(code - KeyCode.Alpha0)).ToString() : code.ToString();
        }
        Labels[key] = name.ToUpperInvariant();
        return Labels[key];
    }
}
