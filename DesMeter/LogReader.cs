using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DesMeter;

internal sealed class AbilityLine
{
    public string Name = string.Empty;
    public int Casts;
    public int Hits;

    public long Direct;

    public long Raw;

    public int Crits;
    public int DirectHits;
    public long MaxHit;

    public double TickAmount;

    public double TickRaw;
    public int Ticks;

    public double Uptime;

    public double Absorbed;

    public double Amount => this.Direct + this.TickAmount + this.Absorbed;

    public double Overheal => this.Raw + this.TickRaw > 0 ? 1 - ((this.Direct + this.TickAmount) / (this.Raw + this.TickRaw)) : 0;
}

internal sealed class FightAbilities
{
    public readonly Dictionary<string, List<AbilityLine>> Damage = new(StringComparer.Ordinal);
    public readonly Dictionary<string, List<AbilityLine>> Healing = new(StringComparer.Ordinal);
    public readonly Dictionary<string, List<AbilityLine>> Taken = new(StringComparer.Ordinal);

    public double Seconds;

    public string? Problem;

    public string Recording = string.Empty;
}

internal static class LogReader
{

    private static readonly TimeSpan LeadIn = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan Tail = TimeSpan.FromSeconds(3);

    internal static Func<HashSet<uint>>? LimitBreaks;

    private static HashSet<uint>? limitBreaks;

    private static bool IsLimitBreak(string idHex)
    {
        limitBreaks ??= LimitBreaks?.Invoke() ?? new HashSet<uint>();
        return uint.TryParse(idHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id) && limitBreaks.Contains(id);
    }

    internal static Task<FightAbilities> ReadAsync(Snapshot fight, string localName, CancellationToken cancel)
        => Task.Run(() => Read(fight, localName, cancel), cancel);

    private enum Kind { Damage, Heal, Taken }

    private sealed class Pending
    {
        public Kind Kind;
        public AbilityLine Line = null!;
        public long Amount;
        public long HpBefore;
        public bool Crit;
        public bool DirectHit;
        public DateTime At;
    }

    private sealed class TickRow
    {
        public List<(string Source, string Status)> Active = null!;
        public long Amount;
        public long Effective;
    }

    private sealed class ShieldApp
    {
        public string Source = string.Empty;
        public string Status = string.Empty;
        public double Left;
        public bool Up = true;
    }

    private static bool IsEnemy(string id) => id.Length > 0 && id[0] == '4';

    private static FightAbilities Read(Snapshot fight, string localName, CancellationToken cancel)
    {
        var result = new FightAbilities();

        if (string.IsNullOrEmpty(fight.LogFile) || !File.Exists(fight.LogFile))
        {
            result.Problem = "The network log for this fight isn't there any more.";
            return result;
        }

        var players = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in fight.Rows) players.Add(row.Name == "YOU" && localName.Length > 0 ? localName : row.Name);

        var from = (fight.StartedAt - LeadIn).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        var to = (fight.CapturedAt + Tail).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

        var tables = new Dictionary<(Kind, string Player, string Ability), AbilityLine>();
        var pending = new Dictionary<(string Target, string Seq), List<Pending>>();
        var applied = new Dictionary<(string Target, string Status, string Source), DateTime>();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var statusNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ownerOf = new Dictionary<string, string>(StringComparer.Ordinal);

        var zeroed = new HashSet<string>(StringComparer.Ordinal);

        var tickingAlone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var shieldPct = new Dictionary<string, (int Pct, long MaxHp)>(StringComparer.Ordinal);
        var shieldWaiting = new Dictionary<string, List<(string Source, string Status)>>(StringComparer.Ordinal);
        var shieldRise = new Dictionary<string, (DateTime At, double Amount)>(StringComparer.Ordinal);
        var shields = new Dictionary<string, List<ShieldApp>>(StringComparer.Ordinal);

        void PlaceShield(string target, string source, string status, double size)
        {
            if (!shields.TryGetValue(target, out var list)) shields[target] = list = new List<ShieldApp>();

            foreach (var app in list)
            {
                if (app.Status != status || app.Left <= 0) continue;
                size += app.Left;
                app.Left = 0;
                app.Up = false;
            }

            list.Add(new ShieldApp { Source = source, Status = status, Left = size });
        }

