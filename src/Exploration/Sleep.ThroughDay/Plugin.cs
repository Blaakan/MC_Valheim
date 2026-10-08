using BepInEx.Configuration;
using MC.Shared;

namespace MC.Exploration.SleepThroughDayMod;

// Me = plugin. Attributes, guid, descriptor come from csproj (ModInfo.g.cs). Life cycle from ModPlugin:
// Enabled toggle, dependencies, server check, status, live on/off. Patches in Patches/ apply only while Active.
// No Awake/Start/Update/OnDestroy here (they hide ModPlugin ones). Use BindConfig / OnActivated / OnDeactivated.
// Me let player lie down in morning (client) and make day sleep end at nightfall same date (server). Both side:
// server start and aim the time skip, each game allow bed in morning and say "Good evening" where vanilla say
// "Good morning".
// Me save nothing, make no object: off = patches gone = vanilla beds.
internal sealed partial class Plugin : ModPlugin
{
    internal static ConfigEntry<float> WakeUpHour;
    internal static ConfigEntry<bool> IncludeAfternoon;
    internal static ConfigEntry<string> WakeUpMessage;

    // Me read all three when used (no cache): edit count at next sleep.
    protected override void BindConfig()
    {
        WakeUpHour = Config.Bind("General", "WakeUpHour", 18f, new ConfigDescription(
            "In-game hour at which you wake up after sleeping during the day. 18 = nightfall, when the game switches to "
            + "night (vanilla sleep wakes you at 6). Range 13 to 23; decimals allowed (18.5 = 18:30, 21 = 9 PM). Sleeps "
            + "that start in the morning (06:00 to noon) wake you at this hour; with IncludeAfternoon on, sleeps that start "
            + "in the afternoon do too, until one in-game hour before this hour. Every other sleep skips to the next "
            + "morning as usual. In multiplayer the server's (or host's) setting is used.",
            new AcceptableValueRange<float>(13f, 23f),
            new ConfigurationManagerAttributes { Order = 90 }));
        IncludeAfternoon = Config.Bind("General", "IncludeAfternoon", false, new ConfigDescription(
            "Off (default): only sleeps that start in the morning (06:00 to noon) wake you at the wake-up hour; afternoon "
            + "sleeps skip to the next morning, as in vanilla. On: sleeps that start in the afternoon also wake you at the "
            + "wake-up hour, until one in-game hour before it. In multiplayer the server's (or host's) setting is used.",
            null,
            new ConfigurationManagerAttributes { Order = 85 }));
        WakeUpMessage = Config.Bind("General", "WakeUpMessage", "Good evening", new ConfigDescription(
            "Message shown when you wake up from a daytime sleep, instead of \"Good morning\". As with \"Good morning\", "
            + "the game's \"You feel rested\" message replaces it at once if you were not Rested when you lay down. Game "
            + "text keys work: $msg_goodnight shows the game's own \"Good night\" in your language. Leave empty to keep "
            + "\"Good morning\".",
            null,
            new ConfigurationManagerAttributes { Order = 80 }));
    }

    // Server settings as used now (wake hour made safe). Only door to WakeUpHour and IncludeAfternoon values.
    // Debug build: self test may force them in memory (SleepSelfTests overrides), never in config file.
    internal static void ReadDaySettings(out float wakeHour, out bool includeAfternoon)
    {
        var hour = WakeUpHour.Value;
        includeAfternoon = IncludeAfternoon.Value;
#if DEBUG
        if (SleepSelfTests.WakeHourOverride.HasValue)
        {
            hour = SleepSelfTests.WakeHourOverride.Value;
        }
        if (SleepSelfTests.IncludeAfternoonOverride.HasValue)
        {
            includeAfternoon = SleepSelfTests.IncludeAfternoonOverride.Value;
        }
#endif
        wakeHour = DayClock.WakeHour(hour);
    }

    // WakeUpMessage as written by player (not trimmed, never null). Only door to its value.
    // Debug build: self test may force it in memory (SleepSelfTests override), never in config file.
    internal static string ReadWakeUpMessage()
    {
#if DEBUG
        if (SleepSelfTests.WakeUpMessageOverride != null)
        {
            return SleepSelfTests.WakeUpMessageOverride;
        }
#endif
        return WakeUpMessage.Value ?? "";
    }

#if DEBUG
    // Debug build only: self test take feature down and up in memory, with the same two steps framework do when
    // Enabled change (OnDeactivated + patches gone / patches on + OnActivated). Nothing written: not Enabled, not
    // even framework's Status line in config file. Release build no have this.
    internal void TestSetPatched(bool on)
    {
        if (on)
        {
            ApplyPatches(Harmony);
            OnActivated();
        }
        else
        {
            OnDeactivated();
            Harmony.UnpatchSelf();
        }
    }
#endif

    // Me just turned on. Nothing to build: patches do all. Debug build: in-world self tests join the list.
    protected override void OnActivated()
    {
        SleepSelfTests.Register();
    }

    // Me going off (patches still on). Forget sleep start me remember; self tests leave list, undo what they made.
    protected override void OnDeactivated()
    {
        WakeMessage.Reset();
        DaySleep.Reset();
        SleepSelfTests.Unregister();
    }
}
