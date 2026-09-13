using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;

namespace DesMeter;

internal sealed class MeterWindow : Window
{

    private enum Panel
    {
        None,
        Displays,
        Segments,
    }

    private readonly IinactLink link;
    private readonly Configuration config;
    private readonly OptionsWindow options;

    internal readonly MeterSettings Settings;

    private Panel panel;

    private Snapshot? pinned;

    private Snapshot? shown;

    private int scroll;

    private int panelScroll;

    private bool viewChosen;

    private readonly List<Rendered> view = new();

    private Snapshot? viewOf;
    private Metric viewMetric;
    private bool viewPets, viewShowLb, viewCountLb;
    private double viewMax, viewDenominator, viewRate;

    private readonly List<Rendered> visible = new();

    private readonly Dictionary<string, float> lengths = new(StringComparer.Ordinal);

    private float Ease => 3f / Math.Max(0.25f, this.link.Cadence);

    private readonly record struct Rendered(int Rank, MeterRow Row, string Left, string Right);

    private bool overall;

    internal Action? Clear { get; set; }

    internal Action<Snapshot, string>? OpenBreakdown { get; set; }

    private DateTime armedUntil = DateTime.MinValue;

    private static readonly Vector4 Glass = new(0.05f, 0.06f, 0.09f, 0.78f);
    private static readonly Vector4 Edge = new(1f, 1f, 1f, 0.10f);

    private static readonly Vector4 Track = new(0.09f, 0.10f, 0.12f, 0.55f);
    private static readonly Vector4 Dim = new(0.72f, 0.76f, 0.84f, 0.75f);
    private static readonly Vector4 Chosen = new(0.85f, 0.62f, 0.28f, 0.85f);
    private static readonly Vector4 Plain = new(0.28f, 0.42f, 0.58f, 0.80f);

    private const float Rounding = 6f;

    private const float BarRounding = 0f;
    private const float MinWidth = 190f;
    private const float MinHeight = 54f;
    private const float GripSize = 16f;

    private const float TextInset = 6f;

    private const float IconScale = 0.78f;

    private const float ArtScale = 1.45f;

    private const float CaptionGap = 10f;

    private const float CaptionScale = 0.85f;

    private const float NameScale = 1.3f;

    private static readonly Vector4 Frost = new(0.055f, 0.06f, 0.075f, 0.94f);

    private const int ScrollSpeed = 2;

    private const float DeathScale = 1.4f;

    private Vector2? nextPos;

    private Vector2? nextSize;

    private const ImGuiWindowFlags BaseFlags =
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar
      | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse;