        void ShieldReading(string target, int pct, long maxHp, DateTime at, bool absorbedHit)
        {
            var had = shieldPct.TryGetValue(target, out var was);
            shieldPct[target] = (pct, maxHp);

            if (!had && !(shieldWaiting.TryGetValue(target, out var first) && first.Count > 0)) return;
            if (!had) was = (0, maxHp);

            var delta = pct - was.Pct;

            if (delta > 0)
            {
                var size = delta * maxHp / 100.0;

                if (shieldWaiting.TryGetValue(target, out var waiting) && waiting.Count > 0)
                {
                    foreach (var (source, status) in waiting) PlaceShield(target, source, status, size / waiting.Count);
                    waiting.Clear();
                }
                else
                {
                    shieldRise[target] = (at, size);
                }

                return;
            }

            if (delta >= 0 || !absorbedHit || !shields.TryGetValue(target, out var up)) return;

            var amount = -delta * maxHp / 100.0;

            foreach (var app in up)
            {
                if (amount <= 0) break;
                if (app.Left <= 0) continue;

                var take = Math.Min(app.Left, amount);
                app.Left -= take;
                amount -= take;
                Credit(app, take);
            }

            if (amount > 0)
            {
                var standing = up.FindAll(a => a.Up);
                foreach (var app in standing) Credit(app, amount / standing.Count);
            }
        }

        void Credit(ShieldApp app, double amount)
        {
            if (players.Contains(app.Source)) LineFor(Kind.Heal, app.Source, Label(app.Source, app.Status)).Absorbed += amount;
        }

        var hpNow = new Dictionary<string, long>(StringComparer.Ordinal);

        var used = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var actionOf = new Dictionary<(string Source, string Status), string>();

        var spans = new Dictionary<(string Source, string Label, string Status), List<(DateTime Start, DateTime End)>>();
        var open = new Dictionary<(string Target, string Status, string Source), ((string, string, string) Slot, int Index)>();

        string Label(string source, string status) => actionOf.GetValueOrDefault((source, status), status);

        var dots = new List<TickRow>();
        var hots = new List<TickRow>();
        var activeDots = new Dictionary<string, List<(string Source, string Status)>>(StringComparer.Ordinal);

        var expires = new Dictionary<(string Target, string Source, string Status), DateTime>();

        bool DropThen(string target, List<(string Source, string Status)> on, string stamp)
        {
            var now = ParseTime(stamp);
            on.RemoveAll(a => expires.TryGetValue((target, a.Source, a.Status), out var until) && until.AddSeconds(0.5) < now);
            return true;
        }
        var activeHots = new Dictionary<string, List<(string Source, string Status)>>(StringComparer.Ordinal);

        DateTime? first = null;
        DateTime last = default;

        AbilityLine LineFor(Kind kind, string player, string ability)
        {
            if (!tables.TryGetValue((kind, player, ability), out var line))
                tables[(kind, player, ability)] = line = new AbilityLine { Name = ability };
            return line;
        }

        AbilityLine TakenLine(string player, string id, string name)
        {
            var line = LineFor(Kind.Taken, player, "#" + id);
            if (line.Name.StartsWith('#')) line.Name = name.StartsWith("unknown_", StringComparison.Ordinal) || name.Length == 0 ? "Attack" : name;
            return line;
        }

        void Hold(string target, string seq, Pending p)
        {
            if (!pending.TryGetValue((target, seq), out var list)) pending[(target, seq)] = list = new List<Pending>(1);
            list.Add(p);
        }

        var recorded = new List<(string At, string Entity, int Shield, long MaxHp)>();
        var recordingRises = 0;

        if (fight.RecordingFile.Length > 0 && File.Exists(fight.RecordingFile))
        {
            foreach (var line in File.ReadLines(fight.RecordingFile))
            {
                var r = line.Split('|');
                if (r.Length >= 3 && r[0] == "o") ownerOf.TryAdd(r[1], r[2]);
                else if (r.Length >= 6 && r[0] == "s") recorded.Add((r[1], r[2], int.Parse(r[3], CultureInfo.InvariantCulture), ParseLong(r[5])));
            }
        }

        var nextRecorded = 0;

