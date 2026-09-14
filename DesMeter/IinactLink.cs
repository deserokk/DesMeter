using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Dalamud.Plugin.Ipc;
using Newtonsoft.Json.Linq;

namespace DesMeter;

internal sealed record MeterRow(
    string Name, string Job,
    double Dps, double Damage, double DamagePct,
    double Hps, double Healed, double HealedPct,
    int Deaths, bool IsSelf)
{

    public double CritPct { get; init; }

    public double DirectHitPct { get; init; }

    public double OverHealPct { get; init; }

    public double DamageTaken { get; init; }

    public double HealsTaken { get; init; }

    public string MaxHit { get; init; } = string.Empty;

    public int Team { get; init; } = -1;
}

internal sealed class Snapshot
{
    public string Title = string.Empty;
    public string Zone = string.Empty;

    public int TeamMode;
    public string Duration = string.Empty;

    public int DurationSeconds;

    public DateTime CapturedAt = DateTime.Now;

    public DateTime StartedAt = DateTime.Now;

    public string LogFile = string.Empty;

    public long LogOffset;
    public double RaidDps;
    public double RaidHps;
    public double MaxDamage;
    public double MaxHealed;

    public double TotalDamage;

    public List<MeterRow> Rows = new();
}

internal sealed class IinactLink : IDisposable
{

    private const string Channel = "DesMeter";

    private readonly ICallGateProvider<JObject, bool> receiver;
    private ICallGateSubscriber<JObject, bool>? sender;

    internal volatile Snapshot? Current;

    internal bool Stalled { get; private set; }

    internal volatile float Cadence = 1f;

    private DateTime lastPayload = DateTime.MinValue;

    internal bool Connected { get; private set; }
    internal string? Trouble { get; private set; }

    internal volatile string LocalName = string.Empty;

    internal volatile string FirstTarget = string.Empty;

    private double lastTotal;

    private DateTime? encounterStart;
    private string anchorFile = string.Empty;
    private long anchorOffset;

    private double mutedAbove = -1;

    private bool dumpedProbe;
    private DateTime nextAttempt = DateTime.MinValue;

    internal readonly History History = new();

    internal IinactLink()
    {

        this.receiver = Plugin.PluginInterface.GetIpcProvider<JObject, bool>(Channel);
        this.receiver.RegisterFunc(this.Receive);
    }

    internal void TryConnect()
    {
        if (this.Connected || DateTime.UtcNow < this.nextAttempt) return;
        this.nextAttempt = DateTime.UtcNow.AddSeconds(3);

        try
        {

            var ipcVersion = Plugin.PluginInterface
                                   .GetIpcSubscriber<Version>("IINACT.IpcVersion").InvokeFunc();

            if (ipcVersion.Major != 2)
            {
                this.Trouble = $"IINACT speaks IPC {ipcVersion}, this was built for 2.x.";
                return;
            }

            var created = Plugin.PluginInterface
                                .GetIpcSubscriber<string, bool>("IINACT.CreateSubscriber")
                                .InvokeFunc(Channel);

            if (!created)
            {

                this.Trouble = "IINACT refused the subscription. Reload it, then reload this.";
                return;
            }

            this.sender = Plugin.PluginInterface
                                .GetIpcSubscriber<JObject, bool>($"IINACT.IpcProvider.{Channel}");

            this.sender.InvokeAction(new JObject
            {
                ["call"] = "subscribe",
                ["events"] = new JArray("CombatData"),
            });

            this.Connected = true;
            this.Trouble = null;
            Plugin.Log.Information($"Attached to IINACT, IPC {ipcVersion}.");
            Probe.Line($"attached, IPC {ipcVersion}, subscribed CombatData + LogLine");
        }
        catch (Exception ex)
        {

            this.Trouble = "IINACT is not running.";
            Plugin.Log.Debug($"IINACT not reachable yet: {ex.Message}");
        }
    }

    private bool Receive(JObject data)
    {
        try
        {
            if (data["type"]?.ToString() != "CombatData") return true;

            this.DumpProbeOnce(data);

            var incoming = Num((JObject?)data["Encounter"] ?? new JObject(), "damage");

            if (incoming < this.lastTotal)
            {
                this.FirstTarget = string.Empty;
                this.encounterStart = null;
            }

            this.lastTotal = incoming;

            if (this.encounterStart is null && incoming > 0)
            {
                this.encounterStart = DateTime.Now;
                (this.anchorFile, this.anchorOffset) = LogAnchor.Current();
            }

            var arrived = DateTime.UtcNow;

            if (this.lastPayload != DateTime.MinValue)
            {

                var gap = (float)(arrived - this.lastPayload).TotalSeconds;
                if (gap is > 0.2f and < 5f) this.Cadence = (this.Cadence * 0.7f) + (gap * 0.3f);
            }

            this.lastPayload = arrived;

            var snapshot = Build(data, this.LocalName, this.FirstTarget);

            snapshot.StartedAt = this.encounterStart ?? snapshot.CapturedAt;
            snapshot.LogFile = this.anchorFile;
            snapshot.LogOffset = this.anchorOffset;

            if (this.mutedAbove >= 0)
            {

                if (snapshot.TotalDamage >= this.mutedAbove) return true;

                this.mutedAbove = -1;
            }

            this.History.Observe(snapshot);
            this.Current = snapshot;
        }
        catch (Exception ex)
        {

            Plugin.Log.Error($"Bad CombatData: {ex.Message}");
        }

        return true;
    }

