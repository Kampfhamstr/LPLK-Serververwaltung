// Bomb timer integration follows K4-BombWires 1.0.1 (K4ryuu), GPL-3.0.
// Round/session state, callbacks and administration rewritten for LPLK.
using LPLK.Common;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace LPLK_BombWires;

[PluginMetadata(Id = "LPLK_BombWires", Name = "LPLK_BombWires", Version = "0.1.4", Author = "LPLK / K4ryuu", Description = "Bombendrähte mit gültigkeitsgeprüften Menüs", MinimumAPIVersion = "1.4.14")]
public sealed class Plugin(ISwiftlyCore core) : BasePlugin(core)
{
    private ModuleSupport module = null!;
    private WireSettings settings = new();
    private readonly WireRound round = new();
    private readonly Dictionary<int, (IPlayer Player, IMenuAPI Menu, Guid Token)> menus = new();
    private static readonly string[] Colors = ["Rot", "Blau", "Gelb", "Grün"];

    public override void Load(bool hotReload)
    {
        module = new(Core, "LPLK_BombWires", "Bombendrähte", "lplk.bombwires.admin", OpenAdmin);
        settings = module.ReadSettings<WireSettings>(WireSettings.Validate); module.Active = true;
        // When loaded mid-round, do not attach a new guess to an already active bomb.
        Core.GameEvent.HookPost<EventRoundPrestart>(_ => { Reset(); return HookResult.Continue; });
        Core.GameEvent.HookPost<EventBombBeginplant>(e => { var p=e.UserIdPlayer; if(settings.Enabled && p is not null) { round.Begin(p.SteamID); Show(p,true); } return HookResult.Continue; });
        Core.GameEvent.HookPost<EventBombAbortplant>(_ => { Reset(); return HookResult.Continue; });
        Core.GameEvent.HookPost<EventBombPlanted>(_ => { if(settings.Enabled) { round.Plant(Random.Shared.Next(4)); CloseAll(); } return HookResult.Continue; });
        Core.GameEvent.HookPost<EventBombBegindefuse>(e => { if(settings.Enabled && round.Planted && !round.Resolved && e.UserIdPlayer is {} p) Show(p,false); return HookResult.Continue; });
        Core.GameEvent.HookPost<EventBombAbortdefuse>(e => { if(e.UserIdPlayer is {} p) Close(p.PlayerID); return HookResult.Continue; });
        Core.GameEvent.HookPost<EventBombDefused>(_ => { Reset(); return HookResult.Continue; });
        Core.GameEvent.HookPost<EventBombExploded>(_ => { Reset(); return HookResult.Continue; });
        Core.GameEvent.HookPost<EventPlayerDeath>(e => { if(e.UserIdPlayer is {} p) Close(p.PlayerID); return HookResult.Continue; });
    }

    public override void ConfigureSharedInterface(IInterfaceManager manager) => module.ConfigureSharedInterface(manager);
    public override void Unload() { if(module is not null) module.Active=false; Reset(); }
    [EventListener<EventDelegates.OnMapUnload>] public void MapUnload(IOnMapUnloadEvent e) => Reset();
    [EventListener<EventDelegates.OnClientDisconnected>] public void Disconnect(IOnClientDisconnectedEvent e) => Close(e.PlayerId);

    private void Reset() { round.Reset(); CloseAll(); }
    private void CloseAll() { foreach(int id in menus.Keys.ToArray()) Close(id); }
    private void Close(int id)
    {
        if(menus.Remove(id,out var entry) && entry.Player.IsValid) Core.MenusAPI.CloseMenuForPlayer(entry.Player,entry.Menu);
    }