        using var stream = new FileStream(fight.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        if (fight.LogOffset > 0 && fight.LogOffset < stream.Length) stream.Seek(Math.Max(0, fight.LogOffset - 65536), SeekOrigin.Begin);

        using var reader = new StreamReader(stream);
        if (stream.Position > 0) reader.ReadLine();

        string? raw;
        var count = 0;

        while ((raw = reader.ReadLine()) != null)
        {
            if ((++count & 0x3FFF) == 0) cancel.ThrowIfCancellationRequested();

            var bar = raw.IndexOf('|');
            if (bar < 0 || raw.Length < bar + 20) continue;

            var stamp = raw.AsSpan(bar + 1, 19);

            if (raw.StartsWith("03|", StringComparison.Ordinal))
            {
                var c = raw.Split('|', 8);
                if (c.Length > 6)
                {
                    names[c[2]] = c[3];
                    if (c[6].Length > 0 && c[6].TrimStart('0').Length > 0) ownerOf[c[2]] = c[6];
                }
            }

            if (stamp.CompareTo(from, StringComparison.Ordinal) < 0) continue;
            if (stamp.CompareTo(to, StringComparison.Ordinal) > 0) break;

            if (nextRecorded < recorded.Count && raw.Length >= bar + 24)
            {
                var fine = raw.AsSpan(bar + 1, 23);

                while (nextRecorded < recorded.Count && fine.CompareTo(recorded[nextRecorded].At, StringComparison.Ordinal) >= 0)
                {
                    var (atText, entity, shield, maxHp) = recorded[nextRecorded++];

                    var known = shieldPct.TryGetValue(entity, out var was);
                    if (known && shield <= was.Pct) continue;
                    if (known) recordingRises++;

                    ShieldReading(entity, shield, maxHp, ParseTime(atText), absorbedHit: false);
                }
            }

            var type = raw.AsSpan(0, bar);

            if (type is "21" or "22")
            {
                var f = raw.Split('|');
                if (f.Length < 47) continue;

                names[f[2]] = f[3];
                var source = f[3];
                var target = f[6];

                if (!players.Contains(source) && ownerOf.TryGetValue(f[2], out var owner) && names.TryGetValue(owner, out var ownerName)
                    && players.Contains(ownerName))
                    source = ownerName;
                var seq = f[44].TrimStart('0');
                var at = ParseTime(f[1]);

                if (players.Contains(source) && players.Contains("Limit Break") && IsLimitBreak(f[4])) source = "Limit Break";

                var byPlayer = players.Contains(source);
                var onPlayer = players.Contains(f[7]) && !players.Contains(source);
                if (!byPlayer && !onPlayer) continue;

                if (byPlayer && f[5] == "Superbolide" && target == f[2])
                {
                    var hpBefore = hpNow.TryGetValue(target, out var known) ? known : ParseLong(f[24]);
                    var line = TakenLine(source, f[4], f[5]);
                    line.Direct += Math.Max(0, hpBefore - 1);
                    line.Raw = line.Direct;
                    line.Hits++;
                }

                if (byPlayer && f[5] != "attack")
                {
                    if (!used.TryGetValue(source, out var mine)) used[source] = mine = new HashSet<string>(StringComparer.Ordinal);
                    mine.Add(f[5]);
                }

                var sawDamage = false;
                var sawHeal = false;

                for (var p = 0; p < 8; p++)
                {
                    var flagText = f[8 + (2 * p)];
                    if (flagText.Length == 0) continue;

                    var flags = ParseHex(flagText);
                    var kind = flags & 0xFF;

                    if (kind is 0x03 or 0x05 or 0x06 && !sawDamage)
                    {
                        sawDamage = true;
                        var pend = new Pending
                        {
                            Amount = Unscramble(f[9 + (2 * p)]),
                            Crit = (flags & 0x2000) != 0,
                            DirectHit = (flags & 0x4000) != 0,
                            At = at,
                        };

                        if (byPlayer && IsEnemy(target))
                        {
                            pend.Kind = Kind.Damage;
                            pend.Line = LineFor(Kind.Damage, source, f[5]);
                            Hold(target, seq, pend);
                        }
                        else if (onPlayer)
                        {
                            pend.Kind = Kind.Taken;
                            pend.Line = TakenLine(f[7], f[4], f[5]);
                            Hold(target, seq, pend);
                        }
                    }
                    else if (kind == 0x04 && byPlayer && !sawHeal)
                    {
                        sawHeal = true;

                        var healed = IsEnemy(target) ? f[2] : target;
                        var before = healed == f[2] ? ParseLong(f[34]) : ParseLong(f[24]);

                        var line = LineFor(Kind.Heal, source, f[5]);

                        if (!IsEnemy(target) && f[45] is "0" or "00") line.Casts++;

                        var healAmount = Unscramble(f[9 + (2 * p)]);

                        line.Hits++;
                        line.Raw += healAmount;

                        if ((flags & 0x200000) != 0) line.Crits++;

                        Hold(healed, seq, new Pending
                        {
                            Kind = Kind.Heal,
                            Line = line,
                            Amount = healAmount,
                            HpBefore = before,
                            At = at,
                        });
                    }
                }

                if (byPlayer && sawDamage && IsEnemy(target) && f[45] is "0" or "00") LineFor(Kind.Damage, source, f[5]).Casts++;

                if (byPlayer && !sawHeal && HotPotency.ByStatus.ContainsKey(f[5]) && f[45] is "0" or "00")
                    LineFor(Kind.Heal, source, f[5]).Casts++;
            }
            else if (type is "37")
            {

                var f = raw.Split('|', 11);
                if (f.Length < 7) continue;

                var after = ParseLong(f[5]);
                var tookHit = false;

                if (pending.Remove((f[2], f[4].TrimStart('0')), out var held))
                {
                    tookHit = held.Exists(h => h.Kind == Kind.Taken);

                    var confirmed = ParseTime(f[1]);

                    foreach (var hit in held)
                    {
                        var line = hit.Line;

                        if (hit.Kind != Kind.Heal)
                        {
                            line.Hits++;
                            if (hit.Crit) line.Crits++;
                            if (hit.DirectHit) line.DirectHits++;
                        }

                        if (hit.Kind == Kind.Heal)
                        {

                            var before = hpNow.TryGetValue(f[2], out var known) ? known : hit.HpBefore;
                            line.Direct += Math.Clamp(after - before, 0, hit.Amount);
                        }
                        else
                        {
                            line.Raw += hit.Amount;
                            line.Direct += hit.Amount;
                            if (hit.Amount > line.MaxHit) line.MaxHit = hit.Amount;
                        }

                        if (hit.Kind == Kind.Damage)
                        {

                            first ??= hit.At;
                            last = confirmed;
                        }
                    }
                }

                if (f[5].Length > 0) hpNow[f[2]] = after;
                if (f[5] == "0" && IsEnemy(f[2])) zeroed.Add(f[2]);

                if (!IsEnemy(f[2]) && f.Length > 9 && int.TryParse(f[9], out var shieldNow))
                    ShieldReading(f[2], shieldNow, ParseLong(f[6]), ParseTime(f[1]), tookHit);
            }
            else if (type is "38")
            {
                var f = raw.Split('|', 11);
                if (f.Length > 9 && !IsEnemy(f[2]) && int.TryParse(f[9], out var shieldNow))
                    ShieldReading(f[2], shieldNow, ParseLong(f[6]), ParseTime(f[1]), absorbedHit: false);
            }
            else if (type is "25")
            {

                var f = raw.Split('|', 5);
                if (f.Length < 4 || !IsEnemy(f[2]) || zeroed.Contains(f[2])) continue;

                var died = ParseTime(f[1]);
                foreach (var key in new List<(string Target, string Seq)>(pending.Keys))
                {
                    if (key.Target != f[2]) continue;

                    foreach (var hit in pending[key])
                    {
                        if (hit.Kind != Kind.Damage || (died - hit.At).TotalSeconds > 10) continue;

                        var line = hit.Line;
                        line.Hits++;
                        if (hit.Crit) line.Crits++;
                        if (hit.DirectHit) line.DirectHits++;
                        line.Raw += hit.Amount;
                        line.Direct += hit.Amount;
                        if (hit.Amount > line.MaxHit) line.MaxHit = hit.Amount;
                        first ??= hit.At;
                        last = died;
                    }

                    pending.Remove(key);
                }
            }
            else if (type is "39")
            {
                var f = raw.Split('|', 6);
                if (f.Length >= 5) hpNow[f[2]] = ParseLong(f[4]);
            }
            else if (type is "26" or "30")
            {
                var f = raw.Split('|', 10);
                if (f.Length < 9) continue;

                var status = f[3];
                statusNames[f[2].TrimStart('0')] = status;
                var source = f[6];
                var target = f[7];
                var key = (target, status, source);
                var isDot = DotPotency.ByStatus.ContainsKey(status);
                var isHot = HotPotency.ByStatus.ContainsKey(status);

                var when = ParseTime(f[1]);
                var mineStatus = players.Contains(source);

                if (ShieldStatus.Is(f[2], status) && !IsEnemy(target))
                {
                    if (type is "26")
                    {

                        if (shieldRise.Remove(target, out var rise) && (when - rise.At).TotalSeconds <= 0.6)
                        {
                            PlaceShield(target, source, status, rise.Amount);
                        }
                        else
                        {
                            if (!shieldWaiting.TryGetValue(target, out var waiting)) shieldWaiting[target] = waiting = new List<(string, string)>();
                            waiting.Add((source, status));
                        }
                    }
                    else if (shields.TryGetValue(target, out var list))
                    {
                        foreach (var app in list)
                        {
                            if (app.Source != source || app.Status != status || !app.Up) continue;
                            app.Left = 0;
                            app.Up = false;
                        }
                    }
                }

                if (type is "26")
                {
                    if (mineStatus)
                    {

                        if (!actionOf.ContainsKey((source, status)) && used.TryGetValue(source, out var mine)
                            && ActionFor(status, mine) is { } action)
                            actionOf[(source, status)] = action;

                        var slot = (source, Label(source, status), status);
                        if (!spans.TryGetValue(slot, out var list)) spans[slot] = list = new List<(DateTime, DateTime)>();

                        var lasts = double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0 ? d : 0;
                        var ends = lasts > 0 ? when.AddSeconds(lasts) : DateTime.MaxValue;

                        if (open.TryGetValue(key, out var was) && was.Index < spans[was.Slot].Count)
                        {
                            var span = spans[was.Slot][was.Index];
                            if (span.End > when) spans[was.Slot][was.Index] = (span.Start, when);
                        }

                        open[key] = (slot, list.Count);
                        list.Add((when, ends));
                    }

                    applied[key] = when;

                    if (isDot || isHot)
                    {
                        var lasts = double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var dur) && dur > 0 ? dur : 0;
                        expires[(target, source, status)] = lasts > 0 ? when.AddSeconds(lasts) : DateTime.MaxValue;
                    }

                    if (isDot) Track(activeDots, target, source, status, add: true);
                    if (isHot) Track(activeHots, target, source, status, add: true);
                }
                else
                {
                    applied.Remove(key);

                    if (open.Remove(key, out var was) && spans.TryGetValue(was.Slot, out var list) && was.Index < list.Count)
                    {
                        var span = list[was.Index];
                        if (span.End > when) list[was.Index] = (span.Start, when);
                    }

                    if (isDot) Track(activeDots, target, source, status, add: false);
                    if (isHot) Track(activeHots, target, source, status, add: false);
                }
            }
            else if (type is "24")
            {
                var f = raw.Split('|', 19);
                if (f.Length < 18) continue;

                var target = f[2];
                var amount = ParseHex(f[6]);
                var dot = f[4] == "DoT";

                var effective = dot ? amount : Math.Clamp(ParseLong(f[8]) - ParseLong(f[7]), 0, amount);
                hpNow[target] = ParseLong(f[7]) + (dot ? -amount : effective);

                if (f[5] != "0" && statusNames.TryGetValue(f[5].TrimStart('0'), out var named))
                {
                    var caster = names.GetValueOrDefault(f[17], string.Empty);
                    tickingAlone.Add(named);

                    if (players.Contains(caster) && (dot ? IsEnemy(target) : !IsEnemy(target)))
                    {
                        var line = LineFor(dot ? Kind.Damage : Kind.Heal, caster, Label(caster, named));
                        line.TickRaw += amount;
                        line.TickAmount += effective;
                        line.Ticks++;
                    }
                    else if (dot && players.Contains(f[3]))
                    {
                        var line = TakenLine(f[3], "dot" + f[5], named);
                        line.Direct += amount;
                        line.Raw += amount;
                        line.Hits++;
                    }

                    continue;
                }

                if (dot)
                {
                    if (IsEnemy(target))
                    {
                        if (activeDots.TryGetValue(target, out var on) && DropThen(target, on, f[1]) && on.Count > 0)
                            dots.Add(new TickRow { Active = new List<(string, string)>(on), Amount = amount, Effective = amount });
                    }
                    else if (players.Contains(f[3]))
                    {
                        var line = TakenLine(f[3], "dot", "Damage over time");
                        line.Direct += amount;
                        line.Raw += amount;
                        line.Hits++;
                    }
                }
                else if (f[4] == "HoT" && !IsEnemy(target))
                {
                    if (activeHots.TryGetValue(target, out var on) && DropThen(target, on, f[1]) && on.Count > 0)
                        hots.Add(new TickRow { Active = new List<(string, string)>(on), Amount = amount, Effective = effective });
                }
            }
        }

