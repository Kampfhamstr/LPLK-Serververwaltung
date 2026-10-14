using Microsoft.Extensions.Logging;
using LPLK.Common;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;

namespace LPLK_Sounds;

public sealed partial class Plugin
{
    private volatile bool menusEnabled;
    private readonly Dictionary<int, OwnedMenu> ownedMenus = [];
    private readonly Dictionary<int, long> lastMenuOpen = [];
    private sealed class OwnedMenu(int slot, ulong steam)
    {
        public int Slot { get; } = slot;
        public ulong Steam { get; } = steam;
        public IMenuAPI? Menu;
        public string? SoundTitle;
        public int Pending;
        public MenuNumbers Numbers { get; }=new();
    }

    private void OpenPlayerMenu(IPlayer player,Action show)
    {
        long now=Environment.TickCount64;
        if(lastMenuOpen.TryGetValue(player.Slot,out long last) && !PlaybackPolicy.Expired(last,now,.3))return;
        lastMenuOpen[player.Slot]=now;
        MenuAction(player,show);
    }
    private static void Reply(IPlayer player,string text)=>player.SendChat(text);
    private void MenuAction(IPlayer player,Action action)
    {
        try { action(); }
        catch(Exception ex)
        {
            Logger.LogWarning(ex,"LPLK-Sounds-Menüaktion fehlgeschlagen.");
            Reply(player,"[LPLK Sounds] "+ex.Message);
        }
    }
    private IMenuBuilderAPI MenuBuilder(string title)=>Core.MenusAPI.CreateBuilder()
        .DisableSound().EnableExit().SetPlayerFrozen(false).SetAutoCloseDelay(120)
        // Keep the server keybinds; visible rows are limited to five, logical sound pages to ten.
        .Design.SetMenuTitle(MenuColors.Heading(title)).Design.SetMaxVisibleItems(5)
        .Design.SetMenuFooterVisible(true)
        .Design.SetCommentVisible(true)
        .Design.SetDefaultComment("");
    private void BackButton(IMenuBuilderAPI builder,OwnedMenu owner,string label,Action<IPlayer> action,string comment="")
        =>Button(builder,owner,label,action,comment,navigation:true);
    private void Button(IMenuBuilderAPI builder,OwnedMenu owner,string label,Action<IPlayer> action,string comment="",bool navigation=false,int? soundNumber=null,bool? enabled=null)
    {
        var option=new ButtonMenuOption(navigation?MenuColors.Back(label):owner.Numbers.Next(label,soundNumber,enabled)) { CloseAfterClick=false,Comment=comment };
        option.Click+=(_,args)=>
        {
            // Swiftly invokes menu clicks on a worker thread. All game/state access belongs on the next tick.
            if(!menusEnabled || Interlocked.Exchange(ref owner.Pending,1)!=0)return ValueTask.CompletedTask;
            try
            {
                Core.Scheduler.NextTick(()=>
                {
                    try
                    {
                        if(!menusEnabled || !ownedMenus.TryGetValue(owner.Slot,out var active) || !ReferenceEquals(active,owner))return;
                        var player=Core.PlayerManager.GetPlayer(owner.Slot);
                        if(player is null || !MenuPolicy.SamePlayer(owner.Slot,owner.Steam,player.Slot,player.SteamID,player.IsAuthorized,player.IsValid,player.IsFakeClient)
                            || args.Player.Slot!=owner.Slot || !ReferenceEquals(Core.MenusAPI.GetCurrentMenu(player),owner.Menu))return;
                        MenuAction(player,()=>action(player));
                    }
                    finally { Interlocked.Exchange(ref owner.Pending,0); }
                });
            }
            catch(ObjectDisposedException) when(!menusEnabled) { Interlocked.Exchange(ref owner.Pending,0); }
            catch(Exception ex)
            {
                Interlocked.Exchange(ref owner.Pending,0);
                Logger.LogWarning(ex,"LPLK-Sounds-Menüaktion konnte nicht eingeplant werden.");
            }
            return ValueTask.CompletedTask;
        };
        builder.AddOption(option);
    }
    private void OpenOwnedMenu(IPlayer player,OwnedMenu owner,IMenuBuilderAPI builder)
    {
        owner.Menu=builder.Build();
        CloseOwnedMenu(player.Slot);
        ownedMenus[player.Slot]=owner;
        try { Core.MenusAPI.OpenMenuForPlayer(player,owner.Menu); }
        catch { CloseOwnedMenu(player.Slot);throw; }
    }
    private void CloseOwnedMenu(int slot)
    {
        if(!ownedMenus.Remove(slot,out var owner) || owner.Menu is null)return;
        try
        {
            var player=Core.PlayerManager.GetPlayer(slot);
            if(player is { IsValid:true,IsFakeClient:false } && player.SteamID==owner.Steam
                && ReferenceEquals(Core.MenusAPI.GetCurrentMenu(player),owner.Menu))
                Core.MenusAPI.CloseMenuForPlayer(player,owner.Menu);
        }
        finally
        {
            foreach(var option in owner.Menu.Options.ToArray())(option as IDisposable)?.Dispose();
            (owner.Menu as IDisposable)?.Dispose();
        }
    }
    private void CloseAllOwnedMenus()
    {
        foreach(int slot in ownedMenus.Keys.ToArray())
            try { CloseOwnedMenu(slot); } catch(Exception ex) { Logger.LogWarning(ex,"LPLK-Sounds-Menü konnte nicht vollstaendig geschlossen werden."); }
    }
    private void SweepMenus()
    {
        foreach(var (slot,owner) in ownedMenus.ToArray())
        {
            var player=Core.PlayerManager.GetPlayer(slot);
            if(player is null || !MenuPolicy.SamePlayer(slot,owner.Steam,player.Slot,player.SteamID,player.IsAuthorized,player.IsValid,player.IsFakeClient)
                || !ReferenceEquals(Core.MenusAPI.GetCurrentMenu(player),owner.Menu))
                try { CloseOwnedMenu(slot); } catch(Exception ex) { Logger.LogWarning(ex,"LPLK-Sounds-Menü-Cleanup fehlgeschlagen."); }
        }
    }
}
