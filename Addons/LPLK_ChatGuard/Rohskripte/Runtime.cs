using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;

namespace LPLK_ChatGuard;

public sealed partial class Plugin
{
    private ModuleSupport _module=null!;
    private bool _active;
    private string? _configError;
    private long _nextScan;
    private IInterfaceManager? _interfaces;
    private bool BridgePresent=>Core.PluginManager.GetAllPlugins().Contains("lplk.serververwaltung.bridge");
    public override void UseSharedInterface(IInterfaceManager manager)=>_interfaces=manager;
    private bool BridgeFilterReady()
    {
        if(!_active||!BridgePresent)return false;
        try{return _interfaces?.TryGetSharedInterface<IReadOnlyDictionary<string,Delegate>>("LPLK.Bridge.Admin.v1",out var api)==true
            &&api is not null&&api.TryGetValue("chatguardReady",out var check)&&((Func<bool>)check)();}
        catch{return false;}
    }
    public override void ConfigureSharedInterface(IInterfaceManager manager)
    {
        _module.ConfigureSharedInterface(manager);
        manager.AddSharedInterface<IReadOnlyDictionary<string,Delegate>,Dictionary<string,Delegate>>("LPLK.ChatGuard.Filter.v1",new(){
            ["filter"]=(Func<int,string,bool,int>)((id,text,team)=>(int)(_active?FilterChat(id,text,team):HookResult.Stop)),
            ["ready"]=(Func<bool>)(()=>_active)});
    }
    [EventListener<EventDelegates.OnTick>]
    public void Tick()
    {
        if(!_active||Environment.TickCount64<_nextScan)return;_nextScan=Environment.TickCount64+1000;
        ScanPlayerNames();WriteStatus();
    }
    private bool LooksLikeAdvertisement(string value,bool isPlayerName,out string match)
    {
        var rules=new GlobalProtectionSnapshot();
        lock(_globalBlockedDomains)rules.Domains=[.._globalBlockedDomains];
        lock(_globalAllowedDomains)rules.AllowedDomains=[.._globalAllowedDomains];
        lock(_globalBlockedPhrases)rules.BlockedPhrases=[.._globalBlockedPhrases];
        lock(_globalMarketingKeywords)rules.MarketingKeywords=[.._globalMarketingKeywords];
        return ChatGuardPolicy.Match(value,isPlayerName,_config,rules,out match);
    }
    private void ScheduleKick(IPlayer player,Action kick)
    {
        int slot=player.PlayerID;ulong steam=player.SteamID;var session=player.SessionId;
        Core.Scheduler.NextWorldUpdate(()=>{
            if(!_active)return;var current=Core.PlayerManager.GetPlayer(slot);
            if(ChatGuardPolicy.CanApplyDeferred(_active,_config.Enabled,current is {IsValid:true,IsAuthorized:true,IsFakeClient:false}&&current.SteamID==steam&&current.SessionId.Equals(session),IsElevatedRoleProtected(steam,out _)))kick();
        });
    }
    private void EnsureRuleSeed()
    {
        string seed=Path.Combine(Core.PluginPath,"resources","rules.seed.json");
        if(File.Exists(_globalProtectionPath)||!File.Exists(seed))return;
        var rules=JsonSerializer.Deserialize<GlobalProtectionSnapshot>(File.ReadAllText(seed),_json)??throw new InvalidDataException("Leere Startregeln.");
        if(rules.Version!=2||rules.BlockedSteamIds.Count!=0)throw new InvalidDataException("Testregeln müssen ohne produktive Spieleridentitäten vorliegen.");
        File.WriteAllText(_globalProtectionPath,JsonSerializer.Serialize(rules,_json));
    }
    private void SaveConfig(AntiAdConfig settings)
    {
        string temp=_configPath+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(settings,_json));File.Move(temp,_configPath,true);_configLastWriteUtc=File.GetLastWriteTimeUtc(_configPath);
    }
    private void WriteStatus()
    {
        try{AtomicJson.Write(Path.Combine(_root,"status.json"),new{updatedAt=DateTimeOffset.UtcNow,version="0.1.0",enabled=_config.Enabled,bridgePresent=BridgePresent,filterReady=_active,bridgeFilterReady=BridgeFilterReady(),localData=true,centralExport=false,
            trackedDisconnects=_advertisingDisconnects.Count,activeGags=_gagUntil.Count,localDomains=_config.BlockedDomains.Count,allowedDomains=_config.AllowedDomains.Count,
            globalDomains=_globalBlockedDomains.Count,globalPhrases=_globalBlockedPhrases.Count,importedBlockedIdentities=_globalBlockedSteamIds.Count,configError=_configError});}
        catch(Exception e){Core.Logger.LogWarning(e,"ChatGuard-Status konnte nicht geschrieben werden.");}
    }
}
