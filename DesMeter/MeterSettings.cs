using System;
using System.Numerics;

namespace DesMeter;

internal enum Metric
{
    Damage,
    Healing,

    DamageTaken,

    HealingTaken,
}

[Serializable]
internal sealed class MeterSettings
{

    public int Id { get; set; }

    public Metric Metric { get; set; }

    public float FontScale { get; set; } = 0.9f;

    public float RowHeight { get; set; }

    public float RowSpacing { get; set; } = 1f;

    public float HeaderClearance { get; set; } = 2f;

    public Vector4 Background { get; set; } = new(0.05f, 0.06f, 0.09f, 0.78f);

    public float HeaderAlpha { get; set; } = 0.78f;

    public bool GrowUpward { get; set; }

    public bool Locked { get; set; }

    public bool SmoothBars { get; set; } = true;

    public bool ShowLimitBreak { get; set; }

    public bool CountLimitBreak { get; set; } = true;

    public bool PvpWholeMatch { get; set; } = true;

    public bool Collapsed { get; set; }

    public float ExpandedHeight { get; set; }

    internal MeterSettings Copy(int id) => new()
    {
        Id = id,
        Metric = this.Metric,
        FontScale = this.FontScale,
        RowHeight = this.RowHeight,
        RowSpacing = this.RowSpacing,
        HeaderClearance = this.HeaderClearance,
        Background = this.Background,
        HeaderAlpha = this.HeaderAlpha,
        GrowUpward = this.GrowUpward,
        SmoothBars = this.SmoothBars,
        ShowLimitBreak = this.ShowLimitBreak,
        CountLimitBreak = this.CountLimitBreak,
        PvpWholeMatch = this.PvpWholeMatch,

        Locked = false,
    };
}
