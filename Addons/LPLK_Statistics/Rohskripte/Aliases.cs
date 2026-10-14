using SwiftlyS2.Shared.Commands;
namespace LPLK_Statistics;
public sealed partial class Plugin
{
 [Command("nades")]public void Nades(ICommandContext c)=>NadeMenu(c);
 [Command("nadesmenu")]public void NadesMenu(ICommandContext c)=>NadeMenu(c);
 [Command("ntop")]public void NTop(ICommandContext c)=>NadeTop(c);
 [Command("nstats")]public void NStats(ICommandContext c)=>NadeStats(c);
 [Command("wkills")]public void WKills(ICommandContext c)=>WeaponKills(c);
 [Command("wmenu")]public void WMenu(ICommandContext c)=>WeaponMenu(c);
 [Command("wtop")]public void WTop(ICommandContext c)=>WeaponTop(c);
 [Command("wstats")]public void WStats(ICommandContext c)=>WeaponStats(c);
 [Command("nadestattrak")]public void NadeStatTrak(ICommandContext c)=>StatTrak(c);
 [Command("nstat")]public void NStat(ICommandContext c)=>StatTrak(c);
 [Command("nadeadmin")]public void NadeAdmin(ICommandContext c)=>Status(c);
 [Command("nadekillsadmin")]public void NadeKillsAdmin(ICommandContext c)=>Status(c);
}
