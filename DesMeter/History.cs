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
            if (this.live is { } previous && Ended(previous, next)) this.Archive(previous);
            this.live = next;
        }
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
