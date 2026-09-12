using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace DesMeter;

internal sealed class OptionsWindow : Window
{
    private readonly Configuration config;

    internal MeterSettings? Target { get; set; }

    internal Action? Clear { get; set; }

    internal Action? AddWindow { get; set; }

    internal Action<MeterSettings>? CloseWindow { get; set; }

    private bool confirming;

    private bool dirty;

    internal OptionsWindow(Configuration config) : base("DesMeter options###DesMeterOptions")
    {
        this.config = config;

        this.Size = new Vector2(340, 560);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void OnClose() => this.confirming = false;

    public override void Draw()
    {
        this.Target ??= this.config.Windows.Count > 0 ? this.config.Windows[0] : null;

        if (this.Target is not { } window)
        {
            ImGui.TextDisabled("No meter windows.");

            if (ImGui.Button("New window")) this.AddWindow?.Invoke();
            return;
        }

        this.dirty = false;

        this.DrawWindows(window);
        this.DrawDisplay(window);
        this.DrawAppearance(window);
        this.DrawShared();
        DrawGestures();
        this.DrawEncounters();

        if (this.dirty) this.config.Save();
    }

    private void DrawWindows(MeterSettings window)
    {
        ImGui.TextUnformatted($"Window {window.Id}");
        ImGui.Separator();
        ImGui.Spacing();

        var room = this.config.Windows.Count < Configuration.MaxWindows;

        ImGui.BeginDisabled(!room);
        if (ImGui.Button("New window", new Vector2(120, 0))) this.AddWindow?.Invoke();
        ImGui.EndDisabled();

        ImGui.SameLine();

        ImGui.BeginDisabled(this.config.Windows.Count <= 1);
        var closing = ImGui.Button("Close this one", new Vector2(120, 0));
        ImGui.EndDisabled();

        if (closing)
        {
            this.CloseWindow?.Invoke(window);
            this.Target = null;
            return;
        }

        if (!room) ImGui.TextDisabled($"{Configuration.MaxWindows} windows is the limit.");

        ImGui.TextDisabled("A new window copies this one, then change");
        ImGui.TextDisabled("whatever you like. The cog on a meter opens");
        ImGui.TextDisabled("that meter's settings.");
        ImGui.Spacing();
        ImGui.Spacing();
    }

    private void DrawDisplay(MeterSettings window)
    {
        ImGui.TextUnformatted("Showing");
        ImGui.Separator();
        ImGui.Spacing();

        this.dirty |= Display(window, Metric.Damage, "Damage Done");
        this.dirty |= Display(window, Metric.Healing, "Healing Done");
        this.dirty |= Display(window, Metric.DamageTaken, "Damage Taken");
        this.dirty |= Display(window, Metric.HealingTaken, "Healing Taken");

        ImGui.TextDisabled("Each window picks its own.");

        ImGui.Spacing();
        ImGui.Spacing();
    }

    private static bool Display(MeterSettings window, Metric metric, string label)
    {
        if (!ImGui.RadioButton(label, window.Metric == metric)) return false;

        window.Metric = metric;
        return true;
    }

    private void DrawAppearance(MeterSettings window)
    {
        ImGui.TextUnformatted("This window");
        ImGui.Separator();
        ImGui.Spacing();

        var scale = window.FontScale;
        ImGui.SetNextItemWidth(150);
        if (ImGui.SliderFloat("Text size", ref scale, 0.65f, 1.4f, "%.2f")) window.FontScale = scale;
        this.Settled();

        var rowHeight = window.RowHeight;
        ImGui.SetNextItemWidth(150);
        if (ImGui.SliderFloat("Row height", ref rowHeight, 0f, 40f, "%.0f px")) window.RowHeight = rowHeight;
        this.Settled();
        ImGui.TextDisabled("Zero follows the text.");

        var spacing = window.RowSpacing;
        ImGui.SetNextItemWidth(150);
        if (ImGui.SliderFloat("Bar spacing", ref spacing, 0f, 8f, "%.0f px")) window.RowSpacing = spacing;
        this.Settled();

        var clearance = window.HeaderClearance;
        ImGui.SetNextItemWidth(150);
        if (ImGui.SliderFloat("Header gap", ref clearance, 0f, 12f, "%.0f px")) window.HeaderClearance = clearance;
        this.Settled();

        ImGui.Spacing();

        var background = window.Background;
        if (ImGui.ColorEdit4("Background", ref background,
                             ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf
                           | ImGuiColorEditFlags.NoInputs))
            window.Background = background;

        this.Settled();

        var headerAlpha = window.HeaderAlpha;
        ImGui.SetNextItemWidth(150);
        if (ImGui.SliderFloat("Header opacity", ref headerAlpha, 0f, 1f, "%.2f")) window.HeaderAlpha = headerAlpha;
        this.Settled();
        ImGui.TextDisabled("Background alpha at zero leaves the bars");
        ImGui.TextDisabled("floating with just the header behind them.");

        ImGui.Spacing();

        var smooth = window.SmoothBars;
        if (ImGui.Checkbox("Grow bars smoothly", ref smooth)) { window.SmoothBars = smooth; this.dirty = true; }

        var grow = window.GrowUpward;
        if (ImGui.Checkbox("Grow upward", ref grow)) { window.GrowUpward = grow; this.dirty = true; }

        var locked = window.Locked;
        if (ImGui.Checkbox("Lock position and size", ref locked)) { window.Locked = locked; this.dirty = true; }

        var pvp = window.PvpWholeMatch;
        if (ImGui.Checkbox("In PvP, show the whole match", ref pvp)) { window.PvpWholeMatch = pvp; this.dirty = true; }

        var showLb = window.ShowLimitBreak;
        if (ImGui.Checkbox("Limit Break gets its own bar", ref showLb)) { window.ShowLimitBreak = showLb; this.dirty = true; }

        var countLb = window.CountLimitBreak;
        if (ImGui.Checkbox("Limit Break counts toward totals", ref countLb)) { window.CountLimitBreak = countLb; this.dirty = true; }

        ImGui.Spacing();
        ImGui.Spacing();
    }

    private void Settled()
    {
        if (ImGui.IsItemDeactivatedAfterEdit()) this.dirty = true;
    }

    private void DrawShared()
    {
        ImGui.TextUnformatted("All windows");
        ImGui.Separator();
        ImGui.Spacing();

        var brightness = this.config.BarBrightness;
        ImGui.SetNextItemWidth(150);

        if (ImGui.SliderFloat("Bar brightness", ref brightness, 0.25f, 1f, "%.2f"))
        {
            this.config.BarBrightness = brightness;
            this.config.Save();
        }

        var pets = this.config.CombinePets;
        if (ImGui.Checkbox("Fold pets into their owner", ref pets))
        {
            this.config.CombinePets = pets;
            this.config.Save();
        }

        var skull = this.config.DeathSkull;
        if (ImGui.Checkbox("Skull for deaths instead of a cross", ref skull))
        {
            this.config.DeathSkull = skull;
            this.config.Save();
        }

        var lockAlt = this.config.LockNeedsAlt;
        if (ImGui.Checkbox("Only show the lock button with Alt held", ref lockAlt))
        {
            this.config.LockNeedsAlt = lockAlt;
            this.config.Save();
        }

        ImGui.Spacing();
        ImGui.Spacing();
    }

    private static void DrawGestures()
    {
        ImGui.TextUnformatted("Using a meter");
        ImGui.Separator();
        ImGui.Spacing();

        Gesture("Drag", "anywhere on the meter");
        Gesture("Resize", "grip at the corner");
        Gesture("Right-click", "choose a display");
        Gesture("Shift right-click", "tonight's encounters");
        Gesture("Ctrl right-click", "close that meter");
        Gesture("Hold Alt", "reveals the lock button");
        Gesture("/desmeter", "show them again");

        ImGui.Spacing();
        ImGui.Spacing();
    }

    private void DrawEncounters()
    {
        ImGui.TextUnformatted("Encounters");
        ImGui.Separator();
        ImGui.Spacing();

        if (this.confirming)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.20f, 0.18f, 1f));

            if (ImGui.Button("Click again to clear", new Vector2(180, 0)))
            {
                this.Clear?.Invoke();
                this.confirming = false;
            }

            ImGui.PopStyleColor();
            ImGui.SameLine();

            if (ImGui.Button("Cancel")) this.confirming = false;
        }
        else if (ImGui.Button("Clear tonight's encounters", new Vector2(180, 0)))
        {
            this.confirming = true;
        }

        ImGui.TextDisabled("Clears this plugin's own list only.");
        ImGui.TextDisabled("IINACT's network log is never touched.");
    }

    private static void Gesture(string what, string does)
    {
        ImGui.TextUnformatted(what);
        ImGui.SameLine(150);
        ImGui.TextDisabled(does);
    }
}
