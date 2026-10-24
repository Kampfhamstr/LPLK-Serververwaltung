using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;

namespace LPLK.Common;

// Five visible rows in one scrolling list; explicit callers may request pages.
public static class CompactMenu
{
    public static void Open(ISwiftlyCore core,IPlayer player,string title,Func<bool> active,Func<IPlayer,bool> allowed,
        Func<IPlayer,IReadOnlyList<(string Label,Action<IPlayer> Action)>> entries,Action<IPlayer>? back=null,int page=0,float autoCloseSeconds=0,bool scroll=true)
    {
        if(!active()||!allowed(player))return;
        var rows=entries(player);int size=scroll?Math.Max(1,rows.Count):rows.Count<=4?4:2;
        int pages=Math.Max(1,(rows.Count+size-1)/size);page=Math.Clamp(page,0,pages-1);
        var menu=core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).SetAutoCloseDelay(autoCloseSeconds).Design.SetMenuTitle(MenuColors.Heading(title+(pages>1?$" · {page+1}/{pages}":""))).Design.SetMaxVisibleItems(5)
            .Design.SetGlobalScrollStyle(SwiftlyS2.Shared.Menus.MenuOptionScrollStyle.WaitingCenter);
        var numbers=new MenuNumbers();
        void Add(string text,Action<IPlayer> action,int? number=null)
        {
            var button=new ButtonMenuOption(number is {} n?numbers.Next(text,n,MenuColors.SwitchValue(text)):MenuColors.Back(text)){CloseAfterClick=false};
            GameThreadMenu.Bind(core,button,player,active,p=>{if(allowed(p))action(p);});menu.AddOption(button);
        }
        for(int i=page*size;i<Math.Min(rows.Count,(page+1)*size);i++)Add(rows[i].Label,rows[i].Action,i+1);
        if(rows.Count==0)menu.AddOption(new TextMenuOption(MenuColors.Heading("Keine Einträge verfügbar.")));
        if(page>0)Add("Vorherige Seite",p=>Open(core,p,title,active,allowed,entries,back,page-1,autoCloseSeconds));
        if(page+1<pages)Add("Nächste Seite",p=>Open(core,p,title,active,allowed,entries,back,page+1,autoCloseSeconds));
        if(back is not null)Add("Zurück",back);
        var built=menu.Build();MenuPresentation.Apply(built);core.MenusAPI.OpenMenuForPlayer(player,built);
    }
}