        var uptime = new Dictionary<(string Source, string Label), double>();
        var fightStart = first ?? last;

        foreach (var group in GroupSpans(spans))
        {
            var ((source, label), byStatus) = group;

            var list = byStatus.TryGetValue(label, out var own) ? own : byStatus.Values.SelectMany(x => x).ToList();
            list.Sort((x, y) => x.Start.CompareTo(y.Start));

            var total = 0d;
            DateTime runStart = default, runEnd = default;
            var running = false;

            foreach (var (a0, b0) in list)
            {

                var a = a0 < fightStart ? fightStart : a0;
                var b = b0 > last ? last : b0;
                if (b <= a) continue;

                if (!running || a > runEnd)
                {
                    if (running) total += (runEnd - runStart).TotalSeconds;
                    runStart = a;
                    runEnd = b;
                    running = true;
                }
                else if (b > runEnd)
                {
                    runEnd = b;
                }
            }

            if (running) total += (runEnd - runStart).TotalSeconds;
            uptime[(source, label)] = total;
        }

        foreach (var tick in dots) tick.Active.RemoveAll(a => tickingAlone.Contains(a.Status));
        foreach (var tick in hots) tick.Active.RemoveAll(a => tickingAlone.Contains(a.Status));
        dots.RemoveAll(t => t.Active.Count == 0);
        hots.RemoveAll(t => t.Active.Count == 0);

