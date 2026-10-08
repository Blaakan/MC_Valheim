using MC.Shared;

namespace MC.Exploration.SleepThroughDayMod;

// Me = client part of wake-up. Me remember date and "after 06:00?" (raw world time) when local player fall asleep.
// Wake from day sleep = me swap vanilla "$msg_goodmorning" for WakeUpMessage AT ITS SOURCE: SetSleeping prefix arm
// swap text, Player.Message prefix change that one message, SetSleeping postfix disarm. Vanilla order stay: wake
// text first, then Rested start message (that one replace wake text in same frame when Rested is new, exactly as it
// replace "Good morning" in vanilla). Me never show message after SetSleeping: Rested message must stay last.
// Client need no server setting: day sleep start after 06:00 and wake before next date's 05:00 (IsDaySleepWake);
// any other sleep that start after 06:00 wake at later date's morning. Server send final time just before SleepStop
// (DaySleep.BeforeTick): wake time is right.
// State = few statics, one local player per game. Reset on use and when feature go off.
internal static class WakeMessage
{
    internal const string GoodMorningToken = "$msg_goodmorning";

    // Day sleep target at most 23:00 same date, but stop can come late: server wait for dream video on host
    // (Game.UpdateSleeping wait CinematicsManager.IsPlaying). With other players online game no pause (Game.CanPause),
    // world time run on, so wake can pass midnight. Wake on next date before this hour still = day sleep. Every other
    // sleep that start after 06:00 wake at next 06:00 or later: far from it.
    private const float LateWakeLimitHour = 5f;

    private static bool _haveStart;
    private static int _startDay;
    private static bool _startedAfterDawn;

    // Armed only inside one SetSleeping(false) call of day-sleep wake. Null = nothing armed: Message prefix read
    // only this for every other message.
    internal static string SwapText;
    private static bool _swapped;

    internal static void Reset()
    {
        _haveStart = false;
        _startDay = 0;
        _startedAfterDawn = false;
        SwapText = null;
        _swapped = false;
    }

    // SetSleeping prefix. Local player asleep and call say wake = decide day sleep now (time no move inside call),
    // arm swap text when WakeUpMessage not empty. Any other call: disarm (leftover of call that threw).
    internal static void Before(Player player, bool sleep)
    {
        SwapText = null;
        _swapped = false;
        if (sleep || player == null || player != Player.m_localPlayer || !player.m_sleeping)
        {
            return;
        }
        var env = EnvMan.instance;
        var net = ZNet.instance;
        if (env == null || net == null || !IsDaySleepWake(env, net.GetTimeSeconds()))
        {
            return;
        }
        var text = Plugin.ReadWakeUpMessage().Trim();
        if (text.Length > 0)
        {
            SwapText = text;
        }
    }

#if DEBUG
    // Debug build only: self test read what me remember of sleep start.
    internal static bool HaveStart => _haveStart;
    internal static int StartDay => _startDay;
    internal static bool StartedAfterDawn => _startedAfterDawn;
#endif

    // Player.Message prefix, only while armed: vanilla "Good morning" of this wake become WakeUpMessage (MessageHud
    // localize $tokens). Once per wake.
    internal static void Swap(Player player, MessageHud.MessageType type, ref string msg)
    {
        if (_swapped || SwapText == null || type != MessageHud.MessageType.Center || msg != GoodMorningToken
            || player != Player.m_localPlayer)
        {
            return;
        }
        msg = SwapText;
        _swapped = true;
    }

    // SetSleeping postfix. wasSleeping = state before call; act only on real change (read state call made, not
    // argument: right even when other mod skip original). Disarm first, whatever happen.
    internal static void After(Player player, bool wasSleeping)
    {
        var text = SwapText;
        var swapped = _swapped;
        SwapText = null;
        _swapped = false;

        if (player == null || player != Player.m_localPlayer)
        {
            return;
        }
        var sleeping = player.m_sleeping;
        if (sleeping == wasSleeping)
        {
            return;
        }

        var env = EnvMan.instance;
        var net = ZNet.instance;
        if (env == null || net == null)
        {
            Reset();
            return;
        }

        var now = net.GetTimeSeconds();
        if (sleeping)
        {
            _startDay = env.GetDay(now);
            _startedAfterDawn = DayClock.IsAfterDawn(env, now);
            _haveStart = true;
            Log.Debug($"Sleep started at {Clock(env, now)}, day {_startDay} ({(_startedAfterDawn ? "after dawn" : "before dawn")}).");
            return;
        }

        var day = env.GetDay(now);
        var daySleep = IsDaySleepWake(env, now);
        Reset();
        if (!daySleep)
        {
            Log.Debug($"Woke up at {Clock(env, now)}, day {day}: night sleep, vanilla message.");
        }
        else if (swapped)
        {
            Log.Debug($"Woke up at {Clock(env, now)}, day {day}: daytime sleep, message \"{text}\" instead of \"Good morning\".");
        }
        else if (text == null)
        {
            Log.Debug($"Woke up at {Clock(env, now)}, day {day}: daytime sleep, WakeUpMessage empty, vanilla message kept.");
        }
        else
        {
            Log.Debug($"Woke up at {Clock(env, now)}, day {day}: daytime sleep, but the game showed no \"Good morning\" "
                      + "to replace (another mod?).");
        }
        SleepSelfTests.RecordWake(now, daySleep);
    }

    // Start after 06:00 of its date, wake same date, or next date before LateWakeLimitHour (late stop, above).
    private static bool IsDaySleepWake(EnvMan env, double now)
    {
        if (!_haveStart || !_startedAfterDawn)
        {
            return false;
        }
        var day = env.GetDay(now);
        if (day == _startDay)
        {
            return true;
        }
        return day == _startDay + 1 && DayClock.FractionAt(env, now) < LateWakeLimitHour / 24f;
    }

    private static string Clock(EnvMan env, double t) => DayClock.ClockText(DayClock.FractionAt(env, t));
}
