using SwiftlyS2.Shared.Players;
namespace LPLK_Economy;
public sealed partial class Plugin
{
    private readonly Dictionary<ulong,string> killDeaths=[];
    private void KillRewardDeath(IPlayer? victim,IPlayer? attacker)
    {
        if(!ready||!Active||victim is null||!Human(victim))return;
        string eventId=$"{boot}:{titleMapEpoch}:{Id(victim)}:{bountyLives.GetValueOrDefault(victim.SteamID)}";
        if(killDeaths.GetValueOrDefault(victim.SteamID)==eventId)return;
        killDeaths[victim.SteamID]=eventId;
        if(!settings.KillReward.Enabled||attacker is null||!Human(attacker)||victim.SteamID==attacker.SteamID||attacker.Controller.ControllingBot||victim.Controller.ControllingBot||attacker.Controller.TeamNum is not (2 or 3)||victim.Controller.TeamNum is not (2 or 3)||settings.KillReward.IgnoreTeamKills&&attacker.Controller.TeamNum==victim.Controller.TeamNum)return;
        // Snapshot in the synchronous death callback. Unknown cash creates no reward.
        int cash=Cash(victim);bank!.Open(Id(attacker),attacker.Name);
        long reward=bank.AwardKillReward(eventId,Id(attacker),Id(victim),victim.Name,cash,attacker.Controller.TeamNum==victim.Controller.TeamNum);
        if(reward>0&&settings.KillReward.AnnounceReward&&!bank.Muted(Id(attacker)))attacker.SendChat($"[LPLK Economy] Kill-Belohnung: {reward:N0} $ für {Ledger.SafeName(victim.Name)} gesichert ({settings.KillReward.PercentOfVictimCash}% von {cash:N0} $ Cash).");
        DeliverKillRewards();
    }
    private void DeliverKillRewards()
    {
        if(bank is null||failure is not null||!ready)return;
        foreach(var claim in bank.KillRewardClaims())
        {
            if(bank.Blocked(claim.Account))continue;
            var p=Core.PlayerManager.GetAllValidPlayers().FirstOrDefault(x=>Human(x)&&Id(x)==claim.Account);
            int before=0,toCash=0;long discard=0;string session="bank";uint pawn=0;byte team=0;
            if(p is not null&&claim.Mode!="Bank")
            {
                if(p.Controller.ControllingBot||p.Controller.TeamNum is not (2 or 3)||p.PlayerPawn is not {IsValid:true} live)continue;
                try{before=Cash(p);if(before>settings.MaxPlayerCash)continue;}catch(BankRejected){continue;}
                toCash=Math.Min(claim.Remaining,settings.MaxPlayerCash-before);session=p.SessionId.ToString();pawn=Core.EntitySystem.GetRefEHandle(live).Raw;team=p.Controller.TeamNum;
                if(claim.Mode=="Cash")discard=claim.Remaining-toCash;
            }
            long toBank=Math.Min(claim.Remaining-toCash-discard,Math.Max(0,bank.Limit-bank.Balance(claim.Account)));
            if(toCash+toBank+discard==0)continue;
            var intent=bank.PayKillReward(NewId(),claim.Id,claim.Account,session,before,toCash,toBank,discard);
            if(intent is not null){try{ExecuteBetCash(p!,intent,pawn,team,()=>ready);}catch(BankRejected e){p!.SendChat("[LPLK Economy] "+e.Message);continue;}}
            if(p is not null&&claim.Announce&&!bank.Muted(claim.Account))p.SendChat($"[LPLK Economy] Kill-Belohnung ausgezahlt: {toCash:N0} $ Cash, {toBank:N0} $ Bank."+(discard>0?$" {discard:N0} $ gemäß Cash-only-Regel verfallen.":""));
        }
    }
    private void KillRewardInfo(IPlayer p)
    {
        if(!CanUse(p)||!settings.KillReward.Enabled)throw new BankRejected("Kill-Belohnungen sind ausgeschaltet.");
        var s=settings.KillReward;
        p.SendChat($"[LPLK Economy] Kill-Belohnung: {s.PercentOfVictimCash}% des Opfer-Cash, ab {s.MinReward:N0} $ berechnetem Bonus, maximal {(s.MaxReward<0?"unbegrenzt":s.MaxReward.ToString("N0")+" $")}. Auszahlung: {s.RewardMode}. Dem Opfer wird dafür nichts abgezogen.");
        p.SendChat($"[LPLK Economy] Teamkills {(s.IgnoreTeamKills?"ausgeschlossen":"zugelassen")}; Bots und Botübernahme ausgeschlossen. Wie bisher auch im Warmup und in Pistolenrunden. Eigene offene Ansprüche: {bank!.KillRewardClaims().Where(c=>c.Account==Id(p)).Sum(c=>(long)c.Remaining):N0} $.");
    }
    private void KillRewardAdmin(IPlayer p)=>Page(p,"Kill-Belohnung · Verwaltung",_=>
    {
        var rows=new List<(string,Action<IPlayer>)>();
        if(settings.AllowToggleKillReward)rows.Add(($"Kill-Belohnung: {(settings.KillReward.Enabled?"AN":"AUS")}",a=>{if(!settings.AllowToggleKillReward)throw new BankRejected("Umschalten nicht freigegeben.");Save(settings with{KillReward=settings.KillReward with{Enabled=!settings.KillReward.Enabled}});KillRewardAdmin(a);}));
        rows.Add(("Berechnung & Status",a=>a.SendChat($"[LPLK Economy] {settings.KillReward.PercentOfVictimCash}% vom Opfer-Cash; Schwelle {settings.KillReward.MinReward:N0} $, Maximum {settings.KillReward.MaxReward:N0} $. Modus {settings.KillReward.RewardMode}; offene Ansprüche {bank?.KillRewardClaims().Sum(c=>(long)c.Remaining):N0} $. Angenommene Ansprüche werden auch nach Ausschalten abgewickelt.")));
        rows.Add(("Weitere Regeln",a=>a.SendChat("[LPLK Economy] settings.json > KillReward: Prozent, Schwelle, Maximum, Teamkills, Auszahlung und Hinweise. Feste Bot-Sperre aus BetBank bleibt erhalten. Offene Cash-Vorgänge über Status & offene Vorgänge prüfen.")));return rows;
    },EconomySettingsMenu,true);
}
