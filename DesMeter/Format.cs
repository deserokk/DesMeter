using System;
using System.Globalization;

namespace DesMeter;

internal static class Format
{

    internal static string Short(double value)
    {
        var magnitude = Math.Abs(value);

        return magnitude switch
        {
            >= 1e9 => (value / 1e9).ToString("0.##", CultureInfo.InvariantCulture) + "B",
            >= 1e6 => (value / 1e6).ToString("0.##", CultureInfo.InvariantCulture) + "M",
            >= 1e3 => (value / 1e3).ToString("0.##", CultureInfo.InvariantCulture) + "K",
            _ => value.ToString("0", CultureInfo.InvariantCulture),
        };
    }
}