    internal MeterWindow(IinactLink link, Configuration config, OptionsWindow options,
                         MeterSettings settings)
        : base($"DesMeter {settings.Id}###DesMeter{settings.Id}", BaseFlags)
    {
        this.link = link;
        this.config = config;
        this.options = options;
        this.Settings = settings;

        this.Size = new Vector2(300, 180);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(190, 54),
            MaximumSize = new Vector2(900, 900),
        };
    }

    public override bool DrawConditions()
        => Plugin.ClientState.IsLoggedIn
        && !Plugin.GameGui.GameUiHidden

        && !Plugin.Condition[ConditionFlag.BetweenAreas]
        && !Plugin.Condition[ConditionFlag.BetweenAreas51]

        && !Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent]
        && !Plugin.Condition[ConditionFlag.WatchingCutscene]
        && !Plugin.Condition[ConditionFlag.WatchingCutscene78];

    public override void PreDraw()
    {

        var ownGrip = this.Settings.GrowUpward && !this.Settings.Locked;

        this.Flags = BaseFlags
                   | (this.Settings.Locked ? ImGuiWindowFlags.NoMove : ImGuiWindowFlags.None)
                   | (this.Settings.Locked || ownGrip ? ImGuiWindowFlags.NoResize : ImGuiWindowFlags.None);

        if (this.nextSize is { } wantedSize)
        {
            this.Size = wantedSize;
            this.SizeCondition = ImGuiCond.Always;
            this.nextSize = null;
        }
        else
        {

            this.SizeCondition = ImGuiCond.FirstUseEver;
        }

        if (this.nextPos is { } wantedPos)
        {
            this.Position = wantedPos;
            this.PositionCondition = ImGuiCond.Always;
            this.nextPos = null;
        }
        else
        {
            this.Position = null;
        }

        var body = this.Settings.Background;
        var bare = body.W <= 0.02f;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Rounding);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, bare ? 0f : 1f);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 6));

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);

        ImGui.PushStyleColor(ImGuiCol.WindowBg, body);
        ImGui.PushStyleColor(ImGuiCol.Border, bare ? Vector4.Zero : Edge);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(4);
    }

    public override void Draw()
    {

        ImGui.SetWindowFontScale(this.Settings.FontScale);

        if (this.Settings.Background.W > 0.02f) this.DrawSheen();

        if (this.Settings.GrowUpward && !this.Settings.Locked) this.DrawTopGrip();

        this.HandleRightClick();

        if (this.panel != Panel.None)
        {
            this.DrawPanel();
            return;
        }

        var wholeMatch = this.Settings.PvpWholeMatch && !this.viewChosen && Plugin.ClientState.IsPvP;

        if (this.link.Stalled) this.DrawStalled();

        this.DrawMeter(this.overall || wholeMatch
            ? this.link.History.Overall()
            : this.pinned ?? this.link.Current);
    }

    private void DrawStalled()
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();

        dl.AddRect(pos, pos + size, ImGui.GetColorU32(new Vector4(0.90f, 0.35f, 0.30f, 0.85f)),
                   Rounding, ImDrawFlags.RoundCornersAll, 2f);

        var at = ImGui.GetCursorScreenPos() with { X = pos.X + TextInset };
        Shadow(at, ImGui.GetColorU32(new Vector4(1f, 0.55f, 0.50f, 1f)), "IINACT has stopped parsing");

        ImGui.Dummy(new Vector2(size.X, ImGui.GetTextLineHeight() + 2));

        Shadow(ImGui.GetCursorScreenPos() with { X = pos.X + TextInset },
               ImGui.GetColorU32(Dim), "restart the game to fix it");

        ImGui.Dummy(new Vector2(size.X, ImGui.GetTextLineHeight() + 4));
    }

    private void DrawEmpty()
    {
        var region = ImGui.GetContentRegionAvail();
        var origin = ImGui.GetCursorScreenPos();
        var up = this.Settings.GrowUpward;

        if (!up)
        {
            this.DrawHeader(string.Empty, string.Empty, atBottom: false);
            this.DrawTrouble();
            return;
        }

        this.DrawTrouble();
        ImGui.SetCursorScreenPos(origin with { Y = origin.Y + region.Y - HeaderHeight });
        this.DrawHeader(string.Empty, string.Empty, atBottom: true);
    }

    private void DrawTrouble()
    {
        if (this.link.Connected) return;

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + TextInset);
        ImGui.TextColored(Dim, this.link.Trouble ?? "Connecting to IINACT...");
    }

    internal void ResetView()
    {
        this.overall = false;
        this.pinned = null;
        this.scroll = 0;
        this.viewChosen = false;
        this.lengths.Clear();
    }

    internal void TerritoryChanged() => this.viewChosen = false;

    private void DrawTopGrip()
    {
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var cursor = ImGui.GetCursorScreenPos();

        ImGui.PushClipRect(pos, pos + size, false);

        var at = new Vector2(pos.X + size.X - GripSize, pos.Y);

        ImGui.SetCursorScreenPos(at);
        ImGui.InvisibleButton("##grip", new Vector2(GripSize, GripSize));

        var active = ImGui.IsItemActive();
        var hot = active || ImGui.IsItemHovered();
        ImGui.SetCursorScreenPos(cursor);

        if (active)
        {
            var delta = ImGui.GetIO().MouseDelta;
            var bottom = pos.Y + size.Y;

            var width = Math.Max(size.X + delta.X, MinWidth);
            var height = Math.Max(size.Y - delta.Y, MinHeight);

            this.nextSize = new Vector2(width, height);
            this.nextPos = new Vector2(pos.X, bottom - height);
        }

        var colour = ImGui.GetColorU32(hot ? ImGuiCol.ResizeGripHovered : ImGuiCol.ResizeGrip);

        var inset = Rounding * 0.5f;
        var tip = new Vector2(at.X + GripSize - inset, at.Y + inset);

        ImGui.GetWindowDrawList().AddTriangleFilled(
            tip,
            tip with { Y = at.Y + GripSize },
            new Vector2(at.X, at.Y + inset),
            colour);

        ImGui.PopClipRect();
    }

    private void HandleRightClick()
    {
        if (!ImGui.IsWindowHovered() || !ImGui.IsMouseClicked(ImGuiMouseButton.Right)) return;

        if (this.panel != Panel.None)
        {
            this.panel = Panel.None;
            return;
        }

        var io = ImGui.GetIO();

        if (io.KeyCtrl)
        {
            this.IsOpen = false;
            return;
        }

        this.OpenPanel(io.KeyShift ? Panel.Segments : Panel.Displays);
    }

    private void OpenPanel(Panel which)
    {
        this.panel = which;
        this.panelScroll = 0;
    }

    private int PanelWindow(int count, float rowHeight, out int first)
    {
        var room = Math.Max(1, (int)(ImGui.GetContentRegionAvail().Y / rowHeight));

        if (ImGui.IsWindowHovered() && ImGui.GetIO().MouseWheel is var wheel and not 0)
            this.panelScroll -= (int)wheel * ScrollSpeed;

        this.panelScroll = Math.Clamp(this.panelScroll, 0, Math.Max(0, count - room));
        first = this.panelScroll;

        return Math.Min(room, count - first);
    }

    private void DrawMeter(Snapshot? snapshot)
    {
        this.shown = snapshot;

        if (snapshot is not { Rows.Count: > 0 } snap)
        {
            this.DrawEmpty();
            return;
        }

        var metric = this.Settings.Metric;
        var title = string.IsNullOrEmpty(snap.Title) ? "Encounter" : snap.Title;

        if (this.pinned is not null) title = $"{snap.CapturedAt:HH:mm}  {title}";
        if (Label(metric) is { Length: > 0 } what) title += $"  ·  {what}";

        this.Rebuild(snap, metric);
        var caption = $"{Format.Short(this.viewRate)}  ·  {snap.Duration}";

        var region = ImGui.GetContentRegionAvail();
        var origin = ImGui.GetCursorScreenPos();
        var barHeight = this.Settings.RowHeight > 0f
            ? this.Settings.RowHeight
            : ImGui.GetTextLineHeight() + 6f;

        var rowHeight = barHeight + this.Settings.RowSpacing;
        var headerHeight = HeaderHeight;
        var up = this.Settings.GrowUpward;

        var capacity = Math.Max(1, (int)((region.Y - headerHeight - this.Settings.HeaderClearance) / rowHeight));

        var ordered = this.view;

        if (ImGui.IsWindowHovered() && ImGui.GetIO().MouseWheel is var wheel and not 0)
            this.scroll -= (int)wheel * ScrollSpeed;

        this.scroll = Math.Clamp(this.scroll, 0, Math.Max(0, ordered.Count - capacity));

        var visible = this.Choose(ordered, capacity);
        var max = this.viewMax;

        var gaps = 0;
        for (var i = 1; i < visible.Count; i++)
            if (visible[i].Rank != visible[i - 1].Rank + 1) gaps++;

        if (up)
        {

            var blockHeight = (visible.Count * rowHeight) + (gaps * 5f);
            var top = origin.Y + region.Y - headerHeight - this.Settings.HeaderClearance - blockHeight;

            ImGui.SetCursorScreenPos(origin with { Y = top });
            this.DrawRows(visible, max, region.X, rowHeight);

            ImGui.SetCursorScreenPos(origin with { Y = origin.Y + region.Y - headerHeight });
            this.DrawHeader(title, caption, atBottom: true);
            return;
        }

        this.DrawHeader(title, caption, atBottom: false);
        ImGui.Dummy(new Vector2(region.X, this.Settings.HeaderClearance));
        this.DrawRows(visible, max, region.X, rowHeight);
    }

    private void DrawRows(List<Rendered> visible, double max, float width, float rowHeight)
    {
        var previousRank = 0;

        foreach (var entry in visible)
        {
            if (previousRank != 0 && entry.Rank != previousRank + 1) DrawGapRule();

            this.DrawRow(entry, max, width, rowHeight);
            previousRank = entry.Rank;
        }
    }

    private static string Label(Metric metric) => metric switch
    {
        Metric.Healing => "Healing",
        Metric.DamageTaken => "Damage taken",
        Metric.HealingTaken => "Healing taken",
        _ => string.Empty,
    };

    private static double Value(MeterRow row, Metric metric) => metric switch
    {
        Metric.Healing => row.Healed,
        Metric.DamageTaken => row.DamageTaken,
        Metric.HealingTaken => row.HealsTaken,
        _ => row.Damage,
    };

    private void Rebuild(Snapshot snap, Metric metric)
    {
        if (ReferenceEquals(this.viewOf, snap)
            && this.viewMetric == this.Settings.Metric
            && this.viewPets == this.config.CombinePets
            && this.viewShowLb == this.Settings.ShowLimitBreak
            && this.viewCountLb == this.Settings.CountLimitBreak)
            return;

        this.viewOf = snap;
        this.viewMetric = this.Settings.Metric;
        this.viewPets = this.config.CombinePets;
        this.viewShowLb = this.Settings.ShowLimitBreak;
        this.viewCountLb = this.Settings.CountLimitBreak;

        this.view.Clear();

        this.viewDenominator = 0;
        this.viewRate = metric switch
        {
            Metric.Healing => snap.RaidHps,
            Metric.Damage => snap.RaidDps,

            _ => 0,
        };

        if (!this.Settings.CountLimitBreak
            && snap.Rows.FirstOrDefault(r => IsLimitBreak(r.Name)) is { } limitBreak)
        {
            foreach (var r in snap.Rows)
                if (!IsLimitBreak(r.Name))
                    this.viewDenominator += Value(r, metric);

            if (snap.DurationSeconds > 0)
                this.viewRate -= Value(limitBreak, metric) / snap.DurationSeconds;
        }

        if (metric is Metric.DamageTaken or Metric.HealingTaken)
        {
            this.viewDenominator = 0;
            foreach (var r in snap.Rows) this.viewDenominator += Value(r, metric);

            if (snap.DurationSeconds > 0)
                this.viewRate = this.viewDenominator / snap.DurationSeconds;
        }

        var denominator = this.viewDenominator;

        var rows = this.Combine(snap.Rows)
                       .Where(r => this.Settings.ShowLimitBreak || !IsLimitBreak(r.Name))
                       .OrderByDescending(r => Value(r, metric));

        var rank = 0;
        this.viewMax = 0;

        foreach (var row in rows)
        {
            rank++;

            var total = Value(row, metric);
            var rate = metric switch
            {
                Metric.Healing => row.Hps,
                Metric.Damage => row.Dps,
                _ => 0,
            };

            var share = denominator > 0
                ? total / denominator * 100
                : metric switch
                {
                    Metric.Healing => row.HealedPct,
                    Metric.Damage => row.DamagePct,
                    _ => 0,
                };

            if (total > this.viewMax) this.viewMax = total;

            var right = rate > 0
                ? $"{Format.Short(total)} ({Format.Short(rate)}, {share:N1}%)"
                : $"{Format.Short(total)} ({share:N1}%)";

            this.view.Add(new Rendered(rank, row, $"{rank}. {row.Name}", right));
        }
    }

    private void DrawPanel()
    {
        var avail = ImGui.GetContentRegionAvail();
        var rowHeight = ImGui.GetTextLineHeight() + 6f;

        if (this.panel == Panel.Displays)
        {
            this.DrawHeader("Showing", "right-click to go back", atBottom: false);

            var shown = this.PanelWindow(Displays.Length, rowHeight, out var from);

            for (var i = from; i < from + shown; i++)
                this.Choice(Displays[i].Label, Displays[i].Metric, i, avail.X, rowHeight);

            return;
        }

        var segments = this.link.History.Recent();
        this.DrawHeader("Tonight", segments.Count == 1 ? "1 encounter" : $"{segments.Count} encounters",
                        atBottom: false);

        var counted = this.link.History.OverallCount;

        var total = segments.Count + 2;
        var visible = this.PanelWindow(total, rowHeight, out var first);

        var biggest = segments.Count > 0 ? segments.Max(s => s.TotalDamage) : 0;

        for (var i = first; i < first + visible; i++)
        {
            if (i == 0)
            {
                var since = counted == 1 ? "1 pull here" : $"{counted} pulls here";

                if (this.Block("Overall", since, 1f, this.overall, 0, avail.X, rowHeight))
                    this.Look(overall: true, null);

                continue;
            }

            if (i == 1)
            {
                if (this.Block("Live", string.Empty, 1f, !this.overall && this.pinned is null,
                               1, avail.X, rowHeight))
                    this.Look(overall: false, null);

                continue;
            }

            var segment = segments[i - 2];
            var fraction = biggest > 0 ? (float)(segment.TotalDamage / biggest) : 0f;
            var label = $"{segment.CapturedAt:HH:mm}  {segment.Title}";
            var value = $"{segment.Duration}  {Format.Short(segment.RaidDps)}";

            if (this.Block(label, value, fraction, ReferenceEquals(this.pinned, segment),
                           i, avail.X, rowHeight))
                this.Look(overall: false, segment);
        }
    }

    private void Look(bool overall, Snapshot? segment)
    {
        this.overall = overall;
        this.pinned = segment;
        this.scroll = 0;
        this.viewChosen = true;
        this.lengths.Clear();
        this.panel = Panel.None;
    }

    private static readonly (string Label, Metric Metric)[] Displays =
    {
        ("Damage Done", Metric.Damage),
        ("Healing Done", Metric.Healing),
        ("Damage Taken", Metric.DamageTaken),
        ("Healing Taken", Metric.HealingTaken),
    };

    private bool Block(string label, string value, float fraction, bool selected, int index,
                       float width, float rowHeight)
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var h = rowHeight - 2f;
        var max = new Vector2(p.X + width, p.Y + h);

        dl.AddRectFilled(p, max, ImGui.GetColorU32(Track), BarRounding);

        if (fraction > 0f)
        {
            var fill = new Vector2(p.X + Math.Max(width * fraction, BarRounding * 2f), p.Y + h);
            dl.AddRectFilled(p, fill, ImGui.GetColorU32(selected ? Chosen : Plain), BarRounding);
            dl.AddRectFilled(p, fill with { Y = p.Y + (h * 0.5f) },
                             ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.13f)),
                             BarRounding, ImDrawFlags.RoundCornersTop);
        }

        var white = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.96f));
        var textY = p.Y + ((h - ImGui.GetTextLineHeight()) * 0.5f);

        Shadow(new Vector2(p.X + 6, textY), white, label);

        if (!string.IsNullOrEmpty(value))
        {
            var valueWidth = ImGui.CalcTextSize(value).X;
            Shadow(new Vector2(p.X + width - valueWidth - 6, textY), white, value);
        }

        ImGui.SetCursorScreenPos(p);
        return ImGui.InvisibleButton($"##block{index}", new Vector2(width, rowHeight));
    }

    private void DrawSheen()
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();

        var top = new Vector2(pos.X + 1, pos.Y + 1);
        var bottom = new Vector2(pos.X + size.X - 1, pos.Y + (size.Y * 0.34f));

        var bright = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.055f));
        var clear = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0f));

        dl.AddRectFilledMultiColor(top, bottom, bright, bright, clear, clear);
    }

    private const float HeaderGap = 3f;

    internal static float HeaderHeight => ImGui.GetTextLineHeight() + (HeaderGap * 2f) + 1f;

    private void DrawHeader(string left, string right, bool atBottom)
    {
        var avail = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var lineHeight = ImGui.GetTextLineHeight();
        var dl = ImGui.GetWindowDrawList();

        var textAt = atBottom ? origin with { Y = origin.Y + HeaderGap + 1f + HeaderGap } : origin;
        var ruleY = atBottom ? origin.Y + HeaderGap : origin.Y + lineHeight + HeaderGap;

        var strip = this.Settings.Background with { W = this.Settings.HeaderAlpha };

        if (strip.W > 0.02f)
        {
            var windowPos = ImGui.GetWindowPos();
            var windowSize = ImGui.GetWindowSize();

            var from = atBottom
                ? new Vector2(windowPos.X, origin.Y)
                : new Vector2(windowPos.X, windowPos.Y);

            var to = atBottom
                ? new Vector2(windowPos.X + windowSize.X, windowPos.Y + windowSize.Y)
                : new Vector2(windowPos.X + windowSize.X, origin.Y + HeaderHeight);

            dl.AddRectFilled(from, to, ImGui.GetColorU32(strip), Rounding,
                             atBottom ? ImDrawFlags.RoundCornersBottom : ImDrawFlags.RoundCornersTop);
        }

        dl.AddLine(new Vector2(origin.X, ruleY), new Vector2(origin.X + avail, ruleY),
                   ImGui.GetColorU32(Edge));

        var cog = lineHeight * IconScale;

        var cogAt = new Vector2(origin.X + avail - cog - TextInset,
                                textAt.Y + ((lineHeight - cog) * 0.5f));

        var textWidth = avail - (cog * 5f) - 16f - (TextInset * 2f) - CaptionGap;

        var rightWidth = ImGui.CalcTextSize(right).X * CaptionScale;

        Shadow(textAt with { X = origin.X + TextInset + textWidth - rightWidth },
               ImGui.GetColorU32(Dim), right, CaptionScale);

        Shadow(textAt with { X = textAt.X + TextInset }, ImGui.GetColorU32(ImGuiCol.Text),
               Fit(left, textWidth - rightWidth - CaptionGap));

        if (IconButton("##cog", cogAt, cog, FontAwesomeIcon.Cog, "Options", Dim with { W = 0.55f }))
        {

            this.options.Target = this.Settings;
            this.options.IsOpen = true;
        }

        var armed = DateTime.UtcNow < this.armedUntil;
        var eraseAt = cogAt with { X = cogAt.X - cog - 4f };
        var eraseIdle = armed ? new Vector4(0.90f, 0.35f, 0.30f, 1f) : Dim with { W = 0.55f };
        var eraseTip = armed ? "Click again to clear tonight" : "Clear tonight's encounters";

        var segmentsAt = eraseAt with { X = eraseAt.X - cog - 4f };
        var displaysAt = segmentsAt with { X = segmentsAt.X - cog - 4f };

        if (IconButton("##displays", displaysAt, cog, FontAwesomeIcon.ChartBar,
                       "What to show", Dim with { W = 0.55f }))
            this.OpenPanel(Panel.Displays);

        if (IconButton("##segments", segmentsAt, cog, FontAwesomeIcon.LayerGroup,
                       "Tonight's encounters", Dim with { W = 0.55f }))
            this.OpenPanel(Panel.Segments);

        if (IconButton("##clear", eraseAt, cog, FontAwesomeIcon.Eraser, eraseTip, eraseIdle))
        {
            if (armed)
            {
                this.Clear?.Invoke();
                this.armedUntil = DateTime.MinValue;
            }
            else
            {

                this.armedUntil = DateTime.UtcNow.AddSeconds(3);
            }
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(avail, HeaderHeight));
    }

    private void Choice(string label, Metric metric, int index, float width, float rowHeight)
    {
        if (!this.Block(label, string.Empty, 1f, this.Settings.Metric == metric, index, width, rowHeight))
            return;

        this.Settings.Metric = metric;
        this.scroll = 0;
        this.lengths.Clear();
        this.panel = Panel.None;
        this.config.Save();
    }

    private static bool IconButton(string id, Vector2 at, float size, FontAwesomeIcon icon,
                                   string tip, Vector4 idle)
    {
        ImGui.SetCursorScreenPos(at);

        var clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
        var hot = ImGui.IsItemHovered();

        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var glyph = icon.ToIconString();
            var glyphSize = ImGui.CalcTextSize(glyph);
            var colour = hot ? new Vector4(1f, 1f, 1f, 0.95f) : idle;

            ImGui.GetWindowDrawList()
                 .AddText(at + ((new Vector2(size, size) - glyphSize) * 0.5f),
                          ImGui.GetColorU32(colour), glyph);
        }

        if (hot) ImGui.SetTooltip(tip);

        return clicked;
    }

    private List<Rendered> Choose(List<Rendered> rows, int capacity)
    {
        this.visible.Clear();
        var scroll = this.scroll;
        if (rows.Count <= capacity) return rows;

        for (var i = scroll; i < rows.Count && this.visible.Count < capacity; i++)
            this.visible.Add(rows[i]);

        var window = this.visible;

        Rendered self = default;
        foreach (var candidate in rows)
            if (candidate.Row.IsSelf) { self = candidate; break; }

        if (self.Row is null) return window;

        foreach (var entry in window)
            if (entry.Row.IsSelf) return window;

        if (capacity == 1)
        {
            window.Clear();
            window.Add(self);
            return window;
        }

        if (self.Rank < window[0].Rank) window[0] = self;
        else window[^1] = self;

        return window;
    }

    private static void DrawGapRule()
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var w = ImGui.GetContentRegionAvail().X;

        var colour = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.18f));
        for (var x = p.X; x < p.X + w; x += 6f)
            dl.AddLine(new Vector2(x, p.Y + 2), new Vector2(Math.Min(x + 3f, p.X + w), p.Y + 2), colour);

        ImGui.Dummy(new Vector2(w, 5));
    }

    private void DrawRow(Rendered entry, double max, float width, float rowHeight)
    {
        var row = entry.Row;
        var total = this.Settings.Metric == Metric.Healing ? row.Healed : row.Damage;

        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var h = rowHeight - this.Settings.RowSpacing;

        var frac = max > 0 ? (float)(total / max) : 0f;
        frac = this.Advance(row.Name, frac);
        var colour = Tone(JobColours.For(row.Job, row.Name), this.config.BarBrightness);

        var barX = p.X;
        var barWidth = width;
        var barOrigin = p;
        var barEnd = new Vector2(barX + barWidth, p.Y + h);

        dl.AddRectFilled(barOrigin, barEnd, ImGui.GetColorU32(Darken(colour)), BarRounding);

        if (frac > 0f)
        {
            var barMax = new Vector2(barX + (barWidth * frac), p.Y + h);
            dl.AddRectFilled(barOrigin, barMax, ImGui.GetColorU32(colour), BarRounding);

            dl.AddRectFilled(barOrigin, barMax with { Y = p.Y + (h * 0.5f) },
                             ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.13f)),
                             BarRounding, ImDrawFlags.RoundCornersTop);
        }

        JobIcons.Draw(row.Job, p, h);

        if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(barOrigin, barEnd))
        {
            dl.AddRectFilled(barOrigin, barEnd,
                             ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.07f)), BarRounding);

            Details(row);

            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && this.shown is { } source)
                this.OpenBreakdown?.Invoke(source, row.Name);
        }

        if (row.IsSelf)
            dl.AddRect(barOrigin, barEnd, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.55f)), BarRounding,
                       ImDrawFlags.RoundCornersAll, 1.5f);

        var left = entry.Left;

        var right = entry.Right;
        var rightWidth = ImGui.CalcTextSize(right).X;
        var white = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.96f));
        var textY = p.Y + ((h - ImGui.GetTextLineHeight()) * 0.5f);

        var nameAt = new Vector2(barX + h + 4f, textY);
        Shadow(nameAt, white, left);

        if (row.Deaths > 0)
        {

            var deathColour = white;
            var x = nameAt.X + ImGui.CalcTextSize(left).X + 7f;

            var mark = "†";

            if (this.config.DeathSkull)
            {
                using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
                    mark = FontAwesomeIcon.Skull.ToIconString();
            }

            x += Big(dl, new Vector2(x, textY), h, deathColour, mark, this.config.DeathSkull) + 3f;

            if (row.Deaths > 1) Shadow(new Vector2(x, textY), deathColour, row.Deaths.ToString());
        }
        Shadow(new Vector2(p.X + width - rightWidth - 6, textY), white, right);

        ImGui.Dummy(new Vector2(width, rowHeight));
    }

    private List<MeterRow> Combine(List<MeterRow> rows)
    {
        if (!this.config.CombinePets) return rows;

        var kept = rows.Where(row => OwnerOf(row.Name) is not { } owner
                                  || rows.All(other => other.Name != owner)
                                  || !ReallyAPet(row.Name[..row.Name.LastIndexOf(" (", StringComparison.Ordinal)]))
                       .ToList();

        for (var i = 0; i < kept.Count; i++)
        {
            var owner = kept[i];

            foreach (var pet in rows)
            {
                if (OwnerOf(pet.Name) != owner.Name) continue;
                if (!ReallyAPet(pet.Name[..pet.Name.LastIndexOf(" (", StringComparison.Ordinal)])) continue;

                owner = owner with
                {
                    Damage = owner.Damage + pet.Damage,
                    Healed = owner.Healed + pet.Healed,
                    Deaths = owner.Deaths + pet.Deaths,
                    Dps = owner.Dps + pet.Dps,
                    Hps = owner.Hps + pet.Hps,
                    DamagePct = owner.DamagePct + pet.DamagePct,
                    HealedPct = owner.HealedPct + pet.HealedPct,
                };
            }

            kept[i] = owner;
        }

        return kept;
    }

    private static readonly Dictionary<string, bool> PetCache = new(StringComparer.Ordinal);

    private static bool ReallyAPet(string name)
    {
        if (PetCache.TryGetValue(name, out var known)) return known;

        var pet = false;

        foreach (var obj in Plugin.Objects)
        {
            if (obj is not IBattleNpc npc) continue;
            if (!string.Equals(npc.Name.TextValue, name, StringComparison.Ordinal)) continue;

            pet = npc.BattleNpcKind == BattleNpcSubKind.Pet;
            break;
        }

        PetCache[name] = pet;
        return pet;
    }

    private static string? OwnerOf(string name)
    {
        if (!name.EndsWith(')')) return null;

        var open = name.LastIndexOf(" (", StringComparison.Ordinal);
        if (open <= 0) return null;

        var owner = name[(open + 2)..^1];
        return string.IsNullOrWhiteSpace(owner) ? null : owner;
    }

    private static bool IsLimitBreak(string name)
        => name.Equals("Limit Break", StringComparison.OrdinalIgnoreCase);

    private static Vector4 Tone(Vector4 colour, float ceiling)
    {
        var luminance = (0.2126f * colour.X) + (0.7152f * colour.Y) + (0.0722f * colour.Z);

        if (luminance <= ceiling || luminance <= 0f) return colour;

        var scale = ceiling / luminance;
        return new Vector4(colour.X * scale, colour.Y * scale, colour.Z * scale, colour.W);
    }

    private float Advance(string name, float target)
    {
        if (!this.Settings.SmoothBars)
        {
            this.lengths[name] = target;
            return target;
        }

        var current = this.lengths.TryGetValue(name, out var held) ? held : 0f;
        var step = 1f - MathF.Exp(-this.Ease * ImGui.GetIO().DeltaTime);

        current += (target - current) * step;
        if (MathF.Abs(target - current) < 0.0015f) current = target;

        this.lengths[name] = current;
        return current;
    }

    private static Vector4 Darken(Vector4 colour)
        => new(colour.X * 0.30f, colour.Y * 0.30f, colour.Z * 0.30f, 0.60f);

    private static float Big(ImDrawListPtr dl, Vector2 at, float rowHeight, uint colour,
                             string mark, bool iconFont)
    {
        var shade = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.7f));
        var font = iconFont ? Plugin.PluginInterface.UiBuilder.IconFontHandle.Push() : null;

        try
        {
            var size = ImGui.GetFontSize() * DeathScale;
            var width = ImGui.CalcTextSize(mark).X * DeathScale;

            var y = at.Y + ((ImGui.GetTextLineHeight() - size) * 0.5f);

            dl.AddText(ImGui.GetFont(), size, new Vector2(at.X + 1, y + 1), shade, mark);
            dl.AddText(ImGui.GetFont(), size, new Vector2(at.X, y), colour, mark);

            return width;
        }
        finally
        {
            font?.Dispose();
        }
    }

    private void Details(MeterRow row)
    {
        var job = string.IsNullOrEmpty(row.Job) ? "" : row.Job.ToUpperInvariant();

        var colour = Tone(JobColours.For(row.Job, row.Name), this.config.BarBrightness);

        var line = ImGui.GetTextLineHeight();
        const float pad = 10f;
        const float gutter = 18f;
        const float barH = 7f;
        const float minBody = 268f;

        var big = Plugin.Typeface.Name;
        var crisp = big is { Available: true };

        float nameW, nameH;

        if (crisp)
        {
            using (big!.Push())
            {
                nameW = ImGui.CalcTextSize(row.Name).X;
                nameH = ImGui.GetTextLineHeight();
            }
        }
        else
        {
            nameW = ImGui.CalcTextSize(row.Name).X * NameScale;
            nameH = line * NameScale;
        }

        var barW = MathF.Max(minBody, nameW + gutter + (ImGui.CalcTextSize(job).X * CaptionScale));

        (string Label, string Value)[] left =
        {
            ("Crit", $"{row.CritPct:N1}%"),
            ("Taken", Format.Short(row.DamageTaken)),
        };

        (string Label, string Value)[] right =
        {
            ("Direct hit", $"{row.DirectHitPct:N1}%"),
            ("Deaths", row.Deaths.ToString()),
        };

        var barTop = nameH + (pad * 1.2f);

        var caption = line * CaptionScale;
        var block = line + 3f + barH + 2f + caption + (pad * 0.7f);

        var width = barW + (pad * 2f);
        var height = barTop + (pad * 0.8f) + (block * 2f) + (left.Length * line) + (pad * 0.9f)
                   + (string.IsNullOrEmpty(row.MaxHit) ? 0f : caption + 5f + (line * ArtScale) + (pad * 0.9f));

        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Rounding);

        ImGui.BeginTooltip();

        var at = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(width, height));

        var dl = ImGui.GetWindowDrawList();
        var end = at + new Vector2(width, height);

        dl.AddRectFilled(at, end, ImGui.GetColorU32(Frost), Rounding);

        dl.AddRectFilled(at, new Vector2(end.X, at.Y + barTop), ImGui.GetColorU32(colour), Rounding,
                         ImDrawFlags.RoundCornersTop);

        dl.AddRectFilledMultiColor(at, new Vector2(end.X, at.Y + (barTop * 0.6f)),
                                   ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.10f)),
                                   ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.10f)),
                                   ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0f)),
                                   ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0f)));

        dl.AddLine(new Vector2(at.X, at.Y + barTop), new Vector2(end.X, at.Y + barTop),
                   ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.25f)));

        dl.AddRect(at, end, ImGui.GetColorU32(Edge), Rounding);

        var lum = (colour.X * 0.299f) + (colour.Y * 0.587f) + (colour.Z * 0.114f);
        var ink = lum > 0.70f ? new Vector4(0.06f, 0.06f, 0.08f, 1f) : new Vector4(1f, 1f, 1f, 1f);

        var nameAt = new Vector2(at.X + pad, at.Y + ((barTop - nameH) * 0.5f));

        if (crisp)
        {
            using (big!.Push()) Shadow(nameAt, ImGui.GetColorU32(ink), row.Name);
        }
        else
        {
            Shadow(nameAt, ImGui.GetColorU32(ink), row.Name, NameScale);
        }

        if (job.Length > 0)
        {
            var jw = ImGui.CalcTextSize(job).X * CaptionScale;
            Shadow(new Vector2(end.X - pad - jw, at.Y + ((barTop - (line * CaptionScale)) * 0.5f)),
                   ImGui.GetColorU32(ink with { W = 0.75f }), job, CaptionScale);
        }

        var dim = ImGui.GetColorU32(Dim);
        var text = ImGui.GetColorU32(ImGuiCol.Text);
        var y = at.Y + barTop + (pad * 0.8f);

        var lb = this.shown?.Rows.Find(r => IsLimitBreak(r.Name));
        var counts = this.Settings.CountLimitBreak && lb is not null;

        var lbDamage = counts ? lb!.Damage : 0d;
        var lbHealing = counts ? lb!.Healed : 0d;

        double totalDamage = 0, totalHealed = 0;

        foreach (var r in this.view) { totalDamage += r.Row.Damage; totalHealed += r.Row.Healed; }

        totalDamage += lbDamage;
        totalHealed += lbHealing;

        var damageShare = totalDamage > 0 ? row.Damage / totalDamage * 100 : 0;
        var healShare = totalHealed > 0 ? row.Healed / totalHealed * 100 : 0;

        Shadow(new Vector2(at.X + pad, y), dim, "Damage");
        Right(at.X + pad + barW, y, $"{Format.Short(row.Damage)}   {Format.Short(row.Dps)} dps", text);

        var dmgAt = new Vector2(at.X + pad, y + line + 3f);
        Rail(dl, dmgAt, barW, barH);
        this.Shares(dl, dmgAt, barW, barW, barH, row, r => r.Damage, totalDamage, lbDamage);

        Centred(at.X + pad, dmgAt.Y + barH + 2f, barW, $"{damageShare:N1}% of damage done", dim);

        y += block;

        Shadow(new Vector2(at.X + pad, y), dim, "Healing");
        Right(at.X + pad + barW, y, $"{Format.Short(row.Healed)}   {Format.Short(row.Hps)} hps", text);

        var healAt = new Vector2(at.X + pad, y + line + 3f);
        Rail(dl, healAt, barW, barH);

        var share = (float)healShare;
        var wasted = MathF.Min(Frac(row.OverHealPct), 0.99f);
        var waste = share * wasted / (1f - wasted);
        var axis = 100f + waste;

        var over = barW * waste / axis;

        this.Shares(dl, healAt, barW * 100f / axis, barW, barH, row, r => r.Healed, totalHealed, lbHealing);

        if (over > 0.5f)
        {
            var line0 = healAt.X + barW - over;

            var wastedColour = new Vector4(colour.X * 0.42f, colour.Y * 0.42f, colour.Z * 0.42f, 1f);

            Lit(dl, healAt with { X = line0 }, over, barH, wastedColour);

            dl.AddLine(new Vector2(line0, healAt.Y - 1f), new Vector2(line0, healAt.Y + barH + 1f),
                       ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.85f)));
        }

        var healSays = row.Healed <= 0
            ? "no healing done"
            : row.OverHealPct >= 1
                ? $"{healShare:N1}% of healing done  ·  {row.OverHealPct:N0}% overheal"
                : $"{healShare:N1}% of healing done";

        Centred(at.X + pad, healAt.Y + barH + 2f, barW, healSays, dim);

        y += block;

        var cell = (barW - pad) / 2f;

        for (var i = 0; i < left.Length; i++)
        {
            Cell(at.X + pad, y, cell, left[i].Label, left[i].Value, dim, text);
            Cell(at.X + pad + cell + pad, y, cell, right[i].Label, right[i].Value, dim, text);

            y += line;
        }

        if (!string.IsNullOrEmpty(row.MaxHit))
        {
            var (hit, amount) = Abilities.Split(row.MaxHit);

            Shadow(new Vector2(at.X + pad, y + (pad * 0.55f)), dim, "Biggest hit", CaptionScale);

            var hitY = y + (pad * 0.55f) + caption + 5f;
            var art = line * ArtScale;
            var x = at.X + pad;

            var drawn = Abilities.Icon(dl, new Vector2(x, hitY), art, hit);
            if (drawn > 0f) x += drawn + 7f;

            var textY = hitY + ((art - line) * 0.5f);

            Shadow(new Vector2(x, textY), ImGui.GetColorU32(ImGuiCol.Text), hit);

            if (amount.Length > 0) Right(at.X + pad + barW, textY, amount, text);
        }

        ImGui.EndTooltip();

        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor();
    }

    private void Shares(ImDrawListPtr dl, Vector2 at, float region, float barWidth, float height,
                        MeterRow self, Func<MeterRow, double> value, double total, double limitBreak)
    {
        if (total <= 0) return;

        var x = at.X;
        var last = this.view.Count - 1;

        for (var i = 0; i <= last; i++)
        {
            var r = this.view[i].Row;
            var w = region * (float)(Math.Max(0, value(r)) / total);

            if (w < 0.4f) continue;

            var mine = ReferenceEquals(r, self) || r.Name == self.Name;
            var c = Tone(JobColours.For(r.Job, r.Name), this.config.BarBrightness);

            var flags = i == 0 ? ImDrawFlags.RoundCornersLeft : ImDrawFlags.RoundCornersNone;

            dl.AddRectFilled(new Vector2(x, at.Y), new Vector2(x + w, at.Y + height),
                             ImGui.GetColorU32(mine ? c : c with { W = 0.26f }),
                             height * 0.5f, flags);

            if (mine)
                dl.AddLine(new Vector2(x + 1f, at.Y + 0.5f), new Vector2(x + w - 1f, at.Y + 0.5f),
                           ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.30f)));

            x += w;
        }

        if (limitBreak <= 0) return;

        var lbW = region * (float)(limitBreak / total);
        var lbX = at.X + region - lbW;

        var corners = region < barWidth - 0.5f ? ImDrawFlags.RoundCornersNone : ImDrawFlags.RoundCornersRight;

        dl.AddRectFilled(new Vector2(lbX, at.Y), new Vector2(lbX + lbW, at.Y + height),
                         ImGui.GetColorU32(new Vector4(0.42f, 0.78f, 0.82f, 0.55f)),
                         height * 0.5f, corners);

        dl.AddLine(new Vector2(lbX, at.Y - 1f), new Vector2(lbX, at.Y + height + 1f),
                   ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.85f)));
    }

    private static float Frac(double percent) => Math.Clamp((float)percent / 100f, 0f, 1f);

    private static void Rail(ImDrawListPtr dl, Vector2 at, float width, float height)
        => dl.AddRectFilled(at, at + new Vector2(width, height), ImGui.GetColorU32(Track), height * 0.5f);

    private static void Lit(ImDrawListPtr dl, Vector2 at, float width, float height, Vector4 colour)
    {
        if (width < 1f) return;

        dl.AddRectFilled(at, at + new Vector2(width, height), ImGui.GetColorU32(colour), height * 0.5f);

        dl.AddLine(at + new Vector2(1f, 0.5f), at + new Vector2(width - 1f, 0.5f),
                   ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.30f)));
    }

    private static void Centred(float x, float y, float width, string text, uint colour)
        => Shadow(new Vector2(x + ((width - (ImGui.CalcTextSize(text).X * CaptionScale)) * 0.5f), y),
                  colour, text, CaptionScale);

    private static void Right(float rightEdge, float y, string text, uint colour)
        => Shadow(new Vector2(rightEdge - ImGui.CalcTextSize(text).X, y), colour, text);

    private static void Cell(float x, float y, float width, string label, string value, uint dim, uint ink)
    {
        Shadow(new Vector2(x, y), dim, label);
        Shadow(new Vector2(x + width - ImGui.CalcTextSize(value).X, y), ink, value);
    }

    private static void Shadow(Vector2 pos, uint colour, string text, float scale)
    {
        var dl = ImGui.GetWindowDrawList();
        var font = ImGui.GetFont();
        var size = ImGui.GetFontSize() * scale;
        var line = ImGui.GetTextLineHeight();
        var at = pos with { Y = pos.Y + ((line - (line * scale)) * 0.5f) };

        dl.AddText(font, size, at + new Vector2(1, 1),
                   ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.65f)), text);

        dl.AddText(font, size, at, colour, text);
    }

    private static string Fit(string text, float maxWidth)
    {
        if (maxWidth <= 0) return string.Empty;
        if (ImGui.CalcTextSize(text).X <= maxWidth) return text;

        const string tail = "...";
        var room = maxWidth - ImGui.CalcTextSize(tail).X;
        if (room <= 0) return string.Empty;

        int lo = 0, hi = text.Length;

        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;

            if (ImGui.CalcTextSize(text[..mid]).X <= room) lo = mid;
            else hi = mid - 1;
        }

        return lo == 0 ? string.Empty : text[..lo].TrimEnd() + tail;
    }

    private static void Shadow(Vector2 pos, uint colour, string text)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddText(pos + new Vector2(1, 1), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.65f)), text);
        dl.AddText(pos, colour, text);
    }
}
