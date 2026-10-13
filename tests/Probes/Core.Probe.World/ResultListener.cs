#if DEBUG
using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace MC.Core.ProbeWorldMod;

// One Warning / Error / Fatal log line (watch window, or read back from the log file).
internal sealed class LogEntry
{
    internal bool Error;
    internal string Source;
    internal string Text;

    internal string FirstLine
    {
        get
        {
            var cut = Text.IndexOf('\n');
            return cut > 0 ? Text.Substring(0, cut).TrimEnd() : Text;
        }
    }
}

// Each mod log its own "[selftest] PASS/FAIL" lines through its own SelfTest copy, so me cannot count calls. Me listen
// to BepInEx log instead (every source, also "Unity Log"): while a test run, count PASS, FAIL and other error lines.
// Log events can come from any thread: me lock.
internal sealed class ResultListener : ILogListener
{
    private const string PassMark = "[selftest] PASS ";
    private const string FailMark = "[selftest] FAIL ";

    private readonly object _gate = new object();
    private bool _counting;
    private int _pass;
    private int _fail;
    private int _errors;
    private string _firstError;
    private bool _watching;
    private List<LogEntry> _watched = new List<LogEntry>();

    internal void Begin()
    {
        lock (_gate)
        {
            _pass = 0;
            _fail = 0;
            _errors = 0;
            _firstError = null;
            _counting = true;
        }
    }

    internal void Snapshot(out int pass, out int fail, out int errors, out string firstError)
    {
        lock (_gate)
        {
            pass = _pass;
            fail = _fail;
            errors = _errors;
            firstError = _firstError;
        }
    }

    internal void Stop()
    {
        lock (_gate)
        {
            _counting = false;
            _watching = false;
        }
    }

    // Watch window: every Warning / Error / Fatal line of every source kept (not test verdict lines), until StopWatch.
    internal void StartWatch()
    {
        lock (_gate)
        {
            _watched = new List<LogEntry>();
            _watching = true;
        }
    }

    internal List<LogEntry> StopWatch()
    {
        lock (_gate)
        {
            _watching = false;
            return _watched;
        }
    }

    public void LogEvent(object sender, LogEventArgs eventArgs)
    {
        try
        {
            if ((!_counting && !_watching) || eventArgs == null)
            {
                return;
            }
            var text = eventArgs.Data as string ?? eventArgs.Data?.ToString();
            if (text == null)
            {
                return;
            }
            lock (_gate)
            {
                if (_watching && (eventArgs.Level & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) != 0
                    && !text.StartsWith(FailMark, StringComparison.Ordinal))
                {
                    _watched.Add(new LogEntry
                    {
                        Error = (eventArgs.Level & (LogLevel.Error | LogLevel.Fatal)) != 0,
                        Source = eventArgs.Source?.SourceName ?? "?",
                        Text = text,
                    });
                }
                if (!_counting)
                {
                    return;
                }
                if (text.StartsWith(PassMark, StringComparison.Ordinal))
                {
                    _pass++;
                }
                else if (text.StartsWith(FailMark, StringComparison.Ordinal))
                {
                    _fail++;
                }
                else if ((eventArgs.Level & (LogLevel.Error | LogLevel.Fatal)) != 0)
                {
                    _errors++;
                    if (_firstError == null)
                    {
                        var cut = text.IndexOf('\n');
                        _firstError = (eventArgs.Source?.SourceName ?? "?") + ": " + (cut > 0 ? text.Substring(0, cut).TrimEnd() : text);
                    }
                }
            }
        }
        catch
        {
            // Me never throw inside logger.
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
#endif
