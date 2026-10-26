using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;

namespace LPLK_Admin;

public sealed partial class Plugin
{
    private sealed record MenuCursor(int Index,string Label);
    private sealed record PlayerCursors(ulong Steam,string Session,Dictionary<string,MenuCursor> Menus);
    private readonly Dictionary<int,PlayerCursors> menuCursors=[];
    private readonly ConditionalWeakTable<IMenuAPI,object> cursorMenus=new();
    private static string CursorLabel(string label)=>Regex.Replace(System.Net.WebUtility.HtmlDecode(Regex.Replace(label,"<[^>]*>","")),@"^\s*\d+[.)]\s*","").Trim();
    private PlayerCursors Cursors(IPlayer player)
    {
        if(!menuCursors.TryGetValue(player.PlayerID,out var state)||state.Steam!=player.SteamID||state.Session!=player.SessionId.ToString())
            menuCursors[player.PlayerID]=state=new(player.SteamID,player.SessionId.ToString(),new(StringComparer.Ordinal));
        return state;
    }
    private string? CursorKey(IPlayer player,IMenuAPI menu)
    {
        if(ownedMenus.TryGetValue(menu,out _))return "admin:"+menu.Configuration.Title;
        if(moduleReturns.TryGetValue(player.PlayerID,out var route)&&route.SteamId==player.SteamID&&route.Session==player.SessionId.ToString()&&ModuleAllowed(player,route.Id))return route.Id+":"+menu.Configuration.Title;
        return null;
    }
    private void RememberCursor(IMenuAPI menu,MenuEventArgs args)
    {
        var player=args.Player;
        if(!active||player is null||!Connected(player)||!ReferenceEquals(Core.MenusAPI.GetCurrentMenu(player),menu)||CursorKey(player,menu) is not {} key)return;
        int index=menu.GetCurrentOptionIndex(player);if(index<0||index>=menu.Options.Count)return;
        if(LPLK.Common.MenuPresentation.Navigation(menu.Options[index].Text))return;
        var memory=Cursors(player).Menus;
        if(memory.Count>=128&&!memory.ContainsKey(key))memory.Remove(memory.Keys.First());
        memory[key]=new(index,CursorLabel(menu.Options[index].Text));
    }
    private void RestoreMenuCursor(IPlayer player,IMenuAPI menu)
    {
        if(CursorKey(player,menu) is not {} key)return;
        if(cursorMenus.TryGetValue(menu,out _))return;
        cursorMenus.Add(menu,new());
        // Hover remembers scrolling; selection also covers immediate keyboard clicks.
        menu.OptionHovered+=(_,args)=>RememberCursor(menu,args);
        menu.OptionSelected+=(_,args)=>RememberCursor(menu,args);
        if(!Cursors(player).Menus.TryGetValue(key,out var previous)||menu.Options.Count==0)return;
        var labels=menu.Options.Select(o=>CursorLabel(o.Text)).ToArray();
        int index=Array.IndexOf(labels,previous.Label);
        if(index<0&&previous.Label.Contains(':'))
        {
            string setting=previous.Label.Split(':')[0];var matches=Enumerable.Range(0,labels.Length).Where(i=>labels[i].Split(':')[0]==setting).ToArray();
            if(matches.Length==1)index=matches[0];
        }
        if(index<0){var available=Enumerable.Range(0,labels.Length).Where(i=>!LPLK.Common.MenuPresentation.Navigation(labels[i])).ToArray();if(available.Length==0)return;index=available.OrderBy(i=>Math.Abs(i-previous.Index)).First();}
        menu.MoveToOptionIndex(player,index);
    }
}