    internal void Watch(bool inCombat, DateTime lastEnemyHit)
    {
        var now = DateTime.UtcNow;
        var total = this.Current?.TotalDamage ?? 0;

        if (Math.Abs(total - this.lastAdvanceTotal) > 0.5)
        {
            this.lastAdvanceTotal = total;
            this.lastAdvance = now;
        }

        if (!inCombat)
        {
            this.damageSince = null;
            this.Stalled = false;
            return;
        }

        if (now - lastEnemyHit > Recent)
            this.damageSince = null;
        else
            this.damageSince ??= lastEnemyHit;

        this.Stalled = this.damageSince is { } since
                    && now - since > Patience
                    && now - this.lastAdvance > Patience;
    }

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan Recent = TimeSpan.FromSeconds(4);

    private DateTime? damageSince;
    private DateTime lastAdvance = DateTime.UtcNow;
    private double lastAdvanceTotal;

    internal void MuteCurrent()
    {
        var running = this.Current?.TotalDamage ?? 0;

        this.mutedAbove = running > 0 ? running : -1;
        this.Current = null;
    }

    private void DumpProbeOnce(JObject data)
    {
        if (this.dumpedProbe) return;
        this.dumpedProbe = true;

        try
        {
            var dir = Plugin.PluginInterface.ConfigDirectory;
            dir.Create();
            File.WriteAllText(Path.Combine(dir.FullName, "first-combatdata.json"), data.ToString());
            Plugin.Log.Information($"Wrote first CombatData to {dir.FullName}");
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"Could not write the probe file: {ex.Message}");
        }
    }

    private static Snapshot Build(JObject data, string localName, string firstTarget)
    {
        var encounter = data["Encounter"] as JObject;
        var combatants = data["Combatant"] as JObject;

        var rows = new List<MeterRow>();

        if (combatants != null)
        {
            foreach (var (name, token) in combatants)
            {
                if (token is not JObject c) continue;

                var isSelf = name.Equals("YOU", StringComparison.OrdinalIgnoreCase)
                          || (localName.Length > 0 && name.Equals(localName, StringComparison.OrdinalIgnoreCase));

                rows.Add(new MeterRow(
                    name,
                    c["Job"]?.ToString().ToUpperInvariant() ?? string.Empty,
                    Num(c, "encdps"),
                    Num(c, "damage"),
                    Num(c, "damage%"),

                    Num(c, "enchps"),
                    Num(c, "healed"),
                    Num(c, "healed%"),
                    (int)Num(c, "deaths"),
                    isSelf)
                {
                    CritPct = Num(c, "crithit%"),
                    DirectHitPct = Num(c, "DirectHitPct"),
                    OverHealPct = Num(c, "OverHealPct"),
                    DamageTaken = Num(c, "damagetaken"),
                    HealsTaken = Num(c, "healstaken"),
                    MaxHit = c["maxhit"]?.ToString() ?? string.Empty,
                });
            }
        }

        return new Snapshot
        {
            Title = Name(encounter?["title"]?.ToString(), firstTarget),
            Zone = encounter?["CurrentZoneName"]?.ToString() ?? string.Empty,
            Duration = encounter?["duration"]?.ToString() ?? string.Empty,
            DurationSeconds = encounter != null ? (int)Num(encounter, "DURATION") : 0,
            RaidDps = encounter != null ? Num(encounter, "encdps") : 0,
            TotalDamage = encounter != null ? Num(encounter, "damage") : 0,
            MaxDamage = rows.Count > 0 ? rows.Max(r => r.Damage) : 0,
            MaxHealed = rows.Count > 0 ? rows.Max(r => r.Healed) : 0,
            RaidHps = encounter != null ? Num(encounter, "enchps") : 0,
            Rows = rows,
        };
    }

    private static string Name(string? title, string firstTarget)
        => string.IsNullOrEmpty(title) || title == "Encounter"
            ? firstTarget
            : title;

    private static double Num(JObject o, string key)
    {
        var s = o[key]?.ToString();
        if (string.IsNullOrEmpty(s)) return 0;

        s = s.Replace(",", string.Empty).Replace("%", string.Empty).Trim();
        if (!double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return 0;

        return double.IsFinite(v) ? v : 0;
    }

    public void Dispose()
    {
        try
        {
            if (this.Connected)
                Plugin.PluginInterface.GetIpcSubscriber<string, bool>("IINACT.Unsubscribe")
                      .InvokeFunc(Channel);
        }
        catch (Exception ex)
        {

            Plugin.Log.Debug($"Unsubscribe went nowhere: {ex.Message}");
        }

        this.receiver.UnregisterFunc();
    }
}
