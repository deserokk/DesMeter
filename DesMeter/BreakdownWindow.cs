using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using DesMeter.UI;

namespace DesMeter;

internal sealed class BreakdownWindow : Window, IDisposable
{
    private readonly Configuration config;
    private readonly UiFonts fonts = new();

    private Snapshot? snapshot;
    private string who = string.Empty;

    private bool collapsed;

    private readonly Dictionary<string, Vector4> colours = new(StringComparer.Ordinal);

    private Snapshot? coloursFor;
    private bool coloursTeams;

    private readonly Func<string> localName;

    private Task<FightAbilities>? abilities;

    private Snapshot? abilitiesFor;
    private FightAbilities? reportedRecording;
    private CancellationTokenSource? cancel;

    private readonly HashSet<string> expanded = new(StringComparer.Ordinal);

    internal static readonly (string Name, Vector2 Size)[] Sizes =
    {
        ("Small", new Vector2(820f, 560f)),
        ("Medium", new Vector2(980f, 660f)),
        ("Large", new Vector2(1260f, 880f)),
        ("Extra large", new Vector2(1600f, 1100f)),
    };

    internal BreakdownWindow(Configuration config, Func<string> localName)
        : base("DesMeter breakdown###DesMeterBreakdown",
               ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar
               | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse)
    {
        this.config = config;
        this.localName = localName;
        Chrome.Attach(this.fonts);
    }

    internal void Show(Snapshot from, string player)
    {
        this.snapshot = from;
        this.who = player;
        this.collapsed = false;
        this.IsOpen = true;

        if (!ReferenceEquals(this.abilitiesFor, from))
        {
            this.cancel?.Cancel();
            this.cancel = new CancellationTokenSource();
            this.abilitiesFor = from;
            this.expanded.Clear();

            this.abilities = from.Title == "Overall" || IsPvp(from) || string.IsNullOrEmpty(from.LogFile)
                ? null
                : LogReader.ReadAsync(from, this.localName(), this.cancel.Token);
        }
    }

    public override void PreDraw()
    {
        Chrome.UserTextScale = this.config.BreakdownTextScale;
        this.fonts.Tick();

        var full = Sizes[Math.Clamp(this.config.BreakdownSize, 0, Sizes.Length - 1)].Size * Chrome.Scale;

        Chrome.CurrentSize = this.collapsed ? new Vector2(full.X, Chrome.HeaderHeight) : full;

        this.Size = Chrome.CurrentSize;
        this.SizeCondition = ImGuiCond.Always;

        Theme.Push();
    }

    public override void PostDraw() => Theme.Pop();

    public override void Draw()
    {
        using var _ = this.fonts.Body.Push();

        if (this.collapsed)
        {
            this.DrawCollapsed();
            return;
        }

        this.DrawRail();

        ImGui.SameLine(0f, 0f);

        var seam = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddRectFilled(seam, new Vector2(seam.X + 1f, seam.Y + ImGui.GetContentRegionAvail().Y),
                                                Theme.U(Theme.RuleHair));

        ImGui.SameLine(0f, 1f);

        if (ImGui.BeginChild("dm_body", new Vector2(0f, -1f), false, ImGuiWindowFlags.NoScrollbar))
        {

            var origin = ImGui.GetCursorScreenPos();
            var size = ImGui.GetContentRegionAvail();

            this.DrawHeaderStrip(origin, size.X);
            this.DrawMiddle(origin, size);
            this.DrawFooter(origin, size);
        }

        ImGui.EndChild();
    }

    private string HeaderTitle
        => this.snapshot is null ? "Breakdown" : this.who.Length > 0 ? this.Display(this.who) : "Pick a player";

    private void DrawCollapsed()
    {
        var s = Chrome.Scale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var draw = ImGui.GetWindowDrawList();

        draw.AddRectFilled(origin, origin + new Vector2(width, Chrome.HeaderHeight), Theme.U(Theme.Panel),
                           Chrome.WindowRounding);

        var tile = 22f * s;
        var tileAt = new Vector2(origin.X + (14f * s), origin.Y + ((Chrome.HeaderHeight - tile) * 0.5f));
        this.BrandTile(tileAt, tile);

        using (this.fonts.Title.Push())
        {
            var line = ImGui.GetTextLineHeight();
            Chrome.TrackedCaps(new Vector2(tileAt.X + tile + (10f * s), origin.Y + ((Chrome.HeaderHeight - line) * 0.5f)),
                               this.HeaderTitle, Theme.Ink);
        }

        this.DrawWindowButtons(origin, width);
    }

