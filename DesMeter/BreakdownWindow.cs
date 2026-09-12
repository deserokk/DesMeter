using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace DesMeter;

internal sealed class BreakdownWindow : Window
{
    private readonly Configuration config;

    private Snapshot? snapshot;
    private string who = string.Empty;

    internal BreakdownWindow(Configuration config) : base("DesMeter breakdown###DesMeterBreakdown")
    {
        this.config = config;

        this.Size = new Vector2(720, 460);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 320),
            MaximumSize = new Vector2(1600, 1200),
        };
    }

    internal void Show(Snapshot from, string player)
    {
        this.snapshot = from;
        this.who = player;
        this.IsOpen = true;
    }

    public override void Draw()
    {
        if (this.snapshot is not { Rows.Count: > 0 } snap)
        {
            ImGui.TextDisabled("Double-click a bar on the meter to open someone here.");
            return;
        }

        var title = string.IsNullOrEmpty(snap.Title) ? "Encounter" : snap.Title;
        ImGui.TextUnformatted($"{title}   ·   {snap.Duration}   ·   {snap.CapturedAt:HH:mm}");

        if (!string.IsNullOrEmpty(snap.Zone)) { ImGui.SameLine(); ImGui.TextDisabled($"  {snap.Zone}"); }

        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.BeginChild("##rail", new Vector2(180, 0), true))
        {
            this.DrawRail(snap);
            ImGui.EndChild();
        }

        ImGui.SameLine();

        if (ImGui.BeginChild("##detail", Vector2.Zero, false))
        {
            this.DrawDetail(snap);
            ImGui.EndChild();
        }
    }

    private void DrawRail(Snapshot snap)
    {
        foreach (var row in snap.Rows)
        {
            var colour = JobColours.For(row.Job, row.Name);

            ImGui.PushStyleColor(ImGuiCol.Text, colour);
            var picked = ImGui.Selectable($"{row.Name}##rail{row.Name}", row.Name == this.who);
            ImGui.PopStyleColor();

            if (picked) this.who = row.Name;
        }
    }

    private void DrawDetail(Snapshot snap)
    {
        var row = snap.Rows.Find(r => r.Name == this.who);

        if (row is null)
        {
            ImGui.TextDisabled("Pick someone on the left.");
            return;
        }

        ImGui.TextUnformatted(string.IsNullOrEmpty(row.Job) ? row.Name : $"{row.Name}   {row.Job}");
        ImGui.Spacing();

        if (ImGui.BeginTable("##stats", 4, ImGuiTableFlags.SizingStretchSame))
        {
            Stat("Damage", Format.Short(row.Damage));
            Stat("DPS", Format.Short(row.Dps));
            Stat("Share", $"{row.DamagePct:N1}%");
            Stat("Deaths", row.Deaths.ToString());

            Stat("Healing", Format.Short(row.Healed));
            Stat("HPS", Format.Short(row.Hps));
            Stat("Overheal", $"{row.OverHealPct:N0}%");
            Stat("Taken", Format.Short(row.DamageTaken));

            Stat("Crit", $"{row.CritPct:N1}%");
            Stat("Direct hit", $"{row.DirectHitPct:N1}%");
            Stat("Healing taken", Format.Short(row.HealsTaken));
            Stat("Biggest", string.IsNullOrEmpty(row.MaxHit) ? "—" : row.MaxHit);

            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextUnformatted("Share of the pull");
        ImGui.Spacing();

        this.DrawPie(snap, row.Name);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextDisabled("Abilities, targets and the over-time charts need the log reader.");
        ImGui.TextDisabled("They are not shown rather than shown wrong.");
    }

    private static void Stat(string label, string value)
    {
        ImGui.TableNextColumn();
        ImGui.TextDisabled(label);
        ImGui.TextUnformatted(value);
    }

    private void DrawPie(Snapshot snap, string highlight)
    {
        const float radius = 78f;

        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var centre = origin + new Vector2(radius + 4f, radius + 4f);

        var total = 0d;
        foreach (var r in snap.Rows) total += r.Damage;

        if (total <= 0)
        {
            ImGui.TextDisabled("No damage recorded.");
            return;
        }

        var angle = -MathF.PI / 2f;

        foreach (var r in snap.Rows)
        {
            if (r.Damage <= 0) continue;

            var sweep = (float)(r.Damage / total) * MathF.PI * 2f;
            var colour = JobColours.For(r.Job, r.Name);
            var lit = r.Name == highlight;

            var nudge = lit ? new Vector2(MathF.Cos(angle + (sweep / 2f)), MathF.Sin(angle + (sweep / 2f))) * 6f
                            : Vector2.Zero;

            dl.PathLineTo(centre + nudge);
            dl.PathArcTo(centre + nudge, radius, angle, angle + sweep, 48);
            dl.PathFillConvex(ImGui.GetColorU32(lit ? colour : colour with { W = 0.55f }));

            angle += sweep;
        }

        ImGui.SetCursorScreenPos(origin + new Vector2((radius * 2f) + 24f, 0));

        if (ImGui.BeginChild("##legend", new Vector2(0, (radius * 2f) + 8f), false))
        {
            foreach (var r in snap.Rows)
            {
                if (r.Damage <= 0) continue;

                ImGui.PushStyleColor(ImGuiCol.Text, JobColours.For(r.Job, r.Name));
                ImGui.TextUnformatted($"{r.Damage / total * 100:N1}%   {r.Name}");
                ImGui.PopStyleColor();
            }

            ImGui.EndChild();
        }

        ImGui.SetCursorScreenPos(origin + new Vector2(0, (radius * 2f) + 12f));
    }
}
