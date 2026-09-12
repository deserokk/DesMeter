using System;
using System.IO;

namespace DesMeter;

internal static class Probe
{
    private static readonly object Gate = new();
    private static string? path;

    internal static void Open()
    {
        try
        {
            var dir = Plugin.PluginInterface.ConfigDirectory;
            dir.Create();
            path = Path.Combine(dir.FullName, "probe.log");

            File.WriteAllText(path, $"--- DesMeter session {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---\n");
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"Probe log unavailable: {ex.Message}");
        }
    }

    internal static void Line(string text)
    {
        if (path is null) return;

        try
        {
            lock (Gate) File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss}  {text}\n");
        }
        catch
        {

        }
    }
}
