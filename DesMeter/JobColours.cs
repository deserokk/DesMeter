using System;
using System.Numerics;

namespace DesMeter;

internal static class JobColours
{
    internal static Vector4 For(string job, string name) => job.ToUpperInvariant() switch
    {

        "PLD" or "GLA" => Rgb(0xA8, 0xD2, 0xE6),
        "WAR" or "MRD" => Rgb(0xCF, 0x36, 0x30),
        "DRK"          => Rgb(0xD1, 0x3F, 0xCC),
        "GNB"          => Rgb(0xB5, 0xA3, 0x4A),

        "WHM" or "CNJ" => Rgb(0xFF, 0xF0, 0xDC),
        "SCH"          => Rgb(0x86, 0x57, 0xFF),
        "AST"          => Rgb(0xFF, 0xE7, 0x4A),
        "SGE"          => Rgb(0x80, 0xA0, 0xF0),

        "MNK" or "PGL" => Rgb(0xD6, 0xA0, 0x44),
        "DRG" or "LNC" => Rgb(0x51, 0x74, 0xDD),
        "NIN" or "ROG" => Rgb(0xC4, 0x2A, 0x7C),
        "SAM"          => Rgb(0xE4, 0x6D, 0x04),
        "RPR"          => Rgb(0xA6, 0x6A, 0xA0),
        "VPR"          => Rgb(0x1C, 0xA5, 0x2A),

        "BRD" or "ARC" => Rgb(0x91, 0xBA, 0x5E),
        "MCH"          => Rgb(0x6E, 0xE1, 0xD6),
        "DNC"          => Rgb(0xE2, 0xB0, 0xAF),

        "BLM" or "THM" => Rgb(0xA5, 0x79, 0xD6),
        "SMN" or "ACN" => Rgb(0x2D, 0xB0, 0x88),
        "RDM"          => Rgb(0xE8, 0x7B, 0x7B),
        "PCT"          => Rgb(0xFC, 0x92, 0xE1),
        "BLU"          => Rgb(0x4A, 0x73, 0xFF),

        "BST"          => Rgb(0xB8, 0x6A, 0x2E),

        _ => FromName(name),
    };

    private static Vector4 Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1f);

    private static Vector4 FromName(string name)
    {
        if (string.IsNullOrEmpty(name)) return Rgb(0x88, 0x8C, 0x94);

        var hash = 17;
        foreach (var c in name) hash = (hash * 31) + c;

        return Hsv(Math.Abs(hash) % 360 / 360f, 0.45f, 0.80f);
    }

    private static Vector4 Hsv(float h, float s, float v)
    {
        ImGuiNET_ColorConvertHSVtoRGB(h, s, v, out var r, out var g, out var b);
        return new Vector4(r, g, b, 1f);
    }

    private static void ImGuiNET_ColorConvertHSVtoRGB(float h, float s, float v,
                                                      out float r, out float g, out float b)
    {
        var i = (int)(h * 6f);
        var f = (h * 6f) - i;
        var p = v * (1f - s);
        var q = v * (1f - (f * s));
        var t = v * (1f - ((1f - f) * s));

        switch (i % 6)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }
    }
}
