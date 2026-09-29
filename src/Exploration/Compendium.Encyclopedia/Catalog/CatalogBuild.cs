using System;
using System.Collections;
using System.Diagnostics;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.CompendiumEncyclopediaMod;

/// <summary>
/// Me = the one place that own the catalog: build it lazily over frames on the plugin's own MonoBehaviour (always
/// active; window get SetActive(false) and would kill a coroutine on it), then swap it in with one assignment.
/// UI call <see cref="EnsureReady"/> on every open: finished catalog matching world data = returned; else a build
/// start (if none alive) and null come back, and <see cref="CatalogReady"/> fire when done.
/// Rules (design 3.2.2): each step check ObjectDB/ZNetScene still the ones it started with (world ended = stop); a
/// build whose last step is older than 1 s (unscaled) = dead (Unity skip iterator finally on stop), new one start;
/// non-critical stage throw = Warning, others still run; critical stage or driver throw = PatchGuard, abandoned.
/// Main thread only.
/// </summary>
internal static class CatalogService
{
    internal const float BudgetMs = 4f;
    internal const float StallSeconds = 1f;

    private static MonoBehaviour _host;
    private static Coroutine _coroutine;
    private static int _generation;
    private static bool _running;
    private static float _lastStep;
    private static ObjectDB _buildDb;
    private static ZNetScene _buildScene;

    /// <summary>Last finished catalog (maybe of an older world: use <see cref="Ready"/> or <see cref="EnsureReady"/>).</summary>
    internal static Catalog Current { get; private set; }

    /// <summary>
    /// Fire (main thread, from the build coroutine) when a new catalog is swapped in. Each subscriber guarded: one
    /// throwing never stop the others. UI: rebind the list if window open.
    /// </summary>
    internal static event Action CatalogReady;

    /// <summary>Plugin set this in OnActivated (the BepInEx manager object, always active).</summary>
    internal static void SetHost(MonoBehaviour host) => _host = host;

    /// <summary>A build is alive (started, stepped less than 1 s ago).</summary>
    internal static bool IsBuilding => _running && Time.unscaledTime - _lastStep <= StallSeconds;

    /// <summary>Finished catalog matching current world data, or null. Never start a build.</summary>
    internal static Catalog Ready
    {
        get
        {
            var cat = Current;
            if (cat == null)
            {
                return null;
            }
            var db = ObjectDB.instance;
            var scene = ZNetScene.instance;
            if (db == null || scene == null)
            {
                return null;
            }
            return cat.Matches(Fingerprint.Take(db, scene, cat.PieceTables)) ? cat : null;
        }
    }

    /// <summary>
    /// Ready catalog, or null while one is built (build started here when none alive). Null also when no world
    /// (no ObjectDB / ZNetScene) or no host. Cheap when ready: fingerprint compare.
    /// </summary>
    internal static Catalog EnsureReady()
    {
        var ready = Ready;
        if (ready != null)
        {
            return ready;
        }
        var db = ObjectDB.instance;
        var scene = ZNetScene.instance;
        if (db == null || scene == null || _host == null)
        {
            return null;
        }
        if (_running)
        {
            var sameWorld = ReferenceEquals(_buildDb, db) && ReferenceEquals(_buildScene, scene);
            if (sameWorld && Time.unscaledTime - _lastStep <= StallSeconds)
            {
                return null; // alive, keep waiting
            }
            Log.Debug(sameWorld ? "Catalog build restarted (previous build stalled)." : "Catalog build restarted (world changed).");
            StopCoroutine();
        }
        Start(db, scene);
        return null;
    }

    /// <summary>OnDeactivated: stop build, forget handle. Catalog kept? No: cleared too (feature off).</summary>
    internal static void Stop()
    {
        StopCoroutine();
        Current = null;
        _host = null;
    }

    private static void StopCoroutine()
    {
        _generation++;
        if (_coroutine != null && _host != null)
        {
            try
            {
                _host.StopCoroutine(_coroutine);
            }
            catch (Exception e)
            {
                Log.Debug($"Catalog build stop: {e.Message}");
            }
        }
        _coroutine = null;
        _running = false;
        _buildDb = null;
        _buildScene = null;
    }

    private static void Start(ObjectDB db, ZNetScene scene)
    {
        _generation++;
        _running = true;
        _lastStep = Time.unscaledTime;
        _buildDb = db;
        _buildScene = scene;
        Log.Debug("Catalog build started.");
        _coroutine = _host.StartCoroutine(Drive(_generation, db, scene));
    }

    private static bool StillValid(int gen, ObjectDB db, ZNetScene scene)
    {
        return gen == _generation && db != null && scene != null && ReferenceEquals(ObjectDB.instance, db)
               && ReferenceEquals(ZNetScene.instance, scene);
    }