    private void Show(IPlayer player, bool planting)
    {
        if(!player.IsValid || !player.IsAlive || player.IsFakeClient) return;
        Close(player.PlayerID);
        int generation=round.Generation; ulong steamId=player.SteamID; Guid token=Guid.NewGuid();
        long expires=Environment.TickCount64 + settings.MenuSeconds*1000L;
        // Swiftly's Space binding represents +jump, including remapped keys and mouse-wheel binds.
        var builder=Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).SetAutoCloseDelay(settings.MenuSeconds)
            .SetSelectButton(KeyBind.Space).Design.SetMenuTitle(MenuColors.Heading(planting?"LPLK · Sicheren Draht wählen":"LPLK · Draht ziehen (optional)")).Design.SetMaxVisibleItems(4);
        int[] order=[0,1,2,3]; if(settings.RandomizeOrder) Random.Shared.Shuffle(order);
        var numbers=new MenuNumbers();
        foreach(int color in order)
        {
            var option=new ButtonMenuOption(numbers.Next(Colors[color])) { CloseAfterClick=false };
            GameThreadMenu.Bind(Core,option,player,()=>module.Active,p=>
            {

                if(!module.Active || !settings.Enabled || !p.IsValid || !p.IsAlive || p.SteamID!=steamId || Environment.TickCount64>expires ||
                    !menus.TryGetValue(p.PlayerID,out var entry) || entry.Token!=token || generation!=round.Generation) return;
                if(planting)
                {
                    if(round.Choose(steamId,generation,color)) p.SendChat("[LPLK] Sicherer Draht: "+Colors[color]);
                }
                else
                {
                    var pawn=p.PlayerPawn;
                    var bomb=Core.EntitySystem.GetAllEntitiesByClass<CPlantedC4>().FirstOrDefault(b => b.IsValid && b.BombTicking && !b.BombDefused);
                    if(pawn is null || !pawn.IsValid || !pawn.IsDefusing || bomb is null || bomb.BombDefuser.Value?.Index!=pawn.Index) return;
                    bool? success=round.Cut(generation,color);
                    if(success==true) { bomb.DefuseCountDown.Value=0; bomb.DefuseCountDownUpdated(); Core.PlayerManager.SendChat("[LPLK] Richtiger Draht – Bombe wird entschärft!"); }
                    else if(success==false) { bomb.C4Blow.Value=0; bomb.C4BlowUpdated(); Core.PlayerManager.SendChat("[LPLK] Falscher Draht – die Bombe explodiert!"); }
                }
                Close(p.PlayerID); return;
            });
            builder.AddOption(option);
        }
        var menu=builder.Build(); menus[player.PlayerID]=(player,menu,token); Core.MenusAPI.OpenMenuForPlayer(player,menu);
        player.SendChat("[LPLK] Vor-/Zurücktaste (Standard: W/S): Draht wählen. Sprungtaste (Standard: Leertaste): bestätigen.");
        player.SendChat(planting
            ? "[LPLK] Ohne Auswahl wird der sichere Draht zufällig gewählt."
            : "[LPLK] Benutzen-Taste (Standard: E) zum Entschärfen halten. Drahtziehen ist optional; falscher Draht explodiert!");
    }

    [Command("lplk_bombwires",permission:"lplk.bombwires.admin")]
    public void Command(ICommandContext ctx) { if(ctx.Sender is {} p) OpenAdmin(p); else ctx.Reply($"LPLK_BombWires: enabled={settings.Enabled}; menuSeconds={settings.MenuSeconds}; planted={round.Planted}"); }
    private void Apply(WireSettings next,IPlayer player) { WireSettings.Validate(next); module.SaveSettings(next); settings=next; if(!next.Enabled) Reset(); OpenAdmin(player); }
    private void OpenAdmin(IPlayer player) => module.Menu(player,"LPLK · Bombendrähte",
        ($"Aktiv: {(settings.Enabled?"Ja":"Nein")}",p=>Apply(settings with {Enabled=!settings.Enabled},p)),
        ($"Menüzeit: {settings.MenuSeconds}s → +1",p=>Apply(settings with {MenuSeconds=Math.Min(10,settings.MenuSeconds+1)},p)),
        ("Menüzeit −1",p=>Apply(settings with {MenuSeconds=Math.Max(1,settings.MenuSeconds-1)},p)),
        ($"Reihenfolge mischen: {(settings.RandomizeOrder?"Ja":"Nein")}",p=>Apply(settings with {RandomizeOrder=!settings.RandomizeOrder},p)));
}
