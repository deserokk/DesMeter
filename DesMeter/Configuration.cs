using System;
using Dalamud.Game.ClientState.Keys;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;

namespace DesMeter;

[Serializable]
internal sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public List<MeterSettings> Windows { get; set; } = new();

    public VirtualKey ToggleKey { get; set; } = VirtualKey.NO_KEY;

    public bool ToggleCtrl { get; set; }

    public bool ToggleAlt { get; set; }

    public bool ToggleShift { get; set; }

    public const int MaxWindows = 5;

    public int NextWindowId { get; set; } = 1;

    internal void Migrate()
    {
        if (this.Windows.Count > 0) return;

        this.Windows.Add(new MeterSettings
        {
            Id = this.NextWindowId++,
            FontScale = this.LegacyFontScale,
            RowHeight = this.LegacyRowHeight,
            RowSpacing = this.LegacyRowSpacing,
            HeaderClearance = this.LegacyHeaderClearance,
            Background = this.LegacyBackground,
            HeaderAlpha = this.LegacyHeaderAlpha,
            GrowUpward = this.LegacyGrowUpward,
            Locked = this.LegacyLocked,
            SmoothBars = this.LegacySmoothBars,
            ShowLimitBreak = this.LegacyShowLimitBreak,
            CountLimitBreak = this.LegacyCountLimitBreak,
            PvpWholeMatch = this.LegacyPvpWholeMatch,
        });
    }

    public float LegacyFontScale { get; set; } = 0.9f;
    public float LegacyRowHeight { get; set; }
    public float LegacyRowSpacing { get; set; } = 1f;
    public float LegacyHeaderClearance { get; set; } = 2f;
    public Vector4 LegacyBackground { get; set; } = new(0.05f, 0.06f, 0.09f, 0.78f);
    public float LegacyHeaderAlpha { get; set; } = 0.78f;
    public bool LegacyGrowUpward { get; set; }
    public bool LegacyLocked { get; set; }
    public bool LegacySmoothBars { get; set; } = true;
    public bool LegacyShowLimitBreak { get; set; }
    public bool LegacyCountLimitBreak { get; set; } = true;
    public bool LegacyPvpWholeMatch { get; set; } = true;

    public float BarBrightness = 0.62f;

    public bool DeathSkull;

    public bool CombinePets = true;

    public bool LockNeedsAlt = true;

    internal void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