    // Driver. No yield inside try/catch (C#), so each step is: check, try MoveNext, yield outside.
    private static IEnumerator Drive(int gen, ObjectDB db, ZNetScene scene)
    {
        var total = Stopwatch.StartNew();
        var budget = new BuildBudget(BudgetMs);
        // Counts at start, before any stage copy a game list: something added while me build (a server-synced mod)
        // then make the published fingerprint differ from the world, and the next open build again.
        var startPrint = Fingerprint.Take(db, scene, null);
        CatalogBuilder builder;
        System.Collections.Generic.List<BuildStage> stages;
        try
        {
            builder = new CatalogBuilder(db, scene, budget);
            stages = builder.Stages();
        }
        catch (Exception e)
        {
            PatchGuard.Report("Catalog build", e);
            End(gen);
            yield break;
        }

        var frames = 1;
        var stageLog = new System.Text.StringBuilder();
        budget.StartFrame();
        foreach (var stage in stages)
        {
            var stageWatch = Stopwatch.StartNew();
            IEnumerator run;
            try
            {
                run = stage.Run();
            }
            catch (Exception e)
            {
                if (!StageFailed(stage, e, gen))
                {
                    yield break;
                }
                continue;
            }
            while (true)
            {
                if (!StillValid(gen, db, scene))
                {
                    if (gen == _generation)
                    {
                        Log.Debug("Catalog build abandoned (world ended).");
                        End(gen);
                    }
                    yield break;
                }
                bool more;
                try
                {
                    more = run.MoveNext();
                }
                catch (Exception e)
                {
                    if (!StageFailed(stage, e, gen))
                    {
                        yield break;
                    }
                    more = false;
                }
                _lastStep = Time.unscaledTime;
                if (!more)
                {
                    break;
                }
                frames++;
                yield return null;
                budget.StartFrame();
            }
            stageLog.Append(stageLog.Length == 0 ? "" : ", ").Append(stage.Name).Append(' ')
                .Append(stageWatch.ElapsedMilliseconds).Append(" ms");
        }

        if (!StillValid(gen, db, scene))
        {
            yield break;
        }
        try
        {
            var cat = builder.Result;
            total.Stop();
            cat.BuildMs = total.ElapsedMilliseconds;
            cat.BuildFrames = frames;
            cat.Fingerprint = startPrint.WithPieceSum(cat.PieceSumSeen);
            if (!cat.Matches(Fingerprint.Take(db, scene, cat.PieceTables)))
            {
                Log.Debug($"Encyclopedia catalog: game data changed during the build ({cat.Fingerprint} -> "
                          + $"{Fingerprint.Take(db, scene, cat.PieceTables)}): built again at the next open.");
            }
            Current = cat;
            End(gen);
            LogBuilt(cat, builder, stageLog.ToString());
        }
        catch (Exception e)
        {
            PatchGuard.Report("Catalog build publish", e);
            End(gen);
            yield break;
        }
        RaiseReady();
    }

    // Non-critical: Warning, go on (return true). Critical: PatchGuard, abandon (return false).
    private static bool StageFailed(BuildStage stage, Exception e, int gen)
    {
        if (stage.Critical)
        {
            PatchGuard.Report($"Catalog build ({stage.Name})", e);
            End(gen);
            return false;
        }
        Log.Warning($"Encyclopedia catalog: stage '{stage.Name}' failed, the data it adds is missing. {e}");
        return true;
    }

    private static void End(int gen)
    {
        if (gen != _generation)
        {
            return;
        }
        _running = false;
        _coroutine = null;
        _buildDb = null;
        _buildScene = null;
    }

    private static void RaiseReady()
    {
        var handlers = CatalogReady;
        if (handlers == null)
        {
            return;
        }
        foreach (var d in handlers.GetInvocationList())
        {
            try
            {
                ((Action)d)();
            }
            catch (Exception e)
            {
                PatchGuard.Report("CatalogReady subscriber", e);
            }
        }
    }

    private static void LogBuilt(Catalog cat, CatalogBuilder builder, string stages)
    {
        Log.Info($"Encyclopedia catalog built in {cat.BuildMs} ms over {cat.BuildFrames} frames: {cat.ItemCount} items "
                 + $"({cat.HiddenCountOf(EntryKind.Item)} hidden until known, {cat.NoSourceCount} with no source found), "
                 + $"{cat.PieceCount} pieces, {cat.CreatureCount} creatures, {cat.ExcludedItemPrefabs} item prefabs excluded.");
        Log.Debug($"Encyclopedia catalog stages: {stages}.");
        Log.Debug(builder.ScanSummary());
        var tabs = new System.Text.StringBuilder();
        for (var t = 0; t < Tabs.Count; t++)
        {
            tabs.Append(t == 0 ? "" : ", ").Append((CatalogTab)t).Append(' ').Append(cat.CountOf((CatalogTab)t));
        }
        Log.Debug($"Encyclopedia catalog tabs: {tabs}.");
        foreach (var line in builder.DebugLines)
        {
            Log.Debug(line);
        }
    }
}
