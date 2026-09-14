using System;
using System.Collections.Generic;

namespace DesMeter;

internal static class HotPotency
{
    internal static readonly Dictionary<string, int> ByStatus = new(StringComparer.OrdinalIgnoreCase)
    {

        ["Knight's Benediction"] = 250,

        ["Equilibrium"] = 200,
        ["Shake It Off (Over Time)"] = 100,
        ["Primeval Impulse"] = 300,

        ["Aurora"] = 300,

        ["Regen"] = 250,
        ["Medica II"] = 150,
        ["Medica III"] = 175,
        ["Asylum"] = 100,
        ["Divine Aura"] = 200,

        ["Whispering Dawn"] = 80,
        ["Angel's Whisper"] = 80,
        ["Sacred Soil"] = 100,
        ["Fey Union"] = 300,
        ["Seraphism"] = 100,

        ["Aspected Benefic"] = 250,
        ["Aspected Helios"] = 150,
        ["Helios Conjunction"] = 175,
        ["Wheel of Fortune"] = 100,
        ["Opposition"] = 100,
        ["The Ewer"] = 200,

        ["Physis"] = 100,
        ["Physis II"] = 130,
        ["Kerakeia"] = 100,

        ["Earth's Resolve"] = 100,

        ["Tengentsu's Foresight"] = 200,

        ["Crest of Time Returned"] = 50,

        ["Improvisation"] = 100,

        ["Undying Flame"] = 200,
        ["Everlasting Flight"] = 100,
    };
}
