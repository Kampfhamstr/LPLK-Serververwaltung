using System.Text.Json;
using LPLK.Common;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace LPLK_Weapons;
public sealed partial class Plugin
{
    private void AmmoProbe(ICommandContext ctx)
    {
        if(ctx.IsSentByPlayer||ctx.Sender is not null||!nativeReady||ammoLimits.Count>0||settings.Rules.Count>0||Core.PlayerManager.GetAllPlayers().Any(p=>p.IsValid&&!p.IsFakeClient)){ctx.Reply("[LPLK] Leerer isolierter Test ohne aktive Waffenregeln erforderlich.");return;}
        string authorization=Path.Combine(Core.PluginDataDirectory,"ammo-test-authorization.json");
        if(!File.Exists(authorization)||new FileInfo(authorization).Length>2048){ctx.Reply("[LPLK] Einmalige Testfreigabe fehlt.");return;}
        using var proof=JsonDocument.Parse(File.ReadAllText(authorization));var expires=proof.RootElement.GetProperty("expires").GetDateTimeOffset();
        if(proof.RootElement.GetProperty("server").GetString()!="89450e53-b4c5-4511-a4f6-82696809154b"||expires<DateTimeOffset.UtcNow||expires>DateTimeOffset.UtcNow.AddMinutes(5)){ctx.Reply("[LPLK] Testfreigabe ungültig.");return;}
        File.Delete(authorization);bool success=false,restored=false;string? failure=null;int oldClip=0,oldAmmo=0,clip=-1,ammo=-1;int errorsBefore=errors;
        void Stage(string phase)=>AtomicJson.Write(Path.Combine(Core.PluginDataDirectory,"ammo-probe.json"),new{at=DateTimeOffset.UtcNow,version="0.2.4",success=false,phase,nativeTestInProgress=true});
        try
        {
            // Use the framework's weapon definition lookup. A synthetic unowned
            // weapon's DispatchSpawn crashed beta.10 before any ammo code ran.
            // Client inventory/refill is deliberately a separate acceptance test.
            Stage("lookup-definition");var data=Core.Helpers.GetWeaponCSDataFromKey(7);
            if(data is not {IsValid:true})throw new InvalidOperationException("Weapon definition unavailable");
            oldClip=data.MaxClip1;oldAmmo=data.PrimaryReserveAmmoMax;
            var rule=new WeaponRule{Clip=17,Ammo=91};Stage("apply-limits");ApplyAmmoDefinition(data,rule);
            if(data.MaxClip1!=17||data.PrimaryReserveAmmoMax!=91)throw new InvalidOperationException("Native limits mismatch");
            clip=data.MaxClip1;ammo=data.PrimaryReserveAmmoMax;
            Stage("restore");ResetAmmo();restored=data.MaxClip1==oldClip&&data.PrimaryReserveAmmoMax==oldAmmo;if(!restored||errors!=errorsBefore)throw new InvalidOperationException("Native restore mismatch");success=true;
        }
        catch(Exception e){failure=e.GetType().Name;}
        finally{Stage("cleanup");ResetAmmo();}
        string assemblySha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(Core.PluginPath,"LPLK_Weapons.dll"))));
        AtomicJson.Write(Path.Combine(Core.PluginDataDirectory,"ammo-probe.json"),new{at=DateTimeOffset.UtcNow,version="0.2.4",assemblySha256,success,failure,oldClip,oldAmmo,clip,ammo,restored,errorsBefore,errorsAfter=errors,nativeRefillTested=false,clientWeaponFireTested=false,clientSwitchTested=false});
        WriteStatus();ctx.Reply(success?"[LPLK] Native Magazin-/Reservelimits und Rücksetzung bestätigt.":"[LPLK] Waffenprüfung fehlgeschlagen.");
    }
}

