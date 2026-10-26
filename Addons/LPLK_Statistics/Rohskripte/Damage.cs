using SwiftlyS2.Shared.Players;
namespace LPLK_Statistics;
public sealed partial class Plugin
{
 private sealed class DamageRow{public string Name="";public long Damage;public int Hits;}
 private readonly Dictionary<(string From,string To),DamageRow> damages=[];
 private readonly HashSet<string> shown=[];
 private void TrackDamage(IPlayer a,IPlayer v,int damage)
 {
  if(!Human(a)&&!Human(v))return;
  if(Human(a))sessions[a.PlayerID]=Session(a);if(Human(v))sessions[v.PlayerID]=Session(v);
  string from=Session(a),to=Session(v);
  if(!damages.TryGetValue((from,to),out var row))damages[(from,to)]=row=new(){Name=StatPolicy.Clean(v.Controller.PlayerName)};
  row.Damage+=damage;row.Hits++;
  if(settings.DamageConsole&&Human(a))a.SendConsole($"[LPLK] {row.Name}: {damage} Schaden, gesamt {row.Damage}/{row.Hits} Treffer.");
  if(settings.DamageCenter&&Human(a)&&Core.MenusAPI.GetCurrentMenu(a)is null)a.SendCenterHTML($"{StatPolicy.Html(row.Name)}: <font color='#FFD966'>{damage} Schaden</font>",3000);
 }
 private static string DamageText(DamageRow? row,string color)=>$"[{color}]{row?.Damage??0}[yellow] in [{color}]{row?.Hits??0}[yellow]";
 private void Summary(IPlayer p,string? killer)
 {
  string self=Session(p);if(shown.Contains(self))return;
  var targets=damages.Keys.Where(k=>k.From==self||k.To==self).Select(k=>k.From==self?k.To:k.From).Distinct().Where(t=>!settings.ShowOnlyKiller||killer is null||t==killer).ToArray();
  foreach(var t in targets)
  {
   damages.TryGetValue((self,t),out var given);damages.TryGetValue((t,self),out var taken);
   if(!settings.ShowAllDamages&&given is null&&taken is null)continue;
   // Opponent name comes from the matching session's outgoing row when available.
   var opponent=Core.PlayerManager.GetAllValidPlayers().FirstOrDefault(a=>Session(a)==t);
   string name=given?.Name??opponent?.Controller.PlayerName??"Gegner";
   int? remaining=opponent is null?null:!opponent.IsAlive?0:opponent.PlayerPawn is {IsValid:true} pawn?Math.Max(0,pawn.Health):null;
   string hp=remaining is null?"–":$"[{(remaining==0?"red":"green")}]{remaining}[yellow]";
   // Fixed semantic colours; the generic addon formatter would recolour both values alike.
   p.SendChat($"[yellow]{ChatTheme.Plain(StatPolicy.Clean(name))} Gegeben [white][{DamageText(given,"green")}[white]][yellow] Erhalten [white][{DamageText(taken,"red")}[white]][yellow] [white][[yellow]HP {hp}[white]][default]");
  }
  shown.Add(self);
  if(settings.NoRoundsMode)foreach(var k in damages.Keys.Where(k=>k.From==self||k.To==self).ToArray())damages.Remove(k);
 }
}
