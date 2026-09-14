using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Interface.Textures;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace DesMeter;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static ITargetManager Targets { get; private set; } = null!;
    [PluginService] internal static IPartyList Party { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IKeyState Keys { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IDataManager Data { get; private set; } = null!;
    [PluginService] internal static ITextureProvider Textures { get; private set; } = null!;

    private const string CommandName = "/desmeter";

    private DateTime nextNameCheck = DateTime.MinValue;

    private readonly Dictionary<uint, uint> enemyHp = new();
    private DateTime nextHpScan = DateTime.MinValue;
    private DateTime lastEnemyHit = DateTime.MinValue;

    internal static Fonts Typeface { get; } = new();

    private readonly WindowSystem windows = new("DesMeter");
    private readonly Configuration config;
    private readonly IinactLink link;
    private readonly OptionsWindow options;
    private readonly BreakdownWindow breakdown;

    private readonly List<MeterWindow> meters = new();

    public Plugin()
    {
        Probe.Open();
        this.config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.link = new IinactLink();
        this.link.History.Open();
        this.config.Migrate();

        this.options = new OptionsWindow(this.config);
        this.breakdown = new BreakdownWindow(this.config);

        this.windows.AddWindow(this.options);
        this.windows.AddWindow(this.breakdown);

        foreach (var settings in this.config.Windows) this.Open(settings);

        this.options.AddWindow = this.NewWindow;
        this.options.CloseWindow = this.CloseWindow;

        this.options.Clear = this.ClearEverything;

        Commands.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Show or hide the meter.",
        });

        PluginInterface.UiBuilder.Draw += this.DrawAll;
        PluginInterface.UiBuilder.OpenMainUi += this.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += this.ToggleOptions;
        Framework.Update += this.OnUpdate;
        ClientState.TerritoryChanged += this.OnTerritoryChanged;

        var use = IntendedUse(ClientState.TerritoryType);
        this.link.History.PvpTitle = PvpTitle(use);
        Teams.Enter(use);
    }

    private static uint IntendedUse(uint territory)
        => Data.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>()
               .GetRowOrDefault(territory)?.TerritoryIntendedUse.RowId ?? uint.MaxValue;

    private void OnUpdate(IFramework _)
    {

        if (DateTime.UtcNow >= this.nextNameCheck)
        {
            this.nextNameCheck = DateTime.UtcNow.AddSeconds(1);

            var name = Objects.LocalPlayer?.Name.TextValue;
            if (!string.IsNullOrEmpty(name) && this.link.LocalName != name)
                this.link.LocalName = name;
        }

        this.link.TryConnect();
        this.link.History.Pulse();
        this.link.History.Pvp = ClientState.IsPvP;
        Teams.Scan();
        this.WatchToggleKey();
        var inCombat = Condition[ConditionFlag.InCombat];
        this.ScanEnemies(inCombat);
        this.link.Watch(inCombat, this.lastEnemyHit);
        this.NoteTarget();
    }

    private void NoteTarget()
    {
        if (this.link.FirstTarget.Length > 0) return;
        if (this.link.Current is not { TotalDamage: > 0 }) return;

        if (Targets.Target is { ObjectKind: ObjectKind.BattleNpc } target)
            this.link.FirstTarget = target.Name.TextValue;
    }

    private void ScanEnemies(bool inCombat)
    {
        if (!inCombat)
        {
            this.enemyHp.Clear();
            return;
        }

        var now = DateTime.UtcNow;
        if (now < this.nextHpScan) return;
        this.nextHpScan = now.AddMilliseconds(250);

        foreach (var obj in Objects)
        {
            if (obj is not IBattleNpc npc) continue;
            if (npc.BattleNpcKind is BattleNpcSubKind.NpcPartyMember or BattleNpcSubKind.Pet) continue;

            var hp = npc.CurrentHp;

            if (this.enemyHp.TryGetValue(npc.EntityId, out var was) && hp < was)
                this.lastEnemyHit = now;

            this.enemyHp[npc.EntityId] = hp;
        }
    }

    private static string PvpTitle(uint use) => use switch
    {
        18 => "Frontline",
        28 or 37 => "Crystalline Conflict",
        39 => "Rival Wings",
        _ => "PvP",
    };

    private void OnTerritoryChanged(uint territory)
    {
        var use = IntendedUse(territory);

        this.link.History.TerritoryChanged();
        this.link.History.PvpTitle = PvpTitle(use);
        Teams.Enter(use);

        foreach (var m in this.meters) m.TerritoryChanged();
    }

    private void DrawAll()
    {

        TextInput = ImGui.GetIO().WantTextInput;

        Typeface.Tick(ImGui.GetFontSize());
        this.windows.Draw();
    }

    private static bool TextInput;
    private DateTime comboLastHeld = DateTime.MinValue;

    private void WatchToggleKey()
    {
        if (this.config.ToggleKey == VirtualKey.NO_KEY || TextInput || GameTypingActive()) return;

        var held = Keys[this.config.ToggleKey]
                && Keys[VirtualKey.CONTROL] == this.config.ToggleCtrl
                && Keys[VirtualKey.MENU] == this.config.ToggleAlt
                && Keys[VirtualKey.SHIFT] == this.config.ToggleShift;

        if (!held) return;

        var now = DateTime.UtcNow;
        var fresh = now - this.comboLastHeld > TimeSpan.FromMilliseconds(300);

        this.comboLastHeld = now;

        if (fresh) this.Toggle();
    }

    private static unsafe byte Battalion(IPlayerCharacter pc)
        => ((FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)pc.Address)->Battalion;

    private static unsafe bool GameTypingActive()
    {
        var ui = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->GetUIModule();
        if (ui == null) return false;

        var atk = ui->GetRaptureAtkModule();
        return atk != null && atk->AtkModule.IsTextInputActive();
    }

    private void OnCommand(string command, string args)
    {
        if (args.Trim().StartsWith("probe", StringComparison.OrdinalIgnoreCase))
        {
            this.DumpWho();
            return;
        }

        this.Toggle();
    }

    private void DumpWho()
    {
        Probe.Line("── /desmeter probe ─────────────────────────────");
        Probe.Line($"party list: {Party.Length} members");

        for (var i = 0; i < Party.Length; i++)
        {
            var member = Party[i];
            if (member is null) continue;

            Probe.Line($"  party[{i}] \"{member.Name.TextValue}\" entityId={member.EntityId:X} "
                     + $"job={member.ClassJob.RowId} level={member.Level}");
        }

        var seen = 0;

        foreach (var obj in Objects)
        {
            if (obj is not IBattleNpc npc) continue;
            if (seen++ > 40) break;

            Probe.Line($"  npc \"{npc.Name.TextValue}\" kind={npc.ObjectKind} subKind={npc.BattleNpcKind} "
                     + $"ownerId={npc.OwnerId:X} entityId={npc.EntityId:X}");
        }

        var intended = Data.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>()
                           .GetRowOrDefault(ClientState.TerritoryType)?.TerritoryIntendedUse.RowId;

        Probe.Line($"pvp={ClientState.IsPvP} territory={ClientState.TerritoryType} intendedUse={intended} "
                 + $"teamsGate={Teams.Active}");

        var players = 0;

        foreach (var obj in Objects)
        {
            if (obj is not IPlayerCharacter pc) continue;
            if (players++ > 80) break;

            var flags = pc.StatusFlags;

            Probe.Line($"  player \"{pc.Name.TextValue}\" battalion={Battalion(pc)} "
                     + $"hostile={flags.HasFlag(StatusFlags.Hostile)} "
                     + $"party={flags.HasFlag(StatusFlags.PartyMember)} "
                     + $"alliance={flags.HasFlag(StatusFlags.AllianceMember)} "
                     + $"job={pc.ClassJob.RowId} self={pc.EntityId == Objects.LocalPlayer?.EntityId}");
        }

        Probe.Line("── end ─────────────────────────────────────────");
        Chat.Print("DesMeter: wrote who-is-here to probe.log");
    }

    private void Toggle()
    {
        var anyHidden = this.meters.Exists(m => !m.IsOpen);
        foreach (var m in this.meters) m.IsOpen = anyHidden;
    }

    private void Open(MeterSettings settings)
    {
        var meter = new MeterWindow(this.link, this.config, this.options, settings) { IsOpen = true };

        meter.OpenBreakdown = this.breakdown.Show;

        meter.Clear = this.ClearEverything;
        this.meters.Add(meter);
        this.windows.AddWindow(meter);
    }

    private void NewWindow()
    {
        if (this.config.Windows.Count >= Configuration.MaxWindows) return;

        var from = this.options.Target
                   ?? (this.config.Windows.Count > 0 ? this.config.Windows[0] : new MeterSettings());

        var settings = from.Copy(this.config.NextWindowId++);

        this.config.Windows.Add(settings);
        this.config.Save();

        this.Open(settings);
        this.options.Target = settings;
    }

    private void CloseWindow(MeterSettings settings)
    {
        var meter = this.meters.Find(m => m.Settings == settings);
        if (meter is null) return;

        this.meters.Remove(meter);
        this.windows.RemoveWindow(meter);

        this.config.Windows.Remove(settings);
        this.config.Save();
    }

    private void ClearEverything()
    {
        this.link.History.Clear();

        this.link.MuteCurrent();

        foreach (var m in this.meters) m.ResetView();
    }

    private void ToggleOptions() => this.options.Toggle();

    public void Dispose()
    {
        Framework.Update -= this.OnUpdate;
        ClientState.TerritoryChanged -= this.OnTerritoryChanged;
        PluginInterface.UiBuilder.Draw -= this.DrawAll;
        PluginInterface.UiBuilder.OpenMainUi -= this.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= this.ToggleOptions;

        this.windows.RemoveAllWindows();
        Commands.RemoveHandler(CommandName);

        this.config.Save();

        this.link.History.Flush();
        this.link.Dispose();
        Typeface.Dispose();
    }
}
