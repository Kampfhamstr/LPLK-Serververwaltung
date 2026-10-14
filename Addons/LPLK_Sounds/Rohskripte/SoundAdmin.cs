using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace LPLK_Sounds;

public sealed partial class Plugin
{
    public const string SoundOverridePermission = "lplk.sounds.override";

    private bool CanOverrideSound(IPlayer player) =>
        player is { IsValid: true, IsAuthorized: true, IsFakeClient: false } && player.SteamID != 0 &&
        (Core.Permission.PlayerHasPermission(player.SteamID, SoundOverridePermission) || Core.Permission.PlayerHasPermission(player.SteamID,"deatheffects.sound.override") || Core.Permission.PlayerHasPermission(player.SteamID,"lplk.admin"));

    private bool RequireSoundOverride(IPlayer player, Action<string>? reply)
    {
        if (CanOverrideSound(player)) return true;
        reply?.Invoke("[LPLK Sounds] Keine Berechtigung fuer Admin-Override: " + SoundOverridePermission);
        return false;
    }

    private bool RequireSoundAccess(IPlayer player, string id, Action<string>? reply = null)
    {
        if (SoundAccess.CanStart(catalog,id,false) || CanOverrideSound(player)) return true;
        reply?.Invoke("[LPLK Sounds] Diese Ansage ist nur fuer Admins: " + SoundOverridePermission);
        return false;
    }

    [Command("adminsounds", registerRaw:true, helpText:"Eigener Bereich fuer Admin-Ansagen: adminsounds [s<Seite>]")]
    public void AdminSoundsCommand(ICommandContext context) => AdminSoundCommand(context);

    [Command("adminsound", registerRaw:true, helpText:"Admin-Ansage per globaler Nummer: adminsound [Nummer|s<Seite>]")]
    public void AdminSoundCommand(ICommandContext context)
    {
        if (AuthenticatedPlayer(context) is not { } player || !RequireSoundOverride(player,context.Reply)) return;
        if(context.Args.Length==0) { OpenPlayerMenu(player,()=>ShowSoundMenu(player,adminOnly:true));return; }
        if(context.Args.Length==1 && SoundMenuPolicy.TryPageToken(context.Args[0],out int page))
        {
            CommandAction(context,()=>
            {
                int pages=SoundMenuPolicy.Page(SoundList(adminOnly:true),0).Count;
                if(page>pages) { context.Reply($"[LPLK Sounds] Admin-Seiten: 1 bis {pages}.");return; }
                OpenPlayerMenu(player,()=>ShowSoundMenu(player,page-1,adminOnly:true));
            });
            return;
        }
        if(context.Args.Length!=1 || !SoundMenuPolicy.TryNumber(context.Args[0],out int number))
        { context.Reply("[LPLK Sounds] !adminsounds: Adminliste; !adminsound 70: Ansage 70; !adminsound s2: Seite 2.");return; }
        CommandAction(context,()=>
        {
            string? id=NumberedSounds.Resolve(catalog,number);
            if(id is null || !catalog.Sounds[id].AdminOnly) { context.Reply("[LPLK Sounds] Keine Admin-Ansage mit dieser Nummer. Liste: !adminsounds");return; }
            BroadcastSound(player,id,context.Reply);
        });
    }

    [Command("soundall", registerRaw:true, helpText:"Admin: soundall [Nummer|s<Seite>|stop] – persoenliche Hoerfilter umgehen")]
    public void SoundAllCommand(ICommandContext context)
    {
        if (AuthenticatedPlayer(context) is not { } player) return;
        if (!RequireSoundOverride(player, context.Reply)) return;
        if (context.Args.Length == 0)
        { OpenPlayerMenu(player, () => ShowSoundMenu(player, adminOverride:true)); return; }
        if (context.Args.Length == 1 && context.Args[0].Equals("stop", StringComparison.OrdinalIgnoreCase))
        { CommandAction(context, () => StopAdminSound(player, context.Reply)); return; }
        if (context.Args.Length == 1 && SoundMenuPolicy.TryPageToken(context.Args[0], out int page))
        {
            CommandAction(context, () =>
            {
                int pages = SoundMenuPolicy.Page(NumberedSounds.List(catalog), 0).Count;
                if (page > pages) { context.Reply($"[LPLK Sounds] Verfuegbare Soundseiten: 1 bis {pages}."); return; }
                OpenPlayerMenu(player, () => ShowSoundMenu(player, page - 1, adminOverride:true));
            });
            return;
        }
        if (context.Args.Length != 1 || !SoundMenuPolicy.TryNumber(context.Args[0], out int number))
        { context.Reply("[LPLK Sounds] !soundall 16: Sound 16 fuer alle. !soundall s6: Seite 6. !soundall stop: Adminsound stoppen."); return; }
        CommandAction(context, () =>
        {
            string? id = NumberedSounds.Resolve(catalog, number);
            if (id is null) { context.Reply($"[LPLK Sounds] Soundnummer {number} fehlt im aktuellen Katalog."); return; }
            BroadcastSound(player, id, context.Reply, adminOverride:true);
        });
    }

    private void StopAdminSound(IPlayer player, Action<string> reply)
    {
        if (!RequireSoundOverride(player, reply)) return;
        if (broadcast?.AdminOverride != true) { reply("[LPLK Sounds] Kein Adminsound aktiv."); return; }
        StopBroadcast();
        reply("[LPLK Sounds] Adminsound fuer alle gestoppt. Rundenlimit und Cooldown bleiben erhalten.");
    }
}
