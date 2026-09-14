using System;
using System.Collections.Concurrent;
using System.Numerics;

using Dalamud.Game.ClientState.Objects.SubKinds;

namespace DesMeter;

internal static class Teams
{
    private static readonly ConcurrentDictionary<string, byte> ByName = new(StringComparer.Ordinal);

    private static DateTime nextScan = DateTime.MinValue;

    private static readonly System.Collections.Generic.HashSet<uint> Placed = new();

    private const uint FrontlineUse = 18, CrystallineUse = 28;

    private static readonly Vector4[] Frontline =
    {
        new(0.70f, 0.20f, 0.18f, 1f),
        new(0.80f, 0.66f, 0.20f, 1f),
        new(0.22f, 0.45f, 0.80f, 1f),
    };

    private static readonly Vector4[] Crystalline =
    {
        new(0.22f, 0.50f, 0.85f, 1f),
        new(0.80f, 0.28f, 0.35f, 1f),
    };

    private static Vector4[]? PaletteFor(int use) => use switch
    {
        (int)FrontlineUse => Frontline,
        (int)CrystallineUse => Crystalline,
        _ => null,
    };

    private static volatile Vector4[]? palette;

    internal static volatile int Mode;

    internal static bool Active => palette != null;

    internal static void Enter(uint use)
    {
        ByName.Clear();
        Placed.Clear();
        palette = PaletteFor((int)use);
        Mode = palette is null ? 0 : (int)use;
    }

    internal static unsafe void Scan()
    {
        var colours = palette;
        if (colours == null) return;

        var now = DateTime.UtcNow;
        if (now < nextScan) return;
        nextScan = now.AddSeconds(1);

        var me = Plugin.Objects.LocalPlayer?.EntityId;

        foreach (var obj in Plugin.Objects)
        {
            if (obj is not IPlayerCharacter pc) continue;
            if (Placed.Contains(pc.EntityId)) continue;

            var team = ((FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)pc.Address)->Battalion;
            if (team >= colours.Length) continue;

            ByName[pc.Name.TextValue] = team;
            Placed.Add(pc.EntityId);

            if (pc.EntityId == me) ByName["YOU"] = team;
        }
    }

    internal static Vector4 Colour(MeterRow row, int mode, bool wanted)
    {
        if (wanted && row.Team >= 0 && PaletteFor(mode) is { } colours && row.Team < colours.Length)
            return colours[row.Team];

        return JobColours.For(row.Job, row.Name);
    }

    internal static int Of(string name) => ByName.TryGetValue(Owner(name), out var team) ? team : -1;

    private static string Owner(string name)
    {
        var open = name.LastIndexOf(" (", StringComparison.Ordinal);
        return open > 0 && name.EndsWith(')') ? name[(open + 2)..^1] : name;
    }
}
