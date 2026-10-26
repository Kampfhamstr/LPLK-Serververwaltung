using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using LPLK.Common;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;

namespace LPLK_Economy;

// Menu input is deliberately stricter than the historical command parser: a comma
// is decimal, dots group thousands, and no fractional dollar is silently rounded.
public static class MenuAmount
{
    public static long Parse(string text,long minimum=1,long maximum=9_000_000_000_000_000)
    {
        text=text.Trim();
        if(text.Length is <1 or >32 || !Regex.IsMatch(text,@"\A(?:[0-9]+|[0-9]{1,3}(?:\.[0-9]{3})+)(?:,[0-9]+)?[kKmMbB]?\z"))
            throw new BankRejected("Betrag ungültig. Beispiele: 2500, 2.500 oder 2,5k.");
        decimal multiplier=char.ToLowerInvariant(text[^1]) switch{'k'=>1000,'m'=>1_000_000,'b'=>1_000_000_000,_=>1};
        if(multiplier!=1)text=text[..^1];
        if(!decimal.TryParse(text.Replace(".","").Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out decimal value)
            ||value>maximum/multiplier)throw new BankRejected($"Betrag muss zwischen {minimum:N0} und {maximum:N0} liegen.");
        value*=multiplier;
        if(value<minimum||value>maximum||value!=decimal.Truncate(value))throw new BankRejected($"Ganze Beträge zwischen {minimum:N0} und {maximum:N0} eingeben.");
        return (long)value;
    }
}

public sealed partial class Plugin
{
    private sealed class AmountRequest(IPlayer p,IMenuAPI menu,long period,bool admin,Action<IPlayer,string> accept,Action<IPlayer> back)
    {
        public readonly ulong Steam=p.SteamID;
        public readonly string Session=p.SessionId.ToString();
        public readonly IMenuAPI Menu=menu;
        public readonly long Period=period,Expires=Environment.TickCount64+60_000;
        public readonly bool Admin=admin;
        public readonly Action<IPlayer,string> Accept=accept;
        public readonly Action<IPlayer> Back=back;
        public int Busy;
    }
    private readonly ConcurrentDictionary<int,AmountRequest> amountRequests=new();
    private Guid amountChatHook;

    private void AmountInput(IPlayer p,string title,Action<IPlayer,long> accept,Action<IPlayer> back,bool admin=false,long minimum=1,long maximum=9_000_000_000_000_000)
    {
        RefreshSettings();
        if(!Human(p)||!(admin?module.CanAdmin(p):CanUse(p)))throw new BankRejected("Keine Berechtigung für die Eingabe.");
        var builder=Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).SetAutoCloseDelay(60)
            .Design.SetMenuTitle(MenuColors.Heading(Escape(title))).Design.SetMaxVisibleItems(5)
            .Design.SetGlobalScrollStyle(MenuOptionScrollStyle.WaitingCenter)
            .AddOption(new TextMenuOption(MenuColors.Heading("Betrag im Textchat eingeben")));
        var cancel=new ButtonMenuOption(MenuColors.Back("Zurück")){CloseAfterClick=false};
        GameThreadMenu.Bind(Core,cancel,p,()=>module.Active,a=>{amountRequests.TryRemove(a.PlayerID,out _);Guard(a,()=>back(a));});
        var menu=builder.AddOption(cancel).Build();
        Core.MenusAPI.OpenMenuForPlayer(p,menu);
        amountRequests[p.PlayerID]=new(p,menu,bank?.Period??0,admin,(a,text)=>accept(a,MenuAmount.Parse(text,minimum,maximum)),back);
        p.SendLplkChat("[LPLK] Betrag jetzt im Textchat eingeben, z. B. 2500, 2.500 oder 2,5k. Abbrechen: abbrechen. Zeit: 60 Sekunden.");
    }

    private bool CurrentAmount(IPlayer? p,AmountRequest request)=>module.Active&&p is not null&&Human(p)&&p.SteamID==request.Steam
        &&p.SessionId.ToString()==request.Session&&Environment.TickCount64<request.Expires
        &&ReferenceEquals(Core.MenusAPI.GetCurrentMenu(p),request.Menu);

    private HookResult AmountChat(int slot,string text,bool teamOnly)
    {
        if(!amountRequests.TryGetValue(slot,out var request))return HookResult.Continue;
        var p=Core.PlayerManager.GetPlayer(slot);
        if(!CurrentAmount(p,request)){amountRequests.TryRemove(new KeyValuePair<int,AmountRequest>(slot,request));return HookResult.Continue;}
        // A command leaves the prompt and continues to its normal handler.
        string input=text.Trim();
        if(input.StartsWith('!')||input.StartsWith('/')){amountRequests.TryRemove(new KeyValuePair<int,AmountRequest>(slot,request));return HookResult.Continue;}
        if(Interlocked.Exchange(ref request.Busy,1)!=0)return HookResult.Stop;
        try
        {
            Core.Scheduler.NextWorldUpdate(()=>
            {
                try
                {
                    var actor=Core.PlayerManager.GetPlayer(slot);
                    if(!amountRequests.TryGetValue(slot,out var current)||!ReferenceEquals(current,request)||!CurrentAmount(actor,request))return;
                    RefreshSettings();
                    if(request.Period!=(bank?.Period??0)||!(request.Admin?module.CanAdmin(actor!):CanUse(actor!)))
                    {amountRequests.TryRemove(new KeyValuePair<int,AmountRequest>(slot,request));actor!.SendLplkChat("[LPLK] Berechtigung oder Bankperiode geändert. Menü neu öffnen.");return;}
                    if(input.Equals("abbrechen",StringComparison.OrdinalIgnoreCase)||input.Equals("cancel",StringComparison.OrdinalIgnoreCase))
                    {amountRequests.TryRemove(new KeyValuePair<int,AmountRequest>(slot,request));request.Back(actor!);return;}
                    Guard(actor!,()=>request.Accept(actor!,input));
                    if(!ReferenceEquals(Core.MenusAPI.GetCurrentMenu(actor!),request.Menu))amountRequests.TryRemove(new KeyValuePair<int,AmountRequest>(slot,request));
                }
                finally{Interlocked.Exchange(ref request.Busy,0);}
            });
        }
        catch{Interlocked.Exchange(ref request.Busy,0);throw;}
        return HookResult.Stop;
    }
}
