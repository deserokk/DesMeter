using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace DesMeter;

internal sealed unsafe class FightRecorder
{
    internal readonly record struct Sample(DateTime At, uint Entity, byte Shield, uint Hp, uint MaxHp);

    private const int Slots = 8;

    private readonly object gate = new();
    private readonly List<Sample> samples = new();
    private readonly Dictionary<uint, uint> owners = new();

    private readonly uint[] entities = new uint[Slots];
    private readonly nint[] characters = new nint[Slots];
    private readonly short[] last = new short[Slots];
    private int members;
    private DateTime nextResolve = DateTime.MinValue;

    private DateTime armedUntil = DateTime.MinValue;
    private DateTime nextReport;
    private long secondTicks;
    private long secondWorst;
    private int secondFrames;
    private long runTicks;
    private long runWorst;
    private long runFrames;

    internal bool Armed => DateTime.UtcNow < this.armedUntil;

    private bool sawCombat;
    private bool warmed;

    internal void Arm()
    {

        this.armedUntil = DateTime.UtcNow.AddMinutes(30);
        this.sawCombat = false;
        this.nextReport = DateTime.UtcNow.AddSeconds(1);
        this.secondTicks = this.secondWorst = this.runTicks = this.runWorst = this.runFrames = 0;
        this.secondFrames = 0;
        Probe.Line("── perfsniff armed: FightRecorder.Tick, for the next fight ──");
    }

    internal void Tick(bool inCombat, bool pvpMatch)
    {

        if (!this.warmed && !inCombat)
        {
            this.warmed = true;
            this.Resolve();
        }

        if (this.Armed)
        {
            if (inCombat) this.sawCombat = true;
            else if (this.sawCombat) this.armedUntil = DateTime.MinValue;
        }

        var timing = this.Armed && inCombat;
        var start = timing ? Stopwatch.GetTimestamp() : 0;

        if (inCombat && !pvpMatch) this.Record();
        else this.members = 0;

        if (!timing)
        {
            if (this.runFrames > 0) this.Report(final: true);
            return;
        }

        var spent = Stopwatch.GetTimestamp() - start;
        this.secondTicks += spent;
        this.secondFrames++;
        if (spent > this.secondWorst) this.secondWorst = spent;

        if (DateTime.UtcNow >= this.nextReport) this.Report(final: false);
    }

    private void Report(bool final)
    {
        if (!final)
        {
            this.runTicks += this.secondTicks;
            this.runFrames += this.secondFrames;
            if (this.secondWorst > this.runWorst) this.runWorst = this.secondWorst;

            Probe.Line($"perf recorder: {this.secondFrames} frames, total {Micro(this.secondTicks):F1}us, "
                     + $"worst {Micro(this.secondWorst):F2}us, tracking {this.members}");

            this.secondTicks = this.secondWorst = 0;
            this.secondFrames = 0;
            this.nextReport = DateTime.UtcNow.AddSeconds(1);
            return;
        }

        var frames = this.runFrames;
        this.runFrames = 0;
        Probe.Line($"── perfsniff done: {frames} frames, average {Micro(this.runTicks) / Math.Max(1, frames):F3}us, "
                 + $"worst {Micro(this.runWorst):F2}us ──");
    }

    private static double Micro(long ticks) => ticks * 1_000_000.0 / Stopwatch.Frequency;

    private void Record()
    {
        var now = DateTime.UtcNow;
        if (now >= this.nextResolve)
        {
            this.nextResolve = now.AddSeconds(1);
            this.Resolve();
        }

        for (var i = 0; i < this.members; i++)
        {
            var c = (Character*)this.characters[i];
            if (c == null || c->EntityId != this.entities[i]) continue;

            var shield = c->ShieldValue;
            if (shield == this.last[i]) continue;

            this.last[i] = shield;

            lock (this.gate) this.samples.Add(new Sample(DateTime.Now, this.entities[i], shield, c->Health, c->MaxHealth));
        }
    }

    private void Resolve()
    {
        var group = GroupManager.Instance();
        var objects = GameObjectManager.Instance();
        if (objects == null) return;

        var count = 0;

        if (group != null && group->MainGroup.MemberCount > 0)
        {
            for (var i = 0; i < group->MainGroup.MemberCount && count < Slots; i++)
            {
                var id = group->MainGroup.PartyMembers[i].EntityId;
                if (id != 0 && id != 0xE0000000) this.Track(count++, id, objects);
            }
        }
        else if (Plugin.Objects.LocalPlayer is { } me)
        {
            this.Track(count++, me.EntityId, objects);
        }

        this.members = count;

        foreach (var obj in Plugin.Objects)
        {
            if (obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc) continue;
            if (obj.OwnerId is 0 or 0xE0000000 || this.owners.ContainsKey(obj.EntityId)) continue;

            for (var i = 0; i < count; i++)
            {
                if (this.entities[i] != obj.OwnerId) continue;
                lock (this.gate) this.owners[obj.EntityId] = obj.OwnerId;
                break;
            }
        }
    }

    private void Track(int slot, uint id, GameObjectManager* objects)
    {
        if (this.entities[slot] != id)
        {
            this.entities[slot] = id;
            this.last[slot] = -1;
        }

        this.characters[slot] = (nint)objects->Objects.GetObjectByEntityId(id);
    }

    internal string? Save(Snapshot fight, string folder)
    {
        List<Sample> inside;
        Dictionary<uint, uint> ownersNow;

        var from = fight.StartedAt.AddSeconds(-10);
        var to = fight.CapturedAt.AddSeconds(5);

        lock (this.gate)
        {
            inside = this.samples.FindAll(x => x.At >= from && x.At <= to);
            this.samples.RemoveAll(x => x.At < to.AddMinutes(-2));
            ownersNow = new Dictionary<uint, uint>(this.owners);
        }

        if (inside.Count == 0 && ownersNow.Count == 0) return null;

        try
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{fight.StartedAt:yyyyMMdd-HHmmss}.rec");

            var text = new StringBuilder();
            foreach (var (pet, owner) in ownersNow) text.Append("o|").Append(pet.ToString("X8")).Append('|').Append(owner.ToString("X8")).Append('\n');
            foreach (var x in inside)
            {
                text.Append("s|").Append(x.At.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture)).Append('|')
                    .Append(x.Entity.ToString("X8")).Append('|').Append(x.Shield).Append('|').Append(x.Hp).Append('|').Append(x.MaxHp).Append('\n');
            }

            File.WriteAllText(path, text.ToString());
            return path;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"Could not write the fight recording: {ex.Message}");
            return null;
        }
    }
}
