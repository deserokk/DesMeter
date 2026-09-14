using System;
using System.Collections.Generic;
using System.Globalization;

namespace DesMeter;

internal static class ShieldStatus
{

    internal static Func<IEnumerable<(uint Id, string Name, string Description)>>? Source;

    private static readonly HashSet<string> NotShields = new(StringComparer.OrdinalIgnoreCase) { "Haimatinon", "Panhaimatinon" };

    private static readonly HashSet<string> Fallback = new(StringComparer.OrdinalIgnoreCase)
    {
        "Eukrasian Prognosis", "Eukrasian Diagnosis", "Differential Diagnosis", "Haima", "Panhaima", "Holosakos",
        "Galvanize", "Catalyze", "Seraphic Veil", "Divine Caress", "Divine Benison", "Intersection", "Neutral Sect",
        "The Spire", "Brutal Shell", "Stem the Tide", "Shake It Off", "Blackest Night", "Divine Veil",
        "Crest of Time Borrowed", "Radiant Aegis", "Tempera Coat", "Tempera Grassa", "Shade Shift", "Improvised Finish",
    };

    private static readonly object Gate = new();
    private static HashSet<uint>? ids;

    internal static bool Is(string idHex, string name)
    {
        var set = Ids();
        if (set is null) return Fallback.Contains(name);

        return uint.TryParse(idHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id) && set.Contains(id);
    }

    private static HashSet<uint>? Ids()
    {
        if (ids is not null || Source is null) return ids;

        lock (Gate)
        {
            if (ids is not null) return ids;

            var built = new HashSet<uint>();
            foreach (var (id, name, description) in Source())
            {
                if (name.Length == 0 || NotShields.Contains(name)) continue;
                if (description.Contains("nullifying damage", StringComparison.OrdinalIgnoreCase)) built.Add(id);
            }

            ids = built;
        }

        return ids;
    }
}
