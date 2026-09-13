using System;
using System.Collections.Generic;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;

namespace DesMeter;

internal static class Abilities
{

    private static Dictionary<string, uint>? byName;

    private static readonly Dictionary<uint, ISharedImmediateTexture?> Textures = new();

    internal static (string Name, string Amount) Split(string maxHit)
    {
        var cut = maxHit.LastIndexOf('-');

        return cut <= 0 || cut == maxHit.Length - 1
            ? (maxHit, string.Empty)
            : (maxHit[..cut], maxHit[(cut + 1)..]);
    }

    private static uint IconFor(string name)
    {
        if (byName is null)
        {
            byName = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

            var sheet = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Action>();

            if (sheet is not null)
            {
                foreach (var row in sheet)
                {
                    if (row.Icon == 0) continue;

                    var text = row.Name.ExtractText();
                    if (text.Length == 0) continue;

                    byName.TryAdd(text, row.Icon);
                }
            }
        }

        return byName.GetValueOrDefault(name, 0u);
    }

    internal static float Icon(ImDrawListPtr dl, Vector2 at, float size, string name)
    {
        var id = IconFor(name);
        if (id == 0) return 0f;

        if (!Textures.TryGetValue(id, out var shared))
        {

            shared = Plugin.Textures.TryGetFromGameIcon(new GameIconLookup(id), out var found)
                ? found
                : null;

            Textures[id] = shared;
        }

        if (shared is null) return 0f;

        dl.AddImage(shared.GetWrapOrEmpty().Handle, at, at + new Vector2(size, size));
        return size;
    }
}