        SplitTicks(dots, DotPotency.ByStatus, players, (p, st) => LineFor(Kind.Damage, p, Label(p, st)));
        SplitTicks(hots, HotPotency.ByStatus, players, (p, st) => LineFor(Kind.Heal, p, Label(p, st)));

        result.Seconds = first is { } start ? Math.Max(1, (last - start).TotalSeconds) : 0;

        foreach (var ((kind, player, ability), line) in tables)
        {
            if (kind != Kind.Taken && uptime.TryGetValue((player, ability), out var seconds)) line.Uptime = seconds;

            if (line.Hits == 0 && line.Ticks == 0 && line.Absorbed <= 0) continue;
            if (kind == Kind.Heal && line.Amount <= 0 && line.Raw <= 0) continue;

            if (kind == Kind.Taken && line.Amount <= 0) continue;

            var table = kind switch { Kind.Damage => result.Damage, Kind.Heal => result.Healing, _ => result.Taken };
            if (!table.TryGetValue(player, out var list)) table[player] = list = new List<AbilityLine>();
            list.Add(line);
        }

        foreach (var table in new[] { result.Damage, result.Healing, result.Taken })
            foreach (var list in table.Values)
                list.Sort((a, b) => b.Amount.CompareTo(a.Amount));

        if (recorded.Count > 0 || fight.RecordingFile.Length > 0)
            result.Recording = $"recording: {recorded.Count} readings, {recordingRises} rises the log hadn't shown yet, {ownerOf.Count} owners known";

