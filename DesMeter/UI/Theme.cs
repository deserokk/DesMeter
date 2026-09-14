using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace DesMeter.UI;

internal static class Theme
{
    private static Vector4 Hex(uint value, bool hasAlpha = false)
        => hasAlpha
            ? new Vector4(((value >> 24) & 0xFF) / 255f, ((value >> 16) & 0xFF) / 255f,
                          ((value >> 8) & 0xFF) / 255f, (value & 0xFF) / 255f)
            : new Vector4(((value >> 16) & 0xFF) / 255f, ((value >> 8) & 0xFF) / 255f,
                          (value & 0xFF) / 255f, 1f);

    public static readonly Vector4 Ground = Hex(0x000000);

    public static readonly Vector4 Panel = Hex(0x141416);

    public static readonly Vector4 Field = Hex(0x1C1C1E);
    public static readonly Vector4 Raised = Hex(0x2C2C2E);
    public static readonly Vector4 RuleStrong = Hex(0x38383A);
    public static readonly Vector4 RuleHair = Hex(0x2C2C2E);
    public static readonly Vector4 BorderControl = Hex(0x48484A);

    public static readonly Vector4 CardBorder = Hex(0xFFFFFF1F, hasAlpha: true);

    public static readonly Vector4 Ink = Hex(0xF5F5F7);
    public static readonly Vector4 Dim = Hex(0xA8A8AD);
    public static readonly Vector4 Faint = Hex(0x7C7C82);

    public static readonly Vector4 Positive = Hex(0x4EA36B);
    public static readonly Vector4 Negative = Hex(0xD6584A);

    public static readonly Vector4 Accent = Hex(0x3D87EB);

    public static Vector4 AccentAlpha(float a) => Accent with { W = a };

    public static Vector4 OnColour(Vector4 c)
    {
        var luminance = (0.2126f * c.X) + (0.7152f * c.Y) + (0.0722f * c.Z);
        return luminance > 0.55f ? Hex(0x000000) : Hex(0xFFFFFF);
    }

    public static uint U(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);

    public static Vector4 Fade(Vector4 c, float alpha) => c with { W = c.W * alpha };

    private static int colours;
    private static int vars;

    public static void Push()
    {
        colours = 0;
        vars = 0;

        Colour(ImGuiCol.WindowBg, Ground);
        Colour(ImGuiCol.ChildBg, Vector4.Zero);
        Colour(ImGuiCol.PopupBg, Panel);
        Colour(ImGuiCol.Border, BorderControl);
        Colour(ImGuiCol.BorderShadow, Vector4.Zero);

        Colour(ImGuiCol.Text, Ink);
        Colour(ImGuiCol.TextDisabled, Faint);

        Colour(ImGuiCol.FrameBg, Field);
        Colour(ImGuiCol.FrameBgHovered, Raised);
        Colour(ImGuiCol.FrameBgActive, Raised);

        Colour(ImGuiCol.Button, Field);
        Colour(ImGuiCol.ButtonHovered, Raised);
        Colour(ImGuiCol.ButtonActive, Raised);

        Colour(ImGuiCol.Header, Raised);
        Colour(ImGuiCol.HeaderHovered, Raised);
        Colour(ImGuiCol.HeaderActive, AccentAlpha(0.33f));

        Colour(ImGuiCol.CheckMark, Accent);
        Colour(ImGuiCol.SliderGrab, Accent);

        Colour(ImGuiCol.ScrollbarBg, Vector4.Zero);
        Colour(ImGuiCol.ScrollbarGrab, RuleStrong);
        Colour(ImGuiCol.ScrollbarGrabHovered, BorderControl);
        Colour(ImGuiCol.ScrollbarGrabActive, Accent);

        Colour(ImGuiCol.Separator, RuleHair);

        Colour(ImGuiCol.TableHeaderBg, Field);
        Colour(ImGuiCol.TableBorderStrong, RuleStrong);
        Colour(ImGuiCol.TableBorderLight, RuleHair);
        Colour(ImGuiCol.TableRowBg, Vector4.Zero);
        Colour(ImGuiCol.TableRowBgAlt, Fade(Field, 0.5f));

        Colour(ImGuiCol.ResizeGrip, Vector4.Zero);
        Colour(ImGuiCol.ResizeGripHovered, Vector4.Zero);
        Colour(ImGuiCol.ResizeGripActive, Vector4.Zero);

        Var(ImGuiStyleVar.WindowRounding, Chrome.WindowRounding);
        Var(ImGuiStyleVar.WindowBorderSize, 0f);
        Var(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        Var(ImGuiStyleVar.WindowMinSize, Chrome.CurrentSize);
        Var(ImGuiStyleVar.ChildRounding, 12f * Chrome.Scale);
        Var(ImGuiStyleVar.ChildBorderSize, 0f);
        Var(ImGuiStyleVar.PopupRounding, 10f * Chrome.Scale);
        Var(ImGuiStyleVar.FrameRounding, 10f * Chrome.Scale);
        Var(ImGuiStyleVar.FrameBorderSize, 0f);
        Var(ImGuiStyleVar.FramePadding, new Vector2(10f, 6f) * Chrome.Scale);
        Var(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 8f) * Chrome.Scale);
        Var(ImGuiStyleVar.ScrollbarSize, 10f * Chrome.Scale);
        Var(ImGuiStyleVar.ScrollbarRounding, 999f);
        Var(ImGuiStyleVar.GrabRounding, 999f);
        Var(ImGuiStyleVar.TabRounding, 8f * Chrome.Scale);
    }

    public static void Pop()
    {
        if (vars > 0) ImGui.PopStyleVar(vars);
        if (colours > 0) ImGui.PopStyleColor(colours);
        vars = 0;
        colours = 0;
    }

    private static void Colour(ImGuiCol which, Vector4 value)
    {
        ImGui.PushStyleColor(which, value);
        colours++;
    }

    private static void Var(ImGuiStyleVar which, float value)
    {
        ImGui.PushStyleVar(which, value);
        vars++;
    }

    private static void Var(ImGuiStyleVar which, Vector2 value)
    {
        ImGui.PushStyleVar(which, value);
        vars++;
    }
}
