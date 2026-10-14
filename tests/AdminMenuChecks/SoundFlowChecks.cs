extern alias SoundsAssembly;
using System.Reflection;
using SoundsAssembly::LPLK_Sounds;
using LPLK_Sounds = SoundsAssembly::LPLK_Sounds;

public static class SoundFlowChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var w=new MenuWorld();var p=new LPLK_Sounds.Plugin(w.Core);
        void Set(string name,object value)=>typeof(LPLK_Sounds.Plugin).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(p,value);
        void Open()=>typeof(LPLK_Sounds.Plugin).GetMethod("ShowSoundAudienceChoice",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(p,[w.Player,new NumberedSound(1,"test","Testton"),0,0,false,false]);
        Set("menusEnabled",true);Set("config",new SoundsConfig{Enabled=false});
        Set("catalog",new AssetCatalog{Sounds=new(){["test"]=new SoundAsset{Title="Testton",Event="Test.Sound"}}});
        Open();check(w.Current!.Configuration.Title!.Contains("An alle?"),"Sound asks for audience");
        check(w.Labels.Count==3&&w.Labels.Any(x=>x.Contains("Ja"))&&w.Labels.Any(x=>x.Contains("Nein")),"Audience is Ja/Nein/Zurück");
        await w.Click("Nein");check(w.Current!.Configuration.Title!.Contains("Empfänger"),"No opens recipient selection");
        await w.Click("Zurück");check(w.Current!.Configuration.Title!.Contains("An alle?"),"Recipient return restores audience decision");
        w.Permissions.Add("lplk.sounds.override");Open();await w.Click("Ja");
        check(w.Current!.Configuration.Title!.Contains("Override?"),"Admin public playback asks override");
        var oldYes=w.Find("Ja");w.Permissions.Clear();int messages=w.Messages.Count;await w.Click(oldYes);
        check(w.Messages.Count>messages&&w.Messages.Last().Contains("Berechtigung"),"Override permission rechecked at execution");
        w.Permissions.Add("lplk.sounds.override");Open();await w.Click("Ja");await w.Click("Nein");
        check(w.Messages.Last().Contains("deaktiviert"),"No uses normal sound path and still respects disabled addon");
        w.Permissions.Clear();Open();await w.Click("Ja");
        check(w.Current!.Configuration.Title!.Contains("An alle?")&&w.Messages.Last().Contains("deaktiviert"),"Ordinary player takes normal playback without override prompt");
        check(w.AllMenus.All(m=>m.Configuration.MaxVisibleItems==5),"Sound decision menus keep five visible rows");
    }
}
