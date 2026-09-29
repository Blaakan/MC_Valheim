#if DEBUG
using System;
using BepInEx.Logging;

namespace MC.Core.ProbeWorldMod;

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
        }
    }

    public void LogEvent(object sender, LogEventArgs eventArgs)
    {
        try
        {
            if (!_counting || eventArgs == null)
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
