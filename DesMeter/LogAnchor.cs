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

            if (!File.Exists(config)) return null;

            folder = JObject.Parse(File.ReadAllText(config))["LogFilePath"]?.ToString();
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"No IINACT log path: {ex.Message}");
        }

        return folder;
    }

    internal static (string Path, long Offset) Current()
    {
        try
        {
            if (Folder() is not { Length: > 0 } dir || !Directory.Exists(dir))
                return (string.Empty, 0);

            var newest = new DirectoryInfo(dir)
                         .GetFiles("Network_*.log")
                         .OrderByDescending(f => f.LastWriteTimeUtc)
                         .FirstOrDefault();

            return newest is null ? (string.Empty, 0) : (newest.FullName, newest.Length);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"Could not anchor the log: {ex.Message}");
            return (string.Empty, 0);
        }
    }
}
