using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;

namespace DesMeter.UI;

internal static class Chrome
{

    public static float Scale => ImGuiHelpers.GlobalScale;

    public static float UserTextScale { get; set; } = 1f;

    public static float TextScale => ImGuiHelpers.GlobalScale * Math.Clamp(UserTextScale, 0.7f, 2f);

    public static float WindowRounding => 18f * Scale;
    public static float RailWidth => 252f * Scale;
    public static float HeaderHeight => 58f * TextScale;
    public static float FooterHeight => 34f * TextScale;
    public static float RowHeight => 38f * TextScale;
    public static float RowGap => 10f * Scale;
    public static float Pad => 18f * Scale;
    public static float RailPad => 12f * Scale;

    public static Vector2 CurrentSize { get; set; }

    private static UiFonts fonts = null!;

    public static void Attach(UiFonts f) => fonts = f;

    public static float TrackedCaps(Vector2 at, string text, Vector4 colour, float tracking = 0.14f)
    {
        var draw = ImGui.GetWindowDrawList();
        var packed = Theme.U(colour);
        var gap = ImGui.GetFontSize() * tracking;
        var x = at.X;

        foreach (var ch in text.ToUpperInvariant())
        {
            var glyph = ch.ToString();
            draw.AddText(new Vector2(x, at.Y), packed, glyph);
            x += ImGui.CalcTextSize(glyph).X + gap;
        }

        return x - at.X;
    }

    public static void SectionLabel(string text)
    {
        using (fonts.Label.Push())
        {
            var at = ImGui.GetCursorScreenPos();
            var width = TrackedCaps(at, text, Theme.Faint);
            ImGui.Dummy(new Vector2(width, ImGui.GetTextLineHeight()));
        }
    }

    public static void TextAt(Vector2 at, string text, Vector4 colour)
        => ImGui.GetWindowDrawList().AddText(at, Theme.U(colour), text);

    public static string Fit(string text, float width)
    {
        if (ImGui.CalcTextSize(text).X <= width) return text;

        const string ellipsis = "...";
        var room = width - ImGui.CalcTextSize(ellipsis).X;
        var cut = text.Length;

        while (cut > 0 && ImGui.CalcTextSize(text[..cut]).X > room) cut--;

        return text[..cut].TrimEnd() + ellipsis;
    }

    public static void Icon(Vector2 at, FontAwesomeIcon icon, Vector4 colour)
    {
        using (fonts.Icons.Push())
            ImGui.GetWindowDrawList().AddText(at, Theme.U(colour), icon.ToIconString());
    }

    public static Vector2 IconSize(FontAwesomeIcon icon)
    {
        using (fonts.Icons.Push())
            return ImGui.CalcTextSize(icon.ToIconString());
    }

    public static bool GlyphButton(string id, FontAwesomeIcon icon, float box = 30f)
    {
        var size = new Vector2(box * Scale, box * Scale);
        var origin = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton($"##{id}", size);

        var clicked = ImGui.IsItemClicked();
        var hovered = ImGui.IsItemHovered();

        if (hovered)
            ImGui.GetWindowDrawList().AddRectFilled(origin, origin + size, Theme.U(Theme.Field), 8f * Scale);

        var glyph = IconSize(icon);
        Icon(origin + ((size - glyph) * 0.5f), icon, hovered ? Theme.Ink : Theme.Dim);
        return clicked;
    }

    public static void Rule(float inset = 0f)
    {
        var draw = ImGui.GetWindowDrawList();
        var at = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        draw.AddRectFilled(new Vector2(at.X + inset, at.Y), new Vector2(at.X + width - inset, at.Y + 1f),
                           Theme.U(Theme.RuleHair));
        ImGui.Dummy(new Vector2(width, 1f));
    }
}
