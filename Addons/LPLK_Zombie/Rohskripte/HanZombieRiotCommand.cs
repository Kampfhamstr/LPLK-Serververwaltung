// LPLK command routing. Legacy console names remain available with current permissions.
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using LPLK.Common;
namespace LPLK_Zombie;

public sealed class HanZriotCommands(ISwiftlyCore core,IOptionsMonitor<HanZriotCFG> config,IStageConfigProvider days,HanZriotGlobals globals,HanZriotService service):IDisposable
{
    private readonly List<Guid> commands=[];
    private Guid? joinHook;
    public bool Allowed(IPlayer player)=>globals.Active&&player.IsValid&&!player.IsFakeClient&&(core.Permission.PlayerHasPermission(player.SteamID,"lplk.admin")||core.Permission.PlayerHasPermission(player.SteamID,"lplk.zombie.admin")||(!string.IsNullOrWhiteSpace(config.CurrentValue.AdminCommandPermission)&&core.Permission.PlayerHasPermission(player.SteamID,config.CurrentValue.AdminCommandPermission)));
    private bool Target(IPlayer actor,IPlayer target)
    {
        int Rank(IPlayer p)=>AdminActionPolicy.Rank(permission=>core.Permission.PlayerHasPermission(p.SteamID,permission));
        return Allowed(actor)&&AdminActionPolicy.CanTarget(actor.SteamID,target.SteamID,Rank(actor),Rank(target));
    }
    public void Command()
    {
        joinHook=core.Command.HookClientCommand(OnJoinTeam);
        foreach(var (name,handler,admin) in new (string,ICommandService.CommandListener,bool)[]{("next",NextDay,true),("setday",SelectDay,true),("diff",Difficulty,true),("human",Human,true),("zombie",Zombie,true),("hud",Hud,false),("dmg",DamageHud,false)})
        foreach(var alias in new[]{"sw_zriot_"+name,"zriot_"+name})
            commands.Add(core.Command.RegisterCommand(alias,c=>{if(!globals.Ready||!globals.Active)return;if(c.Sender is not {} p||!p.IsValid||p.IsFakeClient)return;if(admin&&!Allowed(p)){c.ReplyLplk("[LPLK] Keine Berechtigung.");return;}handler(c);},true,"","LPLK Zombie "+name));
    }
    public void UpDateServerCommand()
    {
        if(!globals.Active||!globals.Ready)return;
        foreach(string command in new[]{"bot_quota_mode fill","bot_quota 20","mp_ignore_round_win_conditions 1","mp_warmup_end 1","bot_join_after_player 1","sv_human_autojoin_team 1","mp_humanteam CT","bot_chatter off","bot_knives_only","mp_autokick 0","mp_round_restart_delay 0","mp_autoteambalance 0"})core.Engine.ExecuteCommand(command);
    }
    private HookResult OnJoinTeam(int id,string command)
    {
        if(!globals.Active||!globals.Ready)return HookResult.Continue;
        var p=core.PlayerManager.GetPlayer(id);if(p is not {IsValid:true,IsFakeClient:false})return HookResult.Continue;
        string[] parts=command.Split(' ',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        if(parts.Length!=2||!parts[0].Equals("jointeam",StringComparison.OrdinalIgnoreCase))return HookResult.Continue;
        if(parts[1] is "1" or "2"){p.SendLplkChat("[LPLK] In diesem Modus spielen Menschen im CT-Team.");return HookResult.Stop;}
        if(parts[1]=="3"){p.SwitchTeam(Team.CT);globals.Scheduler.DelayBySeconds(1,globals.Scheduler.ForPlayer(p,()=>service.JoinTeamCheck(p)));}
        return HookResult.Continue;
    }
    public void SetDay(int day)
    {
        if(!globals.Active||!globals.Ready||day<1||day>days.GetConfig().Days.Count)return;
        globals.Scheduler.Advance();globals.RiotDay=day;service.ForceDayEnd();
    }
    private void NextDay(ICommandContext c){SetDay(Math.Min(globals.RiotDay+1,days.GetConfig().Days.Count));c.ReplyLplk("[LPLK] Nächste Welle vorbereitet.");}
    private void SelectDay(ICommandContext c){if(c.Args.Length!=1||!int.TryParse(c.Args[0],out int day)||day<1||day>days.GetConfig().Days.Count){c.ReplyLplk("[LPLK] Welle: 1 bis "+days.GetConfig().Days.Count);return;}SetDay(day);c.ReplyLplk("[LPLK] Welle "+day+" vorbereitet.");}
    private void Difficulty(ICommandContext c){globals.KillCount=1000;globals.KillPercent=100;c.ReplyLplk("[LPLK] Erhöhte Schwierigkeit für die nächste Map vorbereitet.");}
    private void Hud(ICommandContext c){int slot=c.Sender!.PlayerID;globals.PlayerHud[slot]=!globals.PlayerHud[slot];}
    private void DamageHud(ICommandContext c){int slot=c.Sender!.PlayerID;globals.PlayerDmgHud[slot]=!globals.PlayerDmgHud[slot];}
    private void Human(ICommandContext c)=>ChangeTeam(c,false);
    private void Zombie(ICommandContext c)=>ChangeTeam(c,true);
    private void ChangeTeam(ICommandContext c,bool zombie)
    {
        if(c.Args.Length==0){c.ReplyLplk("[LPLK] Bitte Steam-ID oder eindeutigen Spielernamen angeben.");return;}
        string selector=string.Join(' ',c.Args);
        var matches=core.PlayerManager.GetAllValidPlayers().Where(p=>!p.IsFakeClient&&(p.SteamID.ToString()==selector||p.Name==selector)).ToArray();
        if(matches.Length!=1){c.ReplyLplk("[LPLK] Spieler nicht eindeutig gefunden.");return;}
        var target=matches[0];if(!Target(c.Sender!,target)){c.ReplyLplk("[LPLK] Ziel ist durch Immunität geschützt.");return;}
        globals.BeAZombie[target.PlayerID]=zombie?-1:0;target.ChangeTeam(zombie?Team.T:Team.CT);c.ReplyLplk("[LPLK] Team geändert.");
    }
    public void Dispose(){foreach(var id in commands)core.Command.UnregisterCommand(id);commands.Clear();if(joinHook is {} hook)core.Command.UnhookClientCommand(hook);joinHook=null;}
}
