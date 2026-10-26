using LPLK.Common;
using SwiftlyS2.Shared.Players;

namespace LPLK_CombatRules;
public sealed partial class Plugin
{
    private DamageRule Rule(string id)=>settings.Rules.GetValueOrDefault(id)??new();
    private void ChangeRule(string id,Func<DamageRule,DamageRule> edit)
    {
        Save(current=>{var rules=new Dictionary<string,DamageRule>(current.Rules,StringComparer.Ordinal);rules[id]=edit(rules.GetValueOrDefault(id)??new());return current with{Rules=rules};});
    }
    private void SpecialRules(IPlayer p)=>Page(p,"Kampf · Sonderregeln",_=>WeaponCatalog.All.Select(w=>(w.Id,w.Name))
        .Concat(settings.Rules.Keys.Where(id=>WeaponCatalog.Find(id) is null).Select(id=>(id,id)))
        .Select(w=>(w.Item2,(Action<IPlayer>)(a=>RuleMenu(a,w.Item1)))).ToArray(),OpenAdmin);
    private void RuleMenu(IPlayer p,string id)=>Page(p,"Treffer · "+id,_=>new (string,Action<IPlayer>)[]
    {
        ($"Nur Kopftreffer: {Rule(id).OnlyHeadshot}",a=>{ChangeRule(id,r=>r with{OnlyHeadshot=!r.OnlyHeadshot});RuleMenu(a,id);}),
        ("Schaden",a=>DamageMenu(a,id)),
        ("Status",a=>a.SendLplkChat($"[LPLK] {id}: Kopftreffer={Rule(id).OnlyHeadshot}; Schaden={Rule(id).Damage??"unverändert"}. Globale Kopftreffer-/Goldene Regeln gelten zusätzlich.")),
        ("Zurücksetzen",a=>ConfirmRemove(a,id))
    },SpecialRules);
    private void ConfirmRemove(IPlayer p,string id)
    {
        var expected=settings.Rules.GetValueOrDefault(id);
        Page(p,"Sonderregel löschen?",_=>new (string,Action<IPlayer>)[]{("Ja",a=>
        {
            Save(current=>{if(current.Rules.GetValueOrDefault(id)!=expected)throw new InvalidOperationException("Sonderregel inzwischen geändert; erneut öffnen.");var rules=new Dictionary<string,DamageRule>(current.Rules);rules.Remove(id);return current with{Rules=rules};});SpecialRules(a);
        }),("Nein",a=>RuleMenu(a,id))},a=>RuleMenu(a,id));
    }
    private void DamageMenu(IPlayer p,string id)=>Page(p,"Schaden · "+id,_=>new (string,Action<IPlayer>)[]
    {
        ("Unverändert",a=>{ChangeRule(id,r=>r with{Damage=null});RuleMenu(a,id);}),
        ("Festwert",a=>DamageValues(a,id,"=")),("Zuschlag",a=>DamageValues(a,id,"+")),
        ("Faktor",a=>DamageValues(a,id,"*"))
    },a=>RuleMenu(a,id));
    private void DamageValues(IPlayer p,string id,string op)=>Page(p,"Schaden · "+id,_=>
        (op=="*"?new[]{"0","0.25","0.5","1","1.5","2","4"}:op=="+"?new[]{"-100","-50","-25","-10","0","10","25","50","100"}:new[]{"0","1","10","25","50","100","200","500"})
        .Select(value=>(op=="+"&&value.StartsWith('-')?value:op+value,(Action<IPlayer>)(a=>{ChangeRule(id,r=>r with{Damage=op+value});RuleMenu(a,id);}))).ToArray(),a=>DamageMenu(a,id));
}
