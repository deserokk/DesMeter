using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DesMeter;

internal static class LogAnchor
{
    private static string? folder;
    private static bool looked;

    private static string? Folder()
    {
        if (looked) return folder;
        looked = true;

        try
        {
            var config = Path.Combine(
                Plugin.PluginInterface.ConfigDirectory.Parent!.FullName, "IINACT.json");

            if (File.Exists(config))
                folder = JObject.Parse(File.ReadAllText(config))["LogFilePath"]?.ToString();
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"No IINACT log path: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(folder))
            folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "IINACT");

        return folder;
    }

    internal static string Diagnose()
    {
        try
        {
            var config = Path.Combine(Plugin.PluginInterface.ConfigDirectory.Parent!.FullName, "IINACT.json");

            if (File.Exists(config))
            {
                var settings = JObject.Parse(File.ReadAllText(config));
                if (settings["WriteLogFile"] is { } write && write.Type == JTokenType.Boolean && !write.Value<bool>())
                    return "IINACT isn't writing its network log. Turn on \"Write log file\" in IINACT's settings, then fight again.";
            }

            if (Folder() is not { Length: > 0 } dir || !Directory.Exists(dir))
                return $"IINACT's log folder wasn't found ({Folder()}). Check the log path in IINACT's settings.";

            if (Directory.GetFiles(dir, "Network_*.log").Length == 0)
                return $"IINACT's log folder has no network logs yet ({dir}). Check \"Write log file\" in IINACT's settings.";

            return "This fight started before IINACT's log could be found. Fights from here on should work.";
        }
        catch (Exception ex)
        {
            return $"IINACT's log couldn't be located: {ex.Message}";
        }
    }

    internal static (string Path, long Offset) Current()
    {
        try
        {
            if (Folder() is not { Length: > 0 } dir || !Directory.Exists(dir))
                return (string.Empty, 0);

            var newest = new DirectoryInfo(dir)
                         .GetFiles("Network_*.log")
                         .OrderByDescending(f => f.CreationTimeUtc)
                         .FirstOrDefault();

            if (newest is null) return (string.Empty, 0);

            using var stream = new FileStream(newest.FullName, FileMode.Open, FileAccess.Read,
                                              FileShare.ReadWrite | FileShare.Delete);

            return (newest.FullName, stream.Length);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"Could not anchor the log: {ex.Message}");
            return (string.Empty, 0);
        }
    }
}
