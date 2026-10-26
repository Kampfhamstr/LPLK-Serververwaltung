using LPLK.Common;
using SwiftlyS2.Shared.Players;

namespace LPLK_Weapons;
public sealed partial class Plugin
{
    private WeaponRule Rule(string id)=>settings.Rules.GetValueOrDefault(id)??new();
    private void ChangeRule(string id,Func<WeaponRule,WeaponRule> edit)
    {
        Save(current=>{var rules=new Dictionary<string,WeaponRule>(current.Rules,StringComparer.Ordinal);rules[id]=edit(rules.GetValueOrDefault(id)??new());return current with{Rules=rules};});
    }
    private void SpecialRules(IPlayer p)=>AdminPage(p,"Waffen · Sonderregeln",_=>
        WeaponCatalog.All.Select(w=>(w.Id,w.Name)).Concat(new[]{("weapon_hegrenade","HE"),("weapon_flashbang","Flash"),("weapon_smokegrenade","Rauch"),("weapon_molotov","Molotow"),("weapon_incgrenade","Brandgranate"),("weapon_decoy","Köder"),("weapon_taser","Zeus")})
        .Concat(settings.Rules.Keys.Where(id=>WeaponCatalog.Find(id) is null).Select(id=>(id,id)))
        .DistinctBy(w=>w.Item1).Select(w=>(w.Item2,(Action<IPlayer>)(a=>RuleMenu(a,w.Item1)))).ToArray(),OpenAdmin);
    private void RuleMenu(IPlayer p,string id)=>AdminPage(p,"Regeln · "+id,_=>new (string,Action<IPlayer>)[]
    {
        ("Munition",a=>AmmoMenu(a,id)),("Zugriff",a=>AccessMenu(a,id)),
        ("Grundquote",a=>{a.SendLplkChat("[LPLK] Grundquote nach Teamgröße. Vorhandene Mapquoten haben Vorrang und bleiben in settings.json konfiguriert.");QuotaMenu(a,id);}),("Zurücksetzen",a=>ConfirmRemove(a,id))
    },SpecialRules);
    private void ConfirmRemove(IPlayer p,string id)
    {
        var expected=System.Text.Json.JsonSerializer.Deserialize<WeaponRule>(System.Text.Json.JsonSerializer.Serialize(settings.Rules.GetValueOrDefault(id)));
        AdminPage(p,"Sonderregel löschen?",_=>new (string,Action<IPlayer>)[]{("Ja",a=>{Save(current=>{if(!Equivalent(current.Rules.GetValueOrDefault(id),expected))throw new InvalidOperationException("Sonderregel inzwischen geändert; erneut öffnen.");var rules=new Dictionary<string,WeaponRule>(current.Rules);rules.Remove(id);return current with{Rules=rules};});SpecialRules(a);}),("Nein",a=>RuleMenu(a,id))},a=>RuleMenu(a,id));
    }
    private void AmmoMenu(IPlayer p,string id)=>AdminPage(p,"Munition · "+id,_=>new (string,Action<IPlayer>)[]
    {
        ($"Magazin: {Rule(id).Clip?.ToString()??"Standard"}",a=>AmmoNumber(a,id,false)),
        ($"Reservemagazine: {Rule(id).Ammo?.ToString()??"Standard"}",a=>AmmoNumber(a,id,true)),
        ("Unbegrenzt",a=>AdminPage(a,"Unbegrenzt · "+id,_=>new (string,Action<IPlayer>)[]
        {
            ($"Magazin: {Rule(id).UnlimitedClip}",b=>{ChangeRule(id,r=>r with{UnlimitedClip=!r.UnlimitedClip});AmmoMenu(b,id);}),
            ($"Reserve: {Rule(id).UnlimitedAmmo}",b=>{ChangeRule(id,r=>r with{UnlimitedAmmo=!r.UnlimitedAmmo});AmmoMenu(b,id);})
        },b=>AmmoMenu(b,id))),
        ($"Schusswechsel: {Rule(id).ReloadAfterShoot}",a=>{ChangeRule(id,r=>r with{ReloadAfterShoot=!r.ReloadAfterShoot});AmmoMenu(a,id);})
    },a=>RuleMenu(a,id));
    private void AmmoNumber(IPlayer p,string id,bool reserve)=>AdminPage(p,reserve?"Reservemagazine":"Magazingröße",_=>
    {
        var rows=new List<(string,Action<IPlayer>)>{("Standard",a=>{ChangeRule(id,r=>reserve?r with{Ammo=null}:r with{Clip=null});AmmoMenu(a,id);})};
        foreach(int value in new[]{0,1,2,3,5,10,17,20,30,50,100,200})rows.Add((value.ToString(),a=>{ChangeRule(id,r=>reserve?r with{Ammo=value}:r with{Clip=value});AmmoMenu(a,id);}));
        return rows;
    },a=>AmmoMenu(a,id));
    private void AccessMenu(IPlayer p,string id)=>AdminPage(p,"Zugriff · "+id,_=>new (string,Action<IPlayer>)[]
    {
        ($"Gesperrt: {Rule(id).BlockUsing}",a=>{ChangeRule(id,r=>r with{BlockUsing=!r.BlockUsing});AccessMenu(a,id);}),
        ($"Aufheben erlaubt: {Rule(id).AllowPickupWhenBlocked}",a=>{ChangeRule(id,r=>r with{AllowPickupWhenBlocked=!r.AllowPickupWhenBlocked});AccessMenu(a,id);}),
        ($"Quoten-Ausnahme: {Rule(id).BypassQuota}",a=>{ChangeRule(id,r=>r with{BypassQuota=!r.BypassQuota});AccessMenu(a,id);}),
        ("Rechte",a=>a.SendLplkChat("[LPLK] Ausnahme benötigt: "+(Rule(id).BypassPermission.Length>0?Rule(id).BypassPermission:"kein Einzelrecht")+"; alternativ alle: "+string.Join(", ",Rule(id).BypassPermissions)+". Rechte bleiben in settings.json konfiguriert."))
    },a=>RuleMenu(a,id));
    private void QuotaMenu(IPlayer p,string id)=>AdminPage(p,"Quote · "+id,_=>
        Rule(id).TeamQuota.Keys.Concat(new[]{0,2,4,6,8,10,12,16,20,32,64}).Distinct().Order()
        .Select(n=>($"Ab {n}: {(Rule(id).TeamQuota.TryGetValue(n,out int count)?count.ToString():"–")}",(Action<IPlayer>)(a=>QuotaValue(a,id,n)))).ToArray(),a=>RuleMenu(a,id));
    private void QuotaValue(IPlayer p,string id,int threshold)=>AdminPage(p,$"Quote ab {threshold}",_=>
        new int?[]{null,0,1,2,3,4,5,8,16,32,64,128,256}.Select(n=>(n?.ToString()??"Schwelle löschen",(Action<IPlayer>)(a=>
        {
            ChangeRule(id,r=>{var q=new Dictionary<int,int>(r.TeamQuota);if(n is {} count)q[threshold]=count;else q.Remove(threshold);return r with{TeamQuota=q};});QuotaMenu(a,id);
        }))).ToArray(),a=>QuotaMenu(a,id));
}