    private void DrawHeaderStrip(Vector2 origin, float width)
    {
        var draw = ImGui.GetWindowDrawList();

        draw.AddRectFilled(origin, new Vector2(origin.X + width, origin.Y + Chrome.HeaderHeight), Theme.U(Theme.Panel),
                           Chrome.WindowRounding, ImDrawFlags.RoundCornersTopRight);

        using (this.fonts.Title.Push())
        {
            var line = ImGui.GetTextLineHeight();
            Chrome.TrackedCaps(new Vector2(origin.X + Chrome.Pad, origin.Y + ((Chrome.HeaderHeight - line) * 0.5f)),
                               this.HeaderTitle, Theme.Ink);
        }

        this.DrawWindowButtons(origin, width);

        draw.AddRectFilled(new Vector2(origin.X, origin.Y + Chrome.HeaderHeight),
                           new Vector2(origin.X + width, origin.Y + Chrome.HeaderHeight + 1f), Theme.U(Theme.RuleHair));
    }

    private void DrawWindowButtons(Vector2 origin, float width)
    {
        var s = Chrome.Scale;
        var box = 30f * s;
        var top = origin.Y + ((Chrome.HeaderHeight - box) * 0.5f);

        ImGui.SetCursorScreenPos(new Vector2(origin.X + width - box - (14f * s), top));
        if (Chrome.GlyphButton("dm_close", FontAwesomeIcon.Times)) this.IsOpen = false;

        ImGui.SetCursorScreenPos(new Vector2(origin.X + width - (box * 2f) - (18f * s), top));
        if (Chrome.GlyphButton("dm_collapse", this.collapsed ? FontAwesomeIcon.WindowMaximize : FontAwesomeIcon.WindowMinimize))
            this.collapsed = !this.collapsed;
    }

