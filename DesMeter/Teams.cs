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

    internal static volatile int Version;

    private const uint FrontlineUse = 18;

    internal static volatile bool InFrontlines;

    private static readonly Vector4[] Colours =
    {
        new(0.70f, 0.20f, 0.18f, 1f),
        new(0.80f, 0.66f, 0.20f, 1f),
        new(0.22f, 0.45f, 0.80f, 1f),
    };

    internal static void Enter(uint territory)
    {
        ByName.Clear();
        Placed.Clear();
        Version++;

        var use = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>()
                            .GetRowOrDefault(territory)?.TerritoryIntendedUse.RowId ?? uint.MaxValue;

        InFrontlines = use == FrontlineUse;
    }

    internal static unsafe void Scan()
    {
        if (!InFrontlines) return;

        var now = DateTime.UtcNow;
        if (now < nextScan) return;
        nextScan = now.AddSeconds(1);

        var me = Plugin.Objects.LocalPlayer?.EntityId;

        foreach (var obj in Plugin.Objects)
        {
            if (obj is not IPlayerCharacter pc) continue;
            if (Placed.Contains(pc.EntityId)) continue;

            var team = ((FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)pc.Address)->Battalion;
            if (team > 2) continue;

            ByName[pc.Name.TextValue] = team;
            Placed.Add(pc.EntityId);
            Version++;

            if (pc.EntityId == me) ByName["YOU"] = team;
        }
    }

    internal static Vector4 Colour(string job, string name, bool wanted)
    {
        if (wanted && InFrontlines && ByName.TryGetValue(Owner(name), out var team))
            return Colours[team];

        return JobColours.For(job, name);
    }

    private static string Owner(string name)
    {
        var open = name.LastIndexOf(" (", StringComparison.Ordinal);
        return open > 0 && name.EndsWith(')') ? name[(open + 2)..^1] : name;
    }
}
