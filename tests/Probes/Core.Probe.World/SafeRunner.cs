#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MC.Core.ProbeWorldMod;

// What happened to one run of SafeRunner.
internal sealed class RunResult
{
    internal bool TimedOut;
    internal Exception Error;
    internal float Seconds;
    internal string Warning;
}

// Me step a test coroutine myself (stack of enumerators) instead of handing it to Unity, so me can:
//   catch what MoveNext throw (Unity would only log it and drop the coroutine);
//   stop it at timeout even in the middle of a wait, then Dispose every level (test "finally" blocks run = cleanup).
// Understood yields: null (next frame), nested IEnumerator (also WaitForSecondsRealtime / WaitUntil / WaitWhile,
// they are CustomYieldInstruction = IEnumerator), WaitForSeconds (scaled time, me wait it frame by frame),
// AsyncOperation (poll isDone), WaitForEndOfFrame / WaitForFixedUpdate (given to Unity). A Coroutine object is
// given to Unity too: me cannot time it out (Warning set). Anything else = next frame.
internal static class SafeRunner
{
    // Many steps without a yield (nested enumerators that end at once) = me give one frame, game never freeze.
    private const int MaxStepsPerFrame = 500;

    private static readonly FieldInfo WaitSecondsField =
        typeof(WaitForSeconds).GetField("m_Seconds", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    internal static IEnumerator Run(IEnumerator root, float timeoutSeconds, RunResult result)
    {
        var start = Time.realtimeSinceStartup;
        var stack = new Stack<IEnumerator>();
        if (root != null)
        {
            stack.Push(root);
        }
        var steps = 0;
        while (stack.Count > 0)
        {
            if (Time.realtimeSinceStartup - start > timeoutSeconds)
            {
                result.TimedOut = true;
                break;
            }

            var top = stack.Peek();
            bool moved;
            object current = null;
            try
            {
                moved = top.MoveNext();
                if (moved)
                {
                    current = top.Current;
                }
            }
            catch (Exception e)
            {
                result.Error = e;
                break;
            }

            object give = null;
            var yieldNow = true;
            if (!moved)
            {
                stack.Pop();
                DisposeQuietly(top, result);
                yieldNow = false; // parent go on this frame, like Unity nested coroutine
            }
            else
            {
                switch (current)
                {
                    case null:
                        break;
                    case IEnumerator nested:
                        stack.Push(nested);
                        yieldNow = false;
                        break;
                    case WaitForSeconds wait:
                        if (WaitSecondsField != null && WaitSecondsField.GetValue(wait) is float seconds)
                        {
                            stack.Push(WaitScaled(seconds));
                            yieldNow = false;
                        }
                        else
                        {
                            give = wait;
                            result.Warning = "WaitForSeconds given to Unity (no timeout while it waits)";
                        }
                        break;
                    case AsyncOperation op:
                        stack.Push(WaitAsync(op));
                        yieldNow = false;
                        break;
                    case Coroutine co:
                        give = co;
                        result.Warning = "a started Coroutine was yielded: yield its IEnumerator instead so the timeout can stop it";
                        break;
                    case YieldInstruction other:
                        give = other; // WaitForEndOfFrame, WaitForFixedUpdate: short
                        break;
                }
            }

            if (!yieldNow && ++steps < MaxStepsPerFrame)
            {
                continue;
            }
            steps = 0;
            yield return give;
        }

        // Timeout or throw: close every level still open (runs their finally blocks).
        while (stack.Count > 0)
        {
            DisposeQuietly(stack.Pop(), result);
        }
        result.Seconds = Time.realtimeSinceStartup - start;
    }

    private static IEnumerator WaitScaled(float seconds)
    {
        var end = Time.time + seconds;
        while (Time.time < end)
        {
            yield return null;
        }
    }

    private static IEnumerator WaitAsync(AsyncOperation op)
    {
        while (op != null && !op.isDone)
        {
            yield return null;
        }
    }

    private static void DisposeQuietly(IEnumerator e, RunResult result)
    {
        try
        {
            (e as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            if (result.Error == null)
            {
                result.Error = ex;
            }
        }
    }
}
#endif
