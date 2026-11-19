using System.Reflection;
using LPLK.Common;
using LPLK_Admin;
using SwiftlyS2.Shared.Players;
public static class ClarityChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var w=new MenuWorld();w.Permissions.Add("lplk.admin");
        var module=new ModuleSupport(w.Core,"LPLK_Test","Test","lplk.test.admin",_=>{}){Active=true};
        bool enabled=false;int used=0;
        void Home(IPlayer p,string? section=null)=>MenuRows.Home(p,"Test",(a,t,r,b)=>module.Menu(a,t,b is null?0:1,b is null?r(a).ToArray():r(a).Append(("Zurück",b)).ToArray()),
            _=>new (string,Action<IPlayer>)[]{("Starten",a=>used++),("Status",a=>{}),("Spieler",a=>{}),("Seltene Aktion",a=>used++)},section,
            ("Regeln",_=>(new (string,Action<IPlayer>)[]{($"Aktiv: {enabled}",a=>{enabled=!enabled;Home(a,"Regeln");})})),
            ("Regeln",_=>(new (string,Action<IPlayer>)[]{("Weitere Regel",a=>used++)})));
        Home(w.Player);check(w.Labels.Count==4&&!w.Labels.Any(x=>x.Contains("Aktiv")),"Short module root separates settings and keeps common actions");
        await w.Click("Einstellungen");check(w.Labels.Count(x=>x.Contains("Regeln"))==1,"Repeated settings groups merge without duplicated navigation");
        await w.Click("Regeln");check(w.Find("Aktiv: AUS").Text.Contains(MenuColors.Red),"Disabled switch uses readable AUS and red");
        await w.Click("Aktiv: AUS");check(enabled&&w.Find("Aktiv: AN").Text.Contains(MenuColors.Green),"Real setting callback remains reachable and displays AN in green");
        await w.Click("Zurück");await w.Click("Aktionen");await w.Click("Seltene Aktion");check(used==1,"Overflow actions remain reachable without being discarded");
        await w.Click("Zurück");await w.Click("Zurück");check(w.Labels.Count==4,"Settings return reaches short root");
        var stale=w.Find("Starten");w.Permissions.Clear();await w.Click(stale);check(used==1,"Home callback retains permission checks");
        w.Permissions.Add("lplk.admin");w.IncludePlayer=true;
        var plugin=new Plugin(w.Core);plugin.Load(true);plugin.UseSharedInterface(w.Interfaces);
        var state=new PlayerControlState(false,false);
        typeof(Plugin).GetField("playerControlState",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(plugin,(Func<IPlayer,PlayerControlState>)(_=>state));
        async Task Player(){plugin.Admin(w.Context);await w.Click("Spielerverwaltung");await w.Click(w.FindExact("Spieler"));await w.Click("Testspieler");}
        await Player();check(w.Labels.Any(x=>x.Contains("Einfrieren"))&&!w.Labels.Any(x=>x.Contains("Freigeben")),"Unfrozen player offers only freeze");
        check(w.Labels.Any(x=>x.Contains("Noclip einschalten"))&&!w.Labels.Any(x=>x.Contains("Noclip ausschalten")),"Noclip-off player offers only enabling");
        await w.Click("Einfrieren");state=new(true,true);int calls=w.AdminCalls.Count;await w.Click("Bestätigen");
        check(w.AdminCalls.Count==calls&&w.Labels.Any(x=>x.Contains("Freigeben")),"Changed control state before confirmation avoids stale action and refreshes player");
        await Player();check(!w.Labels.Any(x=>x.Contains("Einfrieren"))&&w.Labels.Any(x=>x.Contains("Noclip ausschalten")),"Frozen/noclip-on player gets matching reverse actions");
        state=default;await Player();check(!w.Labels.Any(x=>x.Contains("Noclip")||x.Contains("Einfrieren")||x.Contains("Freigeben")),"Unknown native state is not guessed");
        plugin.Unload();
    }
}