        if (result.Damage.Count == 0 && result.Healing.Count == 0 && result.Taken.Count == 0)
            result.Problem = "Nothing from this fight was found in the log.";

        return result;
    }

    private static Dictionary<(string Source, string Label), Dictionary<string, List<(DateTime Start, DateTime End)>>> GroupSpans(
        Dictionary<(string Source, string Label, string Status), List<(DateTime Start, DateTime End)>> spans)
    {
        var grouped = new Dictionary<(string, string), Dictionary<string, List<(DateTime, DateTime)>>>();

        foreach (var ((source, label, status), list) in spans)
        {
            if (!grouped.TryGetValue((source, label), out var byStatus)) grouped[(source, label)] = byStatus = new Dictionary<string, List<(DateTime, DateTime)>>(StringComparer.Ordinal);
            byStatus[status] = list;
        }

        return grouped;
    }

    private static string? ActionFor(string status, HashSet<string> actions)
    {
        if (actions.Contains(status)) return status;

        string? best = null;
        var bestLength = 3;

        foreach (var action in actions)
        {
            var n = 0;
            var limit = Math.Min(action.Length, status.Length);
            while (n < limit && char.ToLowerInvariant(action[n]) == char.ToLowerInvariant(status[n])) n++;

            var whole = n == limit;
            var midWord = n > 0 && n < limit && action[n - 1] != ' ' && status[n - 1] != ' ';
            if (!whole && !midWord) continue;

            if (n > bestLength)
            {
                best = action;
                bestLength = n;
            }
        }

        return best;
    }

    private static void Track(Dictionary<string, List<(string Source, string Status)>> active, string target, string source, string status, bool add)
    {
        if (!active.TryGetValue(target, out var on))
        {
            if (!add) return;
            active[target] = on = new List<(string, string)>();
        }

        on.RemoveAll(a => a.Source == source && a.Status == status);
        if (add) on.Add((source, status));
    }

    private static void SplitTicks(List<TickRow> ticks, Dictionary<string, int> potency, HashSet<string> players,
                                   Func<string, string, AbilityLine> lineFor)
    {
        if (ticks.Count == 0) return;

        var sources = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var tick in ticks)
            foreach (var (source, _) in tick.Active)
                sources.TryAdd(source, sources.Count);

        var n = sources.Count;
        var gram = new double[n, n];
        var rhs = new double[n];
        var row = new double[n];

        foreach (var tick in ticks)
        {
            Array.Clear(row);
            foreach (var (source, status) in tick.Active) row[sources[source]] += potency[status];

            for (var i = 0; i < n; i++)
            {
                if (row[i] == 0) continue;
                rhs[i] += row[i] * tick.Amount;
                for (var j = 0; j < n; j++) gram[i, j] += row[i] * row[j];
            }
        }

        var k = new double[n];
        for (var i = 0; i < n; i++) k[i] = gram[i, i] > 0 ? Math.Max(0, rhs[i] / gram[i, i]) : 0;

        for (var pass = 0; pass < 300; pass++)
        {
            for (var i = 0; i < n; i++)
            {
                if (gram[i, i] <= 0) continue;

                var dot = 0d;
                for (var j = 0; j < n; j++) dot += gram[i, j] * k[j];

                k[i] = Math.Max(0, k[i] + ((rhs[i] - dot) / gram[i, i]));
            }
        }

        foreach (var tick in ticks)
        {
            var total = 0d;
            foreach (var (source, status) in tick.Active) total += potency[status] * k[sources[source]];

            foreach (var (source, status) in tick.Active)
            {
                if (!players.Contains(source)) continue;

                var value = potency[status] * k[sources[source]];
                var part = total > 0 ? value / total : 1.0 / tick.Active.Count;

                var line = lineFor(source, status);
                line.TickRaw += tick.Amount * part;
                line.TickAmount += tick.Effective * part;
                line.Ticks++;
            }
        }
    }

    internal static long Unscramble(string field)
    {
        if (field.Length <= 4) return 0;

        var damage = ParseHex(field.AsSpan(0, field.Length - 4));

        if (field[^4] == '4')
        {
            var right = ParseHex(field.AsSpan(field.Length - 2));
            damage = damage - right + (right << 16);
        }

        return damage;
    }

    private static long ParseHex(ReadOnlySpan<char> s)
        => long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static long ParseLong(string s)
        => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static DateTime ParseTime(string s)
        => DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t.LocalDateTime : default;
}
