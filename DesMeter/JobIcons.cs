using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Lumina.Excel.Sheets;

namespace DesMeter;

internal static class JobIcons
{
    private const uint IconBase = 62100;

    private static Dictionary<string, uint>? byAbbreviation;

    private static readonly Dictionary<uint, ISharedImmediateTexture?> Cache = new();

    private static Dictionary<string, uint> Map
    {
        get
        {
            if (byAbbreviation is not null) return byAbbreviation;

            var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

            foreach (var job in Plugin.Data.GetExcelSheet<ClassJob>())
            {
                var abbreviation = job.Abbreviation.ExtractText();

                if (!string.IsNullOrEmpty(abbreviation)) map[abbreviation] = job.RowId;
            }

            byAbbreviation = map;
            return map;
        }
    }

    internal static void Draw(string abbreviation, Vector2 position, float size)
    {
        if (string.IsNullOrEmpty(abbreviation)) return;
        if (!Map.TryGetValue(abbreviation, out var jobId) || jobId == 0) return;

        if (!Cache.TryGetValue(jobId, out var shared))
        {

            shared = Plugin.Textures.TryGetFromGameIcon(new GameIconLookup(IconBase + jobId), out var found)
                ? found
                : null;

            Cache[jobId] = shared;
        }

        if (shared is null) return;

        var wrap = shared.GetWrapOrEmpty();
        ImGui.GetWindowDrawList().AddImage(wrap.Handle, position, position + new Vector2(size, size));
    }
}
