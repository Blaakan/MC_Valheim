#if DEBUG
using System;
using System.IO;
using System.Text;
using HarmonyLib;
using MC.Core.ProbeWorldMod.Patches;
using MC.Shared;

namespace MC.Core.ProbeWorldMod;

// Me keep player's real characters and worlds out of reach during a probe run.
//   Awake (plugin load, before platform init and main menu): Utils.SetSaveDataPath(<run dir>/saves) + cloud saves
//   off (FileHelpersPatches). Every save path of game = Utils.GetSaveDataPath: local, legacy, and cloud when cloud off.
//   Main menu (platform ready): Verify() prove it with real game calls and list what save system see. Not proven =
//   probe never start world.
// Never undone in session: switching back would make game save probe world into real folder at quit.
internal static class SaveIsolation
{
    private static Harmony _harmony;

    internal static bool Applied { get; private set; }

    // Null = ok. Else why me refused (then nothing redirected, probe stay idle).
    internal static string Apply()
    {
        if (Applied)
        {
            return null;
        }
        var saves = ProbeSettings.SaveDir;
        var real = Utils.persistantDataPath;
        if (Overlaps(saves, real))
        {
            return $"run folder '{saves}' overlaps the real save folder '{real}'; refusing to run";
        }
        Directory.CreateDirectory(saves);
        Directory.CreateDirectory(ProbeSettings.ShotDir);
        Utils.SetSaveDataPath(saves);
        AppDomain.CurrentDomain.SetData(SelfTest.ShotDirSlot, ProbeSettings.ShotDir);
        _harmony = new Harmony(ModInfo.Guid + ".Isolation");
        _harmony.CreateClassProcessor(typeof(FileHelpersPatches)).Patch();
        if (ProbeSettings.IsMultiplayer)
        {
            // Rejoin in same process must get new session id (see UtilsPatches).
            _harmony.CreateClassProcessor(typeof(UtilsPatches)).Patch();
        }
        Applied = true;
        return null;
    }

    // Main menu, platform ready. Null = proven isolated. Report = what me checked (goes in log as proof).
    internal static string Verify(out string report)
    {
        var saves = ProbeSettings.SaveDir;
        var local = Utils.GetSaveDataPath(FileHelpers.FileSource.Local);
        var cloud = Utils.GetSaveDataPath(FileHelpers.FileSource.Cloud);
        var legacy = Utils.GetSaveDataPath(FileHelpers.FileSource.Legacy);
        var cloudOn = FileHelpers.CloudStorageSupportedAndEnabled;
        var sb = new StringBuilder();
        sb.Append("save data path local='").Append(local).Append("' cloud='").Append(cloud).Append("' legacy='").Append(legacy)
            .Append("', cloud saves enabled=").Append(cloudOn)
            .Append(", characters='").Append(SaveSystem.GetCharacterFolderPath(FileHelpers.FileSource.Local))
            .Append("', worlds='").Append(SaveSystem.GetWorldsSaveRootPath(FileHelpers.FileSource.Local))
            .Append("'; game default was '").Append(Utils.persistantDataPath).Append('\'');

        string error = null;
        if (cloudOn)
        {
            error = "cloud saves are still enabled";
        }
        else if (!Same(local, saves) || !Same(cloud, saves) || !Same(legacy, saves))
        {
            error = "a save data path is not the run folder";
        }

        // Every save the save system know must live in run folder, none from cloud.
        var count = 0;
        foreach (var type in new[] { SaveDataType.Character, SaveDataType.World })
        {
            var all = SaveSystem.GetSavesByType(type);
            if (all == null)
            {
                continue;
            }
            foreach (var save in all)
            {
                foreach (var file in save.AllFiles)
                {
                    foreach (var path in file.AllPaths)
                    {
                        count++;
                        if (error == null && (file.m_source.IsCloud() || !Inside(path, saves)))
                        {
                            error = $"save file outside the run folder: '{path}' ({file.m_source})";
                        }
                    }
                }
            }
        }
        sb.Append("; ").Append(count).Append(" save file(s) known, all inside the run folder: ").Append(error == null);
        report = sb.ToString();
        return error;
    }

    private static string Norm(string path)
    {
        return Path.GetFullPath(path).Replace('/', '\\').TrimEnd('\\');
    }

    private static bool Same(string a, string b)
    {
        return !string.IsNullOrEmpty(a) && string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);
    }

    private static bool Inside(string path, string dir)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }
        var p = Norm(path);
        var d = Norm(dir);
        return p.Equals(d, StringComparison.OrdinalIgnoreCase)
               || p.StartsWith(d + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Overlaps(string a, string b)
    {
        return Inside(a, b) || Inside(b, a);
    }
}
#endif
