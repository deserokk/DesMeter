using System;
using System.Collections.Generic;

namespace DesMeter;

internal static class DotPotency
{
    internal static readonly Dictionary<string, int> ByStatus = new(StringComparer.OrdinalIgnoreCase)
    {

        ["Combust"] = 50,
        ["Combust II"] = 60,
        ["Combust III"] = 70,

        ["Caustic Bite"] = 20,
        ["Stormbite"] = 25,
        ["Venomous Bite"] = 15,
        ["Windbite"] = 20,

        ["Thunder"] = 45,
        ["Thunder II"] = 30,
        ["Thunder III"] = 50,
        ["Thunder IV"] = 35,
        ["High Thunder"] = 60,
        ["High Thunder II"] = 40,

        ["Chaos Thrust"] = 40,
        ["Chaotic Spring"] = 45,

        ["Bow Shock"] = 60,
        ["Sonic Break"] = 120,

        ["Bioblaster"] = 50,

        ["Circle of Scorn"] = 30,

        ["Eukrasian Dosis"] = 40,
        ["Eukrasian Dosis II"] = 60,
        ["Eukrasian Dosis III"] = 90,
        ["Eukrasian Dyskrasia"] = 40,

        ["Higanbana"] = 50,

        ["Bio"] = 20,
        ["Bio II"] = 40,
        ["Biolysis"] = 85,
        ["Baneful Impaction"] = 140,

        ["Aero"] = 30,
        ["Aero II"] = 50,
        ["Dia"] = 85,
    };
}
