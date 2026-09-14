using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace DesMeter;

internal sealed class History
{

    private const int Keep = 200;

    private readonly object gate = new();
    private readonly List<Snapshot> segments = new();

    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(60);

    private static readonly TimeSpan PulseEvery = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan Backstop = TimeSpan.FromMinutes(-5);

    private int overallFrom;

    private Snapshot? live;

    private Snapshot? overallCache;
    private Snapshot? overallFrom_;
    private int overallCount;

    private string? file;
    private string? beatFile;
    private DateTime nextBeat = DateTime.MinValue;

    internal void Open()
    {
        try
        {
            var dir = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "history");
            Directory.CreateDirectory(dir);
            this.file = Path.Combine(dir, "session.jsonl");
            this.beatFile = Path.Combine(dir, "heartbeat");

            var resuming = this.LastSeen() is { } gap && gap <= Grace && gap > Backstop;
            this.Beat();

            if (!resuming)
            {
                File.WriteAllText(this.file, string.Empty);
                Plugin.Log.Information("New session, history cleared.");
                return;
            }

            if (!File.Exists(this.file)) return;

            foreach (var line in File.ReadAllLines(this.file))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    if (JsonConvert.DeserializeObject<Snapshot>(line) is { } segment)
                        this.segments.Add(segment);
                }
                catch (JsonException)
                {
                }
            }

            Plugin.Log.Information($"Resumed session: {this.segments.Count} encounters.");
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"History unavailable: {ex.Message}");
        }
    }

    private TimeSpan? LastSeen()
    {
        if (this.beatFile is null || !File.Exists(this.beatFile)) return null;

        var text = File.ReadAllText(this.beatFile).Trim();

        return DateTime.TryParse(text, CultureInfo.InvariantCulture,
                                 DateTimeStyles.RoundtripKind, out var last)
            ? DateTime.UtcNow - last
            : null;
    }

    private void Beat()
    {
        if (this.beatFile is null) return;

        try
        {
            File.WriteAllText(this.beatFile, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"Heartbeat not written: {ex.Message}");
        }
    }

    internal void Pulse()
    {
        if (DateTime.UtcNow < this.nextBeat) return;

        this.nextBeat = DateTime.UtcNow.Add(PulseEvery);
        this.Beat();
    }

    internal void Observe(Snapshot next)
    {
        lock (this.gate)
        {
            if (this.Pvp)
            {
                this.ObservePvp(next);
                return;
            }

            if (this.live is { } previous && Ended(previous, next)) this.Archive(previous);
            this.live = next;
        }
    }

    internal volatile bool Pvp;

    internal volatile string PvpTitle = "PvP";

    private void ObservePvp(Snapshot next)
    {

        if (next.DurationSeconds < this.pvpLastSeconds / 2)
            foreach (var t in this.pvpTally.Values) t.Reset();

        this.pvpLastSeconds = next.DurationSeconds;

        var baseline = !this.pvpPrimed;
        this.pvpPrimed = true;

        var gained = false;

        foreach (var row in next.Rows)
        {
            if (!this.pvpTally.TryGetValue(row.Name, out var tally))
                this.pvpTally[row.Name] = tally = new Tally();

            if (baseline) tally.Baseline(row);
            else gained |= tally.Take(row);
        }

        if (gained) this.pvpStarted ??= next.CapturedAt.AddSeconds(-Math.Max(0, next.DurationSeconds));

        this.live = this.pvpStarted is null ? null : this.StickySnapshot(next);
    }

    private readonly Dictionary<string, Tally> pvpTally = new(StringComparer.Ordinal);
    private int pvpLastSeconds;
    private DateTime? pvpStarted;

    private bool pvpPrimed = true;

    private sealed class Tally
    {
        internal MeterRow Latest = null!;
        internal double Damage, Healed, Taken, HealsTaken;
        internal int Deaths;

        internal bool Counted;

        private double lastDamage, lastHealed, lastTaken, lastHealsTaken;
        private int lastDeaths;

        internal void Baseline(MeterRow row)
        {
            this.Latest = row;
            this.lastDamage = row.Damage;
            this.lastHealed = row.Healed;
            this.lastTaken = row.DamageTaken;
            this.lastHealsTaken = row.HealsTaken;
            this.lastDeaths = row.Deaths;
        }

        internal bool Take(MeterRow row)
        {
            this.Latest = row;

            var before = this.Damage + this.Healed + this.Taken + this.HealsTaken + this.Deaths;

            this.Damage += Gain(ref this.lastDamage, row.Damage);
            this.Healed += Gain(ref this.lastHealed, row.Healed);
            this.Taken += Gain(ref this.lastTaken, row.DamageTaken);
            this.HealsTaken += Gain(ref this.lastHealsTaken, row.HealsTaken);

            var d = (double)this.lastDeaths;
            this.Deaths += (int)Gain(ref d, row.Deaths);
            this.lastDeaths = (int)d;

            var gained = this.Damage + this.Healed + this.Taken + this.HealsTaken + this.Deaths > before;
            this.Counted |= gained;
            return gained;
        }

        private static double Gain(ref double last, double now)
        {
            var gain = now > last ? now - last : 0;
            last = now;
            return gain;
        }

        internal void Reset()
        {
            this.lastDamage = this.lastHealed = this.lastTaken = this.lastHealsTaken = 0;
            this.lastDeaths = 0;
        }
    }

    private Snapshot StickySnapshot(Snapshot next)
    {
        var seconds = Math.Max(1, (int)(next.CapturedAt - (this.pvpStarted ?? next.StartedAt)).TotalSeconds);
        var tallies = this.pvpTally.Values.Where(t => t.Counted).ToList();

        var damage = tallies.Sum(t => t.Damage);
        var healed = tallies.Sum(t => t.Healed);

        var mode = Teams.Mode;

        var rows = tallies.Select(t => t.Latest with
        {
            Team = mode == 0 ? -1 : Teams.Of(t.Latest.Name),
            Damage = t.Damage,
            Healed = t.Healed,
            Deaths = t.Deaths,
            DamageTaken = t.Taken,
            HealsTaken = t.HealsTaken,
            Dps = t.Damage / seconds,
            Hps = t.Healed / seconds,
            DamagePct = damage > 0 ? t.Damage / damage * 100 : 0,
            HealedPct = healed > 0 ? t.Healed / healed * 100 : 0,
        }).ToList();

        var span = TimeSpan.FromSeconds(seconds);

        return new Snapshot
        {
            Title = this.PvpTitle,
            TeamMode = mode,
            Zone = next.Zone,
            Duration = $"{(int)span.TotalMinutes:00}:{span.Seconds:00}",
            DurationSeconds = seconds,
            CapturedAt = next.CapturedAt,
            StartedAt = this.pvpStarted ?? next.StartedAt,
            LogFile = next.LogFile,
            LogOffset = next.LogOffset,
            TotalDamage = damage,
            RaidDps = damage / seconds,
            RaidHps = healed / seconds,
            MaxDamage = rows.Count > 0 ? rows.Max(r => r.Damage) : 0,
            MaxHealed = rows.Count > 0 ? rows.Max(r => r.Healed) : 0,
            Rows = rows,
        };
    }

    private static bool Ended(Snapshot previous, Snapshot next)
        => next.TotalDamage < previous.TotalDamage;

    private void Archive(Snapshot done)
    {

        if (done.DurationSeconds <= 0 || done.Rows.Count == 0) return;

        if (this.AlreadyFiled(done)) return;

        this.segments.Add(done);
        if (this.segments.Count > Keep) this.segments.RemoveAt(0);

        Probe.Line($"archived \"{done.Title}\" {done.Duration} ({done.DurationSeconds}s) "
                 + $"damage={done.TotalDamage:N0} rows={done.Rows.Count} zone={done.Zone}");

        if (this.file is null) return;

        try
        {
            File.AppendAllText(this.file, JsonConvert.SerializeObject(done) + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"Could not write encounter to history: {ex.Message}");
        }
    }

    private bool AlreadyFiled(Snapshot candidate)
    {
        for (var i = this.segments.Count - 1; i >= 0 && i >= this.segments.Count - 5; i--)
        {
            var seen = this.segments[i];

            if (seen.DurationSeconds == candidate.DurationSeconds
             && string.Equals(seen.Title, candidate.Title, StringComparison.Ordinal)
             && Math.Abs(seen.TotalDamage - candidate.TotalDamage) < 0.5)
                return true;
        }

        return false;
    }

    internal void TerritoryChanged()
    {
        lock (this.gate)
        {
            if (this.live is { } last) this.Archive(last);
            this.live = null;
            this.overallFrom = this.segments.Count;

            this.pvpTally.Clear();
            this.pvpLastSeconds = 0;
            this.pvpStarted = null;
            this.pvpPrimed = false;
        }
    }

    internal Snapshot? Overall()
    {
        lock (this.gate)
        {

            if (this.overallCache is not null
                && ReferenceEquals(this.overallFrom_, this.live)
                && this.overallCount == this.segments.Count)
                return this.overallCache;

            var scope = new List<Snapshot>();

            for (var i = this.overallFrom; i < this.segments.Count; i++) scope.Add(this.segments[i]);

            if (this.live is { DurationSeconds: > 0 } current && current.Rows.Count > 0)
                scope.Add(current);

            if (scope.Count == 0) return null;

            var seconds = scope.Sum(x => x.DurationSeconds);
            if (seconds <= 0) return null;

            var totals = new Dictionary<string, MeterRow>(StringComparer.Ordinal);

            foreach (var row in scope.SelectMany(x => x.Rows))
            {
                if (totals.TryGetValue(row.Name, out var running))
                {
                    totals[row.Name] = running with
                    {
                        Damage = running.Damage + row.Damage,
                        Healed = running.Healed + row.Healed,
                        Deaths = running.Deaths + row.Deaths,

                        Job = string.IsNullOrEmpty(row.Job) ? running.Job : row.Job,
                    };
                }
                else
                {
                    totals[row.Name] = row;
                }
            }

            var damage = totals.Values.Sum(r => r.Damage);
            var healed = totals.Values.Sum(r => r.Healed);

            var rows = totals.Values.Select(r => r with
            {
                Dps = r.Damage / seconds,
                Hps = r.Healed / seconds,
                DamagePct = damage > 0 ? r.Damage / damage * 100 : 0,
                HealedPct = healed > 0 ? r.Healed / healed * 100 : 0,
            }).ToList();

            var built = new Snapshot
            {
                Title = "Overall",

                TeamMode = scope[^1].TeamMode,
                Zone = scope[^1].Zone,
                Duration = $"{seconds / 60:00}:{seconds % 60:00}",
                DurationSeconds = seconds,
                CapturedAt = scope[^1].CapturedAt,
                RaidDps = damage / seconds,
                RaidHps = healed / seconds,
                TotalDamage = damage,
                MaxDamage = rows.Count > 0 ? rows.Max(r => r.Damage) : 0,
                MaxHealed = rows.Count > 0 ? rows.Max(r => r.Healed) : 0,
                Rows = rows,
            };

            this.overallCache = built;
            this.overallFrom_ = this.live;
            this.overallCount = this.segments.Count;

            return built;
        }
    }

    internal void Clear()
    {
        lock (this.gate)
        {
            this.segments.Clear();
            this.live = null;
            this.overallFrom = 0;

            if (this.file is null) return;

            try
            {
                File.WriteAllText(this.file, string.Empty);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning($"Could not empty the session file: {ex.Message}");
            }
        }

        Probe.Line("history cleared by hand");
    }

    internal int OverallCount
    {
        get { lock (this.gate) return this.segments.Count - this.overallFrom + (this.live is null ? 0 : 1); }
    }

    internal List<Snapshot> Recent()
    {
        lock (this.gate) return Enumerable.Reverse(this.segments).ToList();
    }

    internal void Flush()
    {
        lock (this.gate)
        {
            if (this.live is { } last) this.Archive(last);
            this.live = null;
        }
    }
}