    private void DrawRail()
    {
        var s = Chrome.Scale;
        var origin = ImGui.GetCursorScreenPos();
        var height = ImGui.GetContentRegionAvail().Y;

        ImGui.GetWindowDrawList().AddRectFilled(origin, new Vector2(origin.X + Chrome.RailWidth, origin.Y + height),
                                                Theme.U(Theme.Panel), Chrome.WindowRounding, ImDrawFlags.RoundCornersLeft);

        if (ImGui.BeginChild("dm_rail", new Vector2(Chrome.RailWidth, -1f), false, ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.Dummy(new Vector2(0f, 15f * s));
            ImGui.Indent(Chrome.RailPad);
            this.DrawBrand();
            ImGui.Unindent(Chrome.RailPad);
            ImGui.Dummy(new Vector2(0f, 15f * s));

            Chrome.Rule(Chrome.RailPad);
            ImGui.Dummy(new Vector2(0f, 8f * s));

            if (this.snapshot is { Rows.Count: > 0 } snap)
            {
                ImGui.Indent(Chrome.RailPad);
                var width = ImGui.GetContentRegionAvail().X - Chrome.RailPad;
                var total = 0d;
                foreach (var r in snap.Rows) total += r.Damage;

                foreach (var row in snap.Rows)
                {
                    if (this.PlayerRow(row, row.Name == this.who, width, total)) this.who = row.Name;
                }

                ImGui.Unindent(Chrome.RailPad);
            }
        }

        ImGui.EndChild();
    }

    private void DrawBrand()
    {
        var s = Chrome.Scale;
        var origin = ImGui.GetCursorScreenPos();
        var room = Chrome.RailWidth - (Chrome.RailPad * 2f);

        var what = this.snapshot is null
            ? "No encounter"
            : string.IsNullOrEmpty(this.snapshot.Title) ? "Encounter" : this.snapshot.Title;

        using (this.fonts.Title.Push())
            Chrome.TextAt(origin, Chrome.Fit(what, room), Theme.Ink);

        using (this.fonts.Label.Push())
        {
            if (this.snapshot is { Zone.Length: > 0 } snap)
                Chrome.TextAt(new Vector2(origin.X, origin.Y + (18f * s)), Chrome.Fit(snap.Zone, room), Theme.Faint);
        }

        ImGui.Dummy(new Vector2(room, 30f * s));
    }

    private void BrandTile(Vector2 at, float tile)
    {
        var s = Chrome.Scale;
        ImGui.GetWindowDrawList().AddRectFilled(at, at + new Vector2(tile, tile), Theme.U(Theme.Accent), 8f * s);

        var glyph = Chrome.IconSize(FontAwesomeIcon.ChartBar);
        Chrome.Icon(at + ((new Vector2(tile, tile) - glyph) * 0.5f), FontAwesomeIcon.ChartBar, Theme.OnColour(Theme.Accent));
    }

    private bool PlayerRow(MeterRow row, bool active, float width, double total)
    {
        var ts = Chrome.TextScale;
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();

        var clicked = ImGui.InvisibleButton($"##player{row.Name}", new Vector2(width, Chrome.RowHeight));
        var hovered = ImGui.IsItemHovered();

        if (active || hovered)
        {
            draw.AddRectFilled(origin, origin + new Vector2(width, Chrome.RowHeight),
                               Theme.U(active ? Theme.Raised : Theme.Field), 8f * Chrome.Scale);
        }

        var icon = 22f * ts;
        JobIcons.Draw(row.Job, new Vector2(origin.X + (8f * ts), origin.Y + ((Chrome.RowHeight - icon) * 0.5f)), icon);

        var share = total > 0 ? $"{row.Damage / total * 100:N1}%" : string.Empty;
        var shareWidth = 0f;

        using (this.fonts.Label.Push())
        {
            if (share.Length > 0)
            {
                var size = ImGui.CalcTextSize(share);
                shareWidth = size.X + (10f * ts);
                Chrome.TextAt(new Vector2(origin.X + width - size.X - (10f * ts), origin.Y + ((Chrome.RowHeight - size.Y) * 0.5f)),
                              share, Theme.Faint);
            }
        }

        var nameX = origin.X + (38f * ts);
        var label = Chrome.Fit(this.Display(row.Name), origin.X + width - shareWidth - nameX - (8f * ts));
        var line = ImGui.CalcTextSize(label);
        Chrome.TextAt(new Vector2(nameX, origin.Y + ((Chrome.RowHeight - line.Y) * 0.5f)), label,
                      active || hovered ? Theme.Ink : Theme.Dim);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + Chrome.RowHeight + (4f * Chrome.Scale)));
        return clicked;
    }

    private void DrawFooter(Vector2 bodyOrigin, Vector2 bodySize)
    {
        var origin = new Vector2(bodyOrigin.X, bodyOrigin.Y + bodySize.Y - Chrome.FooterHeight);
        var draw = ImGui.GetWindowDrawList();

        draw.AddRectFilled(origin, new Vector2(origin.X + bodySize.X, origin.Y + 1f), Theme.U(Theme.RuleHair));

        if (this.snapshot is not { } snap) return;

        var parts = new List<string>(4) { string.IsNullOrEmpty(snap.Title) ? "Encounter" : snap.Title, snap.Duration };
        if (!string.IsNullOrEmpty(snap.Zone)) parts.Add(snap.Zone);
        parts.Add(snap.CapturedAt.ToString("HH:mm"));

        var y = origin.Y + ((Chrome.FooterHeight - ImGui.GetTextLineHeight()) * 0.5f);
        Chrome.TextAt(new Vector2(origin.X + Chrome.Pad, y), Chrome.Fit(string.Join("  ·  ", parts), bodySize.X - (Chrome.Pad * 2f)),
                      Theme.Faint);
    }

    private void DrawMiddle(Vector2 bodyOrigin, Vector2 bodySize)
    {
        var top = bodyOrigin.Y + Chrome.HeaderHeight + 1f;
        var height = bodySize.Y - Chrome.HeaderHeight - 1f - Chrome.FooterHeight;

        ImGui.SetCursorScreenPos(new Vector2(bodyOrigin.X + Chrome.Pad, top + Chrome.Pad));

        if (ImGui.BeginChild("dm_middle", new Vector2(bodySize.X - (Chrome.Pad * 2f), height - (Chrome.Pad * 2f)), false))
        {
            if (this.snapshot is not { Rows.Count: > 0 } snap)
                ImGui.TextDisabled("Double-click a bar on the meter to open someone here.");
            else
                this.DrawDetail(snap);
        }

        ImGui.EndChild();
    }

    private enum View { Damage, Healing, Taken }

    private View view = View.Damage;

    private static readonly (View View, string Label)[] Pies =
    {
        (View.Damage, "Damage done"),
        (View.Healing, "Healing done"),
        (View.Taken, "Damage taken"),
    };

    private static double ValueOf(MeterRow row, View view) => view switch
    {
        View.Healing => row.Healed,
        View.Taken => row.DamageTaken,
        _ => row.Damage,
    };

    private string Display(string name)
    {
        if (name != "YOU") return name;

        var mine = this.localName();
        return mine.Length > 0 ? mine : name;
    }

    private static bool IsPvp(Snapshot s)
        => s.Pvp || s.TeamMode != 0 || s.Title is "Frontline" or "Crystalline Conflict" or "Rival Wings" or "PvP";

    private void DrawDetail(Snapshot snap)
    {
        if (IsPvp(snap))
        {
            ImGui.TextDisabled("PvP breakdown not built yet.");
            return;
        }

        var row = snap.Rows.Find(r => r.Name == this.who);

        if (row is null)
        {
            ImGui.TextDisabled("Pick someone on the left.");
            return;
        }

        Chrome.SectionLabel("Summary");

        ImGui.SameLine(0f, 10f * Chrome.Scale);
        using (this.fonts.Label.Push())
            ImGui.TextDisabled("(These figures are not 100% accurate and may differ from FFLogs figures slightly)");

        ImGui.Dummy(new Vector2(0f, 2f * Chrome.Scale));

        if (ImGui.BeginTable("##stats", 4, ImGuiTableFlags.SizingStretchSame))
        {
            this.Stat("Damage", Format.Short(row.Damage));
            this.Stat("DPS", Format.Short(row.Dps));
            this.Stat("Share", $"{row.DamagePct:N1}%");
            this.Stat("Deaths", row.Deaths.ToString());

            this.Stat("Healing", Format.Short(row.Healed));
            this.Stat("HPS", Format.Short(row.Hps));
            this.Stat("Overheal", $"{row.OverHealPct:N0}%");
            this.Stat("Taken", Format.Short(row.DamageTaken));

            this.Stat("Crit", $"{row.CritPct:N1}%");
            this.Stat("Direct hit", $"{row.DirectHitPct:N1}%");
            this.Stat("Healing taken", Format.Short(row.HealsTaken));
            this.Stat("Biggest", Biggest(row.MaxHit));

            ImGui.EndTable();
        }

        ImGui.Dummy(new Vector2(0f, 10f * Chrome.Scale));
        this.DrawPies(snap, row.Name);

        ImGui.Dummy(new Vector2(0f, 10f * Chrome.Scale));
        Chrome.SectionLabel(Pies[(int)this.view].Label);
        this.DrawAbilities(snap, row);
    }

    private void DrawPies(Snapshot snap, string highlight)
    {
        var s = Chrome.Scale;
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;

        var gap = 48f * s;
        var radius = MathF.Min(74f * s, (width - (gap * 2f)) / 6f);
        var labelHeight = 26f * s;
        var rowWidth = (radius * 6f) + (gap * 2f);
        var left = origin.X + ((width - rowWidth) * 0.5f);

        for (var i = 0; i < Pies.Length; i++)
        {
            var (view, label) = Pies[i];
            var centre = new Vector2(left + radius + (i * ((radius * 2f) + gap)), origin.Y + radius + 6f);
            var selected = view == this.view;

            var total = 0d;
            foreach (var r in snap.Rows) total += Math.Max(0, ValueOf(r, view));

            var hovered = PieHit(snap, view, centre, radius, total, out var inside);

            if (total <= 0)
            {
                dl.AddCircle(centre, radius, Theme.U(Theme.RuleStrong), 48, 2f * s);
            }
            else
            {
                var angle = -MathF.PI / 2f;

                foreach (var r in snap.Rows)
                {
                    var value = ValueOf(r, view);
                    if (value <= 0) continue;

                    var sweep = (float)(value / total) * MathF.PI * 2f;
                    var colour = this.ColourOf(r);
                    var picked = r.Name == highlight;
                    var under = r.Name == hovered;

                    var out_ = picked ? 6f * s : under ? 3f * s : 0f;
                    var nudge = out_ > 0
                        ? new Vector2(MathF.Cos(angle + (sweep / 2f)), MathF.Sin(angle + (sweep / 2f))) * out_
                        : Vector2.Zero;

                    var alpha = selected ? (picked || under ? 1f : 0.62f) : (picked || under ? 0.7f : 0.32f);

                    dl.PathLineTo(centre + nudge);
                    dl.PathArcTo(centre + nudge, radius, angle, angle + sweep, 48);
                    dl.PathFillConvex(Theme.U(colour with { W = alpha }));

                    angle += sweep;
                }
            }

            using (this.fonts.Label.Push())
            {
                var text = label.ToUpperInvariant();
                var size = ImGui.CalcTextSize(text);
                var tracked = size.X + (text.Length * ImGui.GetFontSize() * 0.14f);
                var at = new Vector2(centre.X - (tracked * 0.5f), centre.Y + radius + (10f * s));
                Chrome.TrackedCaps(at, text, selected ? Theme.Accent : inside ? Theme.Dim : Theme.Faint);

                if (selected)
                    dl.AddRectFilled(new Vector2(at.X, at.Y + size.Y + (4f * s)), new Vector2(at.X + tracked, at.Y + size.Y + (6f * s)),
                                     Theme.U(Theme.Accent), 1f * s);
            }

            if (hovered is { Length: > 0 } && snap.Rows.Find(r => r.Name == hovered) is { } over)
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(this.Display(over.Name));
                ImGui.TextDisabled($"{Format.Short(ValueOf(over, view))}   {ValueOf(over, view) / total * 100:N1}% of {label.ToLowerInvariant()}");
                ImGui.EndTooltip();
            }

            if (inside && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                this.view = view;
                if (hovered is { Length: > 0 }) this.who = hovered;
            }
        }

        ImGui.Dummy(new Vector2(width, (radius * 2f) + 12f + labelHeight));
    }

    private static string? PieHit(Snapshot snap, View view, Vector2 centre, float radius, double total, out bool inside)
    {
        inside = false;
        if (!ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows)) return null;

        var d = ImGui.GetMousePos() - centre;
        if (d.LengthSquared() > radius * radius) return null;

        inside = true;
        if (total <= 0) return null;

        var a = MathF.Atan2(d.Y, d.X) + (MathF.PI / 2f);
        if (a < 0) a += MathF.PI * 2f;

        var angle = 0f;

        foreach (var r in snap.Rows)
        {
            var value = ValueOf(r, view);
            if (value <= 0) continue;

            var sweep = (float)(value / total) * MathF.PI * 2f;
            if (a >= angle && a < angle + sweep) return r.Name;

            angle += sweep;
        }

        return null;
    }

    private enum Part { Whole, Hit, Ticks, Shield }

    private sealed record Column(string Header, float Width, Func<AbilityLine, Part, double, string> Text);

    private static readonly Column[] DamageColumns =
    {
        new("Casts", 44f, (l, p, _) => p == Part.Ticks ? "-" : Count(l.Casts)),
        new("Avg Cast", 96f, (l, p, _) => p switch
        {
            Part.Whole when l.Ticks > 0 => $"{Short(l.Direct / Casts(l))} ({Short(l.TickAmount / Casts(l))})",
            Part.Ticks => "-",
            Part.Hit => Short(l.Direct / Casts(l)),
            _ => Short(l.Amount / Casts(l)),
        }),
        new("Hits", 56f, (l, p, _) => p switch
        {
            Part.Whole when l.Ticks > 0 => $"{l.Hits} ({l.Ticks})",
            Part.Ticks => l.Ticks.ToString(),
            _ => l.Hits.ToString(),
        }),
        new("Avg Hit", 96f, (l, p, _) => p switch
        {
            Part.Whole when l.Ticks > 0 => $"{Short(l.Direct / Hits(l))} ({Short(l.TickAmount / l.Ticks)})",
            Part.Ticks => Short(l.TickAmount / Math.Max(1, l.Ticks)),
            _ => Short(l.Direct / Hits(l)),
        }),
        new("Crit %", 52f, (l, p, _) => p == Part.Ticks ? "-" : Pct(l.Crits, l.Hits)),
        new("DHit %", 52f, (l, p, _) => p == Part.Ticks ? "-" : Pct(l.DirectHits, l.Hits)),
        new("Uptime %", 64f, (l, p, sec) => p == Part.Hit || l.Uptime <= 0 ? "-" : $"{l.Uptime / sec * 100:N2}%"),
    };

    private static readonly Column[] HealingColumns =
    {
        new("Casts", 44f, (l, p, _) => p is Part.Ticks or Part.Shield ? "-" : Count(l.Casts)),
        new("Avg Cast", 96f, (l, p, _) => p is Part.Ticks or Part.Shield || l.Casts == 0 ? "-" : Short((p == Part.Hit ? l.Direct : l.Amount) / Casts(l))),
        new("Hits", 56f, (l, p, _) => p switch
        {
            Part.Shield => "-",
            Part.Whole when l.Ticks > 0 && l.Hits > 0 => $"{l.Hits} ({l.Ticks})",
            Part.Whole when l.Ticks > 0 => l.Ticks.ToString(),
            Part.Ticks => l.Ticks.ToString(),
            _ => Count(l.Hits),
        }),
        new("Avg Hit", 96f, (l, p, _) => p switch
        {
            Part.Shield => "-",
            Part.Ticks => Short(l.TickAmount / Math.Max(1, l.Ticks)),
            Part.Whole when l.Hits == 0 && l.Ticks > 0 => Short(l.TickAmount / l.Ticks),
            Part.Whole when l.Hits == 0 => "-",
            _ => Short(l.Direct / Hits(l)),
        }),
        new("Crit %", 52f, (l, p, _) => p is Part.Ticks or Part.Shield ? "-" : Pct(l.Crits, l.Hits)),
        new("Uptime %", 64f, (l, p, sec) => p == Part.Hit || l.Uptime <= 0 ? "-" : $"{l.Uptime / sec * 100:N2}%"),
        new("Overheal", 64f, (l, p, _) =>
        {
            if (p == Part.Shield) return "-";

            var raw = p switch { Part.Hit => l.Raw, Part.Ticks => l.TickRaw, _ => l.Raw + l.TickRaw };
            var landed = p switch { Part.Hit => l.Direct, Part.Ticks => l.TickAmount, _ => l.Amount };
            return raw > 0 && raw > landed ? $"{(1 - (landed / raw)) * 100:N2}%" : "-";
        }),
    };

    private static readonly Column[] TakenColumns =
    {
        new("Hits", 56f, (l, _, _) => l.Hits.ToString()),
        new("Avg Hit", 96f, (l, _, _) => Short(l.Direct / Hits(l))),
    };

    private static double Casts(AbilityLine l) => Math.Max(1, l.Casts);

    private static double Hits(AbilityLine l) => Math.Max(1, l.Hits);

    private static string Count(int n) => n > 0 ? n.ToString() : "-";

    private void DrawAbilities(Snapshot snap, MeterRow row)
    {
        if (this.abilities is null)
        {
            ImGui.TextDisabled(snap.Title == "Overall"
                ? "Abilities are per pull. Open a single fight from the list."
                : "This fight has no log position recorded, so it can't be read.");
            return;
        }

        if (!this.abilities.IsCompleted)
        {
            ImGui.TextDisabled("Reading the log...");
            return;
        }

        if (this.abilities.IsFaulted || this.abilities.IsCanceled)
        {
            ImGui.TextDisabled("Couldn't read the log for this fight.");
            return;
        }

        var fight = this.abilities.Result;

        if (!ReferenceEquals(this.reportedRecording, fight))
        {
            this.reportedRecording = fight;
            if (fight.Recording.Length > 0) Probe.Line($"breakdown {snap.Title}: {fight.Recording}");
        }

        if (fight.Problem is { } problem)
        {
            ImGui.TextDisabled(problem);
            return;
        }

        var (table, columns, rate, note) = this.view switch
        {
            View.Healing => (fight.Healing, HealingColumns, "HPS", "*Ticks and shields are estimated"),
            View.Taken => (fight.Taken, TakenColumns, "DTPS", string.Empty),
            _ => (fight.Damage, DamageColumns, "DPS", "*DoT ticks are estimated"),
        };

        var name = this.Display(row.Name);
        if (!table.TryGetValue(name, out var lines) || lines.Count == 0)
        {
            ImGui.TextDisabled("Nothing for this player in the log.");
            return;
        }

        var total = 0d;
        var biggest = 0d;
        foreach (var l in lines)
        {
            total += l.Amount;
            biggest = Math.Max(biggest, l.Amount);
        }

        var seconds = Math.Max(1, fight.Seconds);
        var colour = this.ColourOf(row);
        var s = Chrome.Scale;

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.PadOuterX;

        if (!ImGui.BeginTable($"##abilities{this.view}", columns.Length + 3, flags)) return;

        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableSetupColumn("Amount", ImGuiTableColumnFlags.WidthStretch, 1.4f);

        var ts = Chrome.TextScale;
        foreach (var c in columns) ImGui.TableSetupColumn(c.Header, ImGuiTableColumnFlags.WidthFixed, c.Width * ts);
        ImGui.TableSetupColumn(rate, ImGuiTableColumnFlags.WidthFixed, 64f * ts);
        ImGui.TableHeadersRow();

        foreach (var line in lines)
        {
            var parts = (line.Ticks > 0 ? 1 : 0) + (line.Hits > 0 ? 1 : 0) + (line.Absorbed > 0 ? 1 : 0);
            var expandable = line.Ticks > 0 || (line.Absorbed > 0 && parts > 1);
            var open = expandable && this.expanded.Contains($"{this.view}{line.Name}");

            this.AbilityRow(line.Name, line.Name, line, Part.Whole, line.Amount, total, biggest, seconds, colour, columns,
                            expandable: expandable, open: open, shieldTail: line.Absorbed, estimated: line.Absorbed > 0 && parts == 1);

            if (!open) continue;

            if (line.Ticks > 0)
                this.AbilityRow(line.Name, $"{line.Name}##tick", line, Part.Ticks, line.TickAmount, total, biggest, seconds, colour,
                                columns, indent: true, estimated: true);

            if (line.Hits > 0)
                this.AbilityRow(line.Name, $"{line.Name}##hit", line, Part.Hit, line.Direct, total, biggest, seconds, colour, columns,
                                indent: true);

            if (line.Absorbed > 0)
                this.AbilityRow(line.Name, $"{line.Name} (shield)", line, Part.Shield, line.Absorbed, total, biggest, seconds,
                                colour, columns, indent: true, estimated: true, shieldTail: line.Absorbed);
        }

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted("Total");
        ImGui.TableNextColumn();
        ImGui.TextUnformatted($"100%   {Short(total)}");
        foreach (var _ in columns) ImGui.TableNextColumn();
        ImGui.TableNextColumn();
        RightAligned($"{total / seconds:N1}");

        ImGui.EndTable();

        if (note.Length > 0) ImGui.TextDisabled(note);
    }

    private void AbilityRow(string icon, string label, AbilityLine data, Part part, double amount, double total, double biggest,
                            double seconds, Vector4 colour, Column[] columns, bool expandable = false, bool open = false,
                            bool indent = false, bool estimated = false, double shieldTail = 0)
    {
        var s = Chrome.Scale;
        var dl = ImGui.GetWindowDrawList();
        var line = ImGui.GetTextLineHeight();

        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        var at = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var x = at.X + (indent ? 18f * s : 0f);
        var shown = label.Split("##")[0] + (estimated ? "*" : string.Empty);

        var used = Abilities.Icon(dl, new Vector2(x, at.Y), line, icon);
        Chrome.TextAt(new Vector2(x + used + (used > 0 ? 6f * s : 0f), at.Y), Chrome.Fit(shown, width - used - (24f * s)),
                      indent ? Theme.Dim : Theme.Ink);

        if (expandable)
        {
            var key = $"{this.view}{label}";
            ImGui.InvisibleButton($"##expand{key}", new Vector2(width, line));
            if (ImGui.IsItemClicked() && !this.expanded.Remove(key)) this.expanded.Add(key);

            var glyph = open ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight;
            var size = Chrome.IconSize(glyph);
            Chrome.Icon(new Vector2(at.X + width - size.X, at.Y + ((line - size.Y) * 0.5f)), glyph, Theme.Dim);
        }
        else
        {
            ImGui.Dummy(new Vector2(width, line));
        }

        ImGui.TableNextColumn();
        {
            var cell = ImGui.GetCursorScreenPos();
            var room = ImGui.GetContentRegionAvail().X;
            var value = Short(amount);
            var pctWidth = 54f * s;
            var valueWidth = ImGui.CalcTextSize(value).X;
            var barRoom = MathF.Max(0f, room - pctWidth - valueWidth - (10f * s));
            var barLength = biggest > 0 ? barRoom * (float)(amount / biggest) : 0f;
            var barHeight = line * 0.62f;
            var barTop = cell.Y + ((line - barHeight) * 0.5f);

            Chrome.TextAt(cell, total > 0 ? $"{amount / total * 100:N2}%" : "-", Theme.Dim);

            var tail = amount > 0 ? barLength * (float)(Math.Min(shieldTail, amount) / amount) : 0f;
            var lit = barLength - tail;
            var barLeft = cell.X + pctWidth;

            if (lit > 0)
                dl.AddRectFilled(new Vector2(barLeft, barTop), new Vector2(barLeft + lit, barTop + barHeight),
                                 Theme.U(estimated && tail <= 0 ? colour with { W = 0.5f } : colour), 2f * s);

            if (tail > 0)
                dl.AddRectFilled(new Vector2(barLeft + lit, barTop), new Vector2(barLeft + barLength, barTop + barHeight),
                                 Theme.U(new Vector4(colour.X * 0.45f, colour.Y * 0.45f, colour.Z * 0.45f, 1f)), 2f * s);
            Chrome.TextAt(new Vector2(cell.X + room - valueWidth, cell.Y), value, estimated ? Theme.Dim : Theme.Ink);
            ImGui.Dummy(new Vector2(room, line));
        }

        foreach (var c in columns)
        {
            ImGui.TableNextColumn();
            RightAligned(c.Text(data, part, seconds));
        }

        ImGui.TableNextColumn();
        RightAligned((amount / seconds).ToString("N1"));
    }

    private static void RightAligned(string text)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var size = ImGui.CalcTextSize(text).X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, width - size));
        ImGui.TextUnformatted(text);
    }

    private static string Pct(int part, int of) => of > 0 && part > 0 ? $"{part * 100.0 / of:N1}%" : "-";

    private static string Short(double v) => v switch
    {
        >= 1e6 => $"{v / 1e6:0.00}m",
        >= 1e3 => $"{v / 1e3:0.0}k",
        _ => $"{v:0}",
    };

    private Vector4 ColourOf(MeterRow row)
    {
        var teams = this.config.TeamColours;

        if (!ReferenceEquals(this.coloursFor, this.snapshot) || this.coloursTeams != teams)
        {
            this.colours.Clear();
            this.coloursFor = this.snapshot;
            this.coloursTeams = teams;
        }

        if (!this.colours.TryGetValue(row.Name, out var colour))
            this.colours[row.Name] = colour = Teams.Colour(row, this.snapshot?.TeamMode ?? 0, teams);

        return colour;
    }

    private void Stat(string label, string value)
    {
        ImGui.TableNextColumn();

        var s = Chrome.Scale;
        var at = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var bodyHeight = ImGui.GetTextLineHeight();

        float valueHeight;
        using (this.fonts.Title.Push()) valueHeight = ImGui.GetTextLineHeight();

        Chrome.TextAt(new Vector2(at.X, at.Y + ((valueHeight - bodyHeight) * 0.5f)), label, Theme.Dim);
        var gap = ImGui.CalcTextSize(label).X + (8f * s);

        using (this.fonts.Title.Push())
            Chrome.TextAt(new Vector2(at.X + gap, at.Y), Chrome.Fit(value, width - gap - (8f * s)), Theme.Ink);

        ImGui.Dummy(new Vector2(width, valueHeight + (6f * s)));
    }

    private static string Biggest(string maxHit)
    {
        if (string.IsNullOrEmpty(maxHit)) return "-";

        var (name, amount) = Abilities.Split(maxHit);
        return double.TryParse(amount, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? $"{Format.Short(v)} {name}"
            : maxHit;
    }

    public void Dispose()
    {
        this.cancel?.Cancel();
        this.fonts.Dispose();
    }
}
