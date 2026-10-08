using MC.Shared;

namespace MC.Exploration.SleepThroughDayMod;

// Me = server part (server or host only; Game.UpdateSleeping run there every 2 s). Me keep vanilla sleep flow and
// only (a) start sleep in morning, where vanilla never start, with vanilla's own three steps, and (b) move end of
// every skip that start inside day window to wake hour of same date. Vanilla stop branch end every sleep.
internal static class DaySleep
{
    private const double MinSecondsBetweenSleeps = 10.0; // vanilla guard in Game.UpdateSleeping
    private const double FallbackSkipSeconds = 12.0;     // vanilla EnvMan.c_TimeSkipDuration, only when skip look broken
    private const double MaxSkipSeconds = 600.0;

    // "Everyone in bed but outside window" Debug line: once until next sleep.
    private static bool _outsideWindowLogged;

    internal static void Reset() => _outsideWindowLogged = false;

#if DEBUG
    // Debug build only: self test read it (forced time of day case).
    internal static bool OutsideWindowLogged => _outsideWindowLogged;
#endif

    // Prefix part. Stop due this tick (sleep run, skip done) = send final time now. NetTime go over same ZRpc as
    // SleepStop that follow, ZRpc keep order: every client wake up with right clock (right date for message).
    internal static void BeforeTick(Game game)
    {
        var net = ZNet.instance;
        var env = EnvMan.instance;
        if (net == null || env == null || !net.IsServer())
        {
            return;
        }
        if (game.m_sleeping && !env.IsTimeSkipping())
        {
            net.SendNetTime();
        }
    }

    // Postfix part. wasSleeping = sleep already ran when tick began (then stop branch own it, me no touch).
    internal static void AfterTick(Game game, bool wasSleeping)
    {
        var net = ZNet.instance;
        var env = EnvMan.instance;
        if (wasSleeping || net == null || env == null || !net.IsServer())
        {
            return;
        }

        var justStarted = game.m_sleeping && env.IsTimeSkipping();
        if (!justStarted && !DayClock.IsMorning())
        {
            return; // afternoon or night tick, nothing new: vanilla already decide. Cheap exit.
        }

        var now = net.GetTimeSeconds();
        if (justStarted)
        {
            _outsideWindowLogged = false; // new sleep: next "outside window" line may show again
        }
        else
        {
            // Morning, nothing started: vanilla time test failed. Same other conditions as vanilla first (cheap ones
            // first; EverybodyIsTryingToSleep build one list of player ZDOs, as vanilla do each tick at night).
            if (game.m_sleeping || env.IsTimeSkipping() || now - game.m_lastSleepTime < MinSecondsBetweenSleeps
                || ZRoutedRpc.instance == null || !game.EverybodyIsTryingToSleep())
            {
                return;
            }
        }

        Plugin.ReadDaySettings(out var wakeHour, out var includeAfternoon);
        var window = DayClock.Today(env, now, wakeHour, includeAfternoon);
        if (!window.InWindow(now))
        {
            if (!justStarted)
            {
                MaybeLogOutsideWindow(env, now, window);
            }
            return;
        }

        if (justStarted)
        {
            // Game (afternoon with IncludeAfternoon on, or first second after 06:00 while flags still say night) or
            // other mod ("sleep any time") just started sleep inside window. Its end later than ours = me move it.
            if (env.m_skipToTime > window.Target)
            {
                Retarget(env, now, window.Target);
                Log.Info($"Day sleep: a sleep started at {Clock(env, now)} by the game or another mod now ends at "
                         + $"{DayClock.HourText(window.WakeHour)} instead of the next morning.");
            }
            return;
        }

        StartDaySleep(game, env, now, window);
    }

    // Keep skip length that game (or skip-speed / season mod) chose; move only its end. Never switch skip on.
    // Return skip length in real seconds.
    internal static double Retarget(EnvMan env, double now, double target)
    {
        var duration = (env.m_skipToTime - now) / env.m_timeSkipSpeed;
        if (double.IsNaN(duration) || double.IsInfinity(duration) || duration <= 0.0 || duration > MaxSkipSeconds)
        {
            duration = FallbackSkipSeconds;
        }
        if (target <= now)
        {
            return duration; // nothing ahead to aim at (window say this never happen)
        }
        env.m_skipToTime = target;
        env.m_timeSkipSpeed = (target - now) / duration;
        return duration;
    }

    // Everything that can throw (other mods' SkipToMorning patches, retarget, log text) happen BEFORE m_sleeping
    // and SleepStart: failure leave at worst plain skip, never player stuck asleep.
    private static void StartDaySleep(Game game, EnvMan env, double now, DayWindow window)
    {
        env.SkipToMorning(); // vanilla call: other mods' patches on it still run (Seasons: own speed)
        string text;
        if (env.IsTimeSkipping())
        {
            var duration = Retarget(env, now, window.Target);
            text = $"Day sleep: everyone is in bed at {Clock(env, now)} (day {window.Day}). Waking up at "
                   + $"{DayClock.HourText(window.WakeHour)}: skipping {window.Target - now:0} s of world time in "
                   + $"{duration:0.#} s (day length {window.DayLength:0} s).";
        }
        else
        {
            Log.Debug("Day sleep: SkipToMorning started no time skip (another mod?); the sleep runs without one.");
            text = $"Day sleep: everyone is in bed at {Clock(env, now)} (day {window.Day}); no time skip.";
        }

        game.m_sleeping = true;
        _outsideWindowLogged = false;
        ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "SleepStart");
        Log.Info(text);
    }

    // Everyone in bed, flags say morning, but raw time far outside day (before 06:00 or after 18:00): forced time of
    // day (tod, time mods). Nobody start sleep then. Me say why once (Debug) until next sleep.
    private static void MaybeLogOutsideWindow(EnvMan env, double now, DayWindow window)
    {
        if (_outsideWindowLogged)
        {
            return;
        }
        var nightfall = window.DayStart + DayClock.RawFractionForHour(env, DayClock.NightfallHour) * window.DayLength;
        if (now >= window.Dawn && now <= nightfall)
        {
            return; // only smoothed clock lag near noon: normal, nothing to say
        }
        _outsideWindowLogged = true;
        Log.Debug($"Day sleep not started: everyone is in bed, but the world time {Clock(env, now)} is outside the day "
                  + $"window {DayClock.HourText(DayClock.DawnHour)}-{DayClock.HourText(window.LatestStartHour)} "
                  + "(forced time of day?).");
    }

    private static string Clock(EnvMan env, double t) => DayClock.ClockText(DayClock.FractionAt(env, t));
}
