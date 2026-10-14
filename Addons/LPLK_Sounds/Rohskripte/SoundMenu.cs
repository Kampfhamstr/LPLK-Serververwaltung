using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using LPLK.Common;

namespace LPLK_Sounds;

public sealed partial class Plugin
{
    private SoundNumberStore? soundNumbers;
    private string? soundNumberError;

    private void RefreshSoundNumbers()
    {
        try
        {
            soundNumbers ??= new SoundNumberStore(Path.Combine(Core.PluginDataDirectory, "sound-numbers.json"));
            soundNumbers.Update(catalog);
            soundNumberError = null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            soundNumberError = "Soundnummern nicht verfuegbar; Serverlog pruefen. Abspielen per ID bleibt mit !lplk_sound_play <id> moeglich.";
            Logger.LogWarning(ex, "Soundnummern konnten nicht dauerhaft geladen/gespeichert werden; keine neuen Nummern vergeben.");
        }
    }

    private SoundNumberStore NumberedSounds => soundNumberError is null && soundNumbers is not null
        ? soundNumbers : throw new InvalidOperationException(soundNumberError ?? "Soundnummern noch nicht bereit.");

    private NumberedSound[] SoundList(bool adminOnly=false,bool includeAll=false) => NumberedSounds.List(catalog)
        .Where(s=>includeAll || catalog.Sounds[s.Id].AdminOnly==adminOnly).ToArray();

    [Command("sound", registerRaw:true, helpText:"sound <Nummer> spielt; sound s<Seite> blaettert; ohne Argument das Menue")]
    public void SoundMenuCommand(ICommandContext context)
    {
        if(AuthenticatedPlayer(context) is not { } player)return;
        if(context.Args.Length==0) { OpenPlayerMenu(player,()=>ShowSoundMenu(player)); return; }
        if(context.Args.Length==1 && SoundMenuPolicy.TryPageToken(context.Args[0],out int page))
        { OpenSoundPage(context,player,page); return; }
        if(context.Args.Length!=1 || !SoundMenuPolicy.TryNumber(context.Args[0],out int number))
        { context.Reply("[LPLK Sounds] !sound 51 spielt Sound 51; !sound s6 oeffnet Seite 6. Liste: !sounds"); return; }
        CommandAction(context,()=>
        {
            string? id=NumberedSounds.Resolve(catalog,number);
            if(id is null) { context.Reply($"[LPLK Sounds] Soundnummer {number} ist nicht im aktuellen Katalog. Liste: !sounds"); return; }
            BroadcastSound(player,id,context.Reply);
        });
    }

    [Command("sounds", registerRaw:true, helpText:"Soundliste mit zehn Sounds pro Seite: sounds [Seite]")]
    public void SoundListCommand(ICommandContext context)
    {
        if(AuthenticatedPlayer(context) is not { } player)return;
        if(context.Args.Length==0) { OpenPlayerMenu(player,()=>ShowSoundMenu(player)); return; }
        if(context.Args.Length!=1 || !SoundMenuPolicy.TryNumber(context.Args[0],out int number))
        { context.Reply("[LPLK Sounds] Liste: !sounds. Seite 6: !sound s6. Direkt abspielen: !sound 51"); return; }
        OpenSoundPage(context,player,number);
    }

    private void OpenSoundPage(ICommandContext context,IPlayer player,int number)
    {
        CommandAction(context,()=>
        {
            int pages=SoundMenuPolicy.Page(SoundList(),0).Count;
            if(number>pages) { context.Reply($"[LPLK Sounds] Verfuegbare Soundseiten: 1 bis {pages}."); return; }
            OpenPlayerMenu(player,()=>ShowSoundMenu(player,number-1));
        });
    }

    private void ShowSoundMenu(IPlayer player,int pageIndex=0,int selectedIndex=0,bool adminOverride=false,bool adminOnly=false)
    {
        if((adminOverride||adminOnly) && !RequireSoundOverride(player,m=>Reply(player,m)))return;
        var page=SoundMenuPolicy.Page(SoundList(adminOnly,includeAll:adminOverride&&!adminOnly),pageIndex);
        string titlePrefix=adminOnly?(adminOverride?"ANSAGEN: ALLE":"Admin-Ansagen"):(adminOverride?"ADMIN: ALLE":"Sounds");
        var owner=new OwnedMenu(player.Slot,player.SteamID) { SoundTitle = $"{titlePrefix} {page.Index+1}/{page.Count}" };
        var builder=MenuBuilder(SoundLimitTitle(owner))
            // Ten sounds per logical page. Swiftly scrolls them within the viewport
            // from core.jsonc (1-5 rows); page size is not a visible-row setting.
            .Design.SetGlobalScrollStyle(MenuOptionScrollStyle.LinearScroll)
            .Design.SetCommentVisible(false);
        foreach(var entry in page.Items)
        {
            bool ready=precachedSounds.Contains(entry.Id);
            string title=string.IsNullOrWhiteSpace(entry.Title)?entry.Id:entry.Title;
            Button(builder,owner,MenuPolicy.Label(title)+(ready?"":" (nicht bereit)"),p=>
            {
                if (!RequireSoundAccess(p,entry.Id,m=>Reply(p,m))) return;
                if ((adminOverride || adminOnly) && !RequireSoundOverride(p,m=>Reply(p,m))) return;
                ShowSoundAudienceChoice(p,entry,page.Index,owner.Menu!.GetCurrentOptionIndex(p),adminOverride,adminOnly);
            },soundNumber:entry.Number);
        }
        if(page.Items.Count==0)builder.AddOption(new TextMenuOption("Noch keine Sounds vorhanden."));
        if(page.Index>0)BackButton(builder,owner,"Zurueck - Seite "+page.Index,p=>ShowSoundMenu(p,page.Index-1,adminOverride:adminOverride,adminOnly:adminOnly));
        if(page.Index+1<page.Count)Button(builder,owner,"Weiter - Seite "+(page.Index+2),p=>ShowSoundMenu(p,page.Index+1,adminOverride:adminOverride,adminOnly:adminOnly));
        if(adminOverride)
        {
            Button(builder,owner,"Stoppen",p=>StopAdminSound(p,m=>Reply(p,m)));
            if(adminOnly)BackButton(builder,owner,"Ansagen",p=>ShowSoundMenu(p,page.Index,adminOnly:true));
            else BackButton(builder,owner,"Soundliste",p=>ShowSoundMenu(p,page.Index));
        }
        else if(adminOnly)
        {
            Button(builder,owner,"Override",p=>ShowSoundMenu(p,page.Index,adminOverride:true,adminOnly:true));
            BackButton(builder,owner,"Soundliste",p=>ShowSoundMenu(p));
        }
        else Button(builder,owner,"Einstellungen",p=>ShowSoundSettings(p,page.Index,owner.Menu!.GetCurrentOptionIndex(p)));
        OpenOwnedMenu(player,owner,builder);
        if(selectedIndex>0)owner.Menu!.MoveToOptionIndex(player,Math.Min(selectedIndex,owner.Menu.Options.Count-1));
    }

    private void ShowSoundAudienceChoice(IPlayer player,NumberedSound entry,int returnPage,int returnIndex,bool returnOverride,bool returnAdminOnly)
    {
        if (!RequireSoundAccess(player,entry.Id,m=>Reply(player,m))) return;
        var owner=new OwnedMenu(player.Slot,player.SteamID);
        var builder=MenuBuilder("An alle?");
        void Back(IPlayer p)=>ShowSoundAudienceChoice(p,entry,returnPage,returnIndex,returnOverride,returnAdminOnly);
        Button(builder,owner,"Ja",p=>
        {
            if (!RequireSoundAccess(p,entry.Id,m=>Reply(p,m))) return;
            if (CanOverrideSound(p)) ShowSoundStartChoice(p,entry,returnPage,returnIndex,returnOverride,returnAdminOnly);
            else if (BroadcastSound(p,entry.Id,m=>Reply(p,m)))
                ShowSoundMenu(p,returnPage,returnIndex);
        },$"{entry.Number}. {MenuPolicy.Label(entry.Title)} öffentlich abspielen.");
        Button(builder,owner,"Nein",p=>ShowSoundRecipients(p,entry,0,Back),
            "Empfänger auswählen. Private Sperren, Hörfilter und die Totenregel gelten immer.");
        BackButton(builder,owner,"Zurück",p=>ShowSoundMenu(p,returnPage,returnIndex,
            adminOverride:returnOverride && CanOverrideSound(p),adminOnly:returnAdminOnly && CanOverrideSound(p)));
        OpenOwnedMenu(player,owner,builder);
    }

    private void ShowSoundStartChoice(IPlayer player,NumberedSound entry,int returnPage,int returnIndex,bool returnOverride,bool returnAdminOnly)
    {
        if (!RequireSoundOverride(player,m=>Reply(player,m))) return;
        var owner = new OwnedMenu(player.Slot,player.SteamID) { SoundTitle = $"{entry.Number}. {MenuPolicy.Label(entry.Title)}" };
        var builder = MenuBuilder("Override?");
        void ReturnToList(IPlayer p) => ShowSoundMenu(p,returnPage,returnIndex,
            adminOverride:returnOverride && CanOverrideSound(p),adminOnly:returnAdminOnly && CanOverrideSound(p));
        Button(builder,owner,"Nein",p=>
        {
            if (BroadcastSound(p,entry.Id,m=>Reply(p,m))) ReturnToList(p);
        },"Beachtet persönliche Hörfilter, Lautstärke und die Totenregel.");
        Button(builder,owner,"Ja",p=>
        {
            if (BroadcastSound(p,entry.Id,m=>Reply(p,m),adminOverride:true)) ReturnToList(p);
        },"Alle verbundenen Spieler hören den Sound trotz Hörfiltern bei 100% Kataloglautstärke.");
        BackButton(builder,owner,"Zurück",p=>ShowSoundAudienceChoice(p,entry,returnPage,returnIndex,returnOverride,returnAdminOnly));
        OpenOwnedMenu(player,owner,builder);
    }

    private void ShowSoundSettings(IPlayer player,int returnPage,int returnIndex,int selectedIndex=0)
    {
        var owner=new OwnedMenu(player.Slot,player.SteamID) { SoundTitle = "Sound-Einstellungen" };
        var builder=MenuBuilder(SoundLimitTitle(owner));
        builder.AddOption(new TextMenuOption("Totenregel: "+(config.DeadPlayersOnlyToDead?"AN":"AUS")));
        var preference=Listening(player);
        Button(builder,owner,"Hören: "+(preference.Enabled?"AN":"AUS"),p=>
        {
            int index=owner.Menu!.GetCurrentOptionIndex(p);
            SetListening(p,!Listening(p).Enabled,m=>Reply(p,m));ShowSoundSettings(p,returnPage,returnIndex,index);
        },"Nur für dich. AUS bleibt gespeichert. Admin-Override ist ausgenommen.",enabled:preference.Enabled);
        Button(builder,owner,$"Lautstärke: {preference.VolumePercent}%",p=>ShowSoundVolumeMenu(p,returnPage,returnIndex));
        Button(builder,owner,"Lebend: "+(preference.HearAlive?"AN":"AUS"),p=>
        {
            int index=owner.Menu!.GetCurrentOptionIndex(p);
            SetSoundState(p,true,!Listening(p).HearAlive);ShowSoundSettings(p,returnPage,returnIndex,index);
        },"Bezieht sich auf deinen Zustand als Zuhörer, unabhängig vom Absender.",enabled:preference.HearAlive);
        Button(builder,owner,"Tot: "+(preference.HearDead?"AN":"AUS"),p=>
        {
            int index=owner.Menu!.GetCurrentOptionIndex(p);
            SetSoundState(p,false,!Listening(p).HearDead);ShowSoundSettings(p,returnPage,returnIndex,index);
        },enabled:preference.HearDead);
        Button(builder,owner,$"Einzelfilter ({catalog.Sounds.Keys.Count(preference.MutedSounds.Contains)} stumm)",
            p=>ShowSoundMuteMenu(p,returnPage,returnIndex),"Hier einzeln erlauben oder stummschalten. Die Auswahl spielt keinen Sound ab.");
        Button(builder,owner,"Lokal stoppen",p=>
        {
            Reply(p,StopLocalSounds(p.Slot)?"[LPLK Sounds] Sound nur bei dir gestoppt.":"[LPLK Sounds] Eigene Sounds gestoppt; der Admin-Override laeuft weiter.");
        },"Andere Zuhörer hören weiter. Ein Admin-Override läuft weiter.");
        if(CanOverrideSound(player))
        {
            if(catalog.Sounds.Values.Any(s=>s.AdminOnly))
                Button(builder,owner,"Ansagen",p=>ShowSoundMenu(p,adminOnly:true),"Nur für Admins abrufbar. Für alle trotz Hörfilter: dort den Override wählen.");
            Button(builder,owner,"Override",p=>ShowSoundMenu(p,returnPage,adminOverride:true),
                "Nach der Soundauswahl normal oder als Admin-Override für alle abspielen.");
        }
        Button(builder,owner,"Senden",p=>ShowSendSoundMenu(p),"Sound und Empfänger wählen; ohne Admin-Override.");
        Button(builder,owner,"Absender sperren",p=>ShowSoundSenderBlocks(p),"Die Sperre gilt für private Sounds dieses Absenders und bleibt gespeichert.");
        Button(builder,owner,"Hilfe",p=>ShowSoundStatus(p,returnPage,returnIndex));
        BackButton(builder,owner,"Soundliste",p=>ShowSoundMenu(p,returnPage,returnIndex));
        OpenOwnedMenu(player,owner,builder);
        if(selectedIndex>0)owner.Menu!.MoveToOptionIndex(player,Math.Min(selectedIndex,owner.Menu.Options.Count-1));
    }

    private void ShowSoundVolumeMenu(IPlayer player,int returnPage,int returnIndex)
    {
        var owner=new OwnedMenu(player.Slot,player.SteamID);
        var builder=MenuBuilder($"Deine Lautstärke: {Listening(player).VolumePercent}%");
        foreach(int volume in new[]{0,10,25,50,75,100})
            Button(builder,owner,(Listening(player).VolumePercent==volume?"[x] ":"")+volume+"%"+(volume==0?" (stumm)":""),p=>
            {
                SetSoundVolume(p,volume,m=>Reply(p,m));ShowSoundSettings(p,returnPage,returnIndex,2);
            },"Für normale Sounds. Andere Werte: !soundvolume 40. 0% stoppt sofort; Admin-Override ist ausgenommen.");
        BackButton(builder,owner,"Zurück",p=>ShowSoundSettings(p,returnPage,returnIndex,2));
        OpenOwnedMenu(player,owner,builder);
    }

    private void ShowSoundMuteMenu(IPlayer player,int returnPage,int returnIndex,int pageIndex=0,int selectedIndex=0)
    {
        var page=SoundMenuPolicy.Page(NumberedSounds.List(catalog),pageIndex);
        var preference=Listening(player);
        var owner=new OwnedMenu(player.Slot,player.SteamID);
        var builder=MenuBuilder($"Einzeln hören {page.Index+1}/{page.Count}")
            .Design.SetGlobalScrollStyle(MenuOptionScrollStyle.LinearScroll);
        foreach(var entry in page.Items)
            Button(builder,owner,$"[{(preference.MutedSounds.Contains(entry.Id)?"STUMM":"AN")}] "+MenuPolicy.Label(entry.Title),p=>
            {
                int index=owner.Menu!.GetCurrentOptionIndex(p);
                ToggleSoundMute(p,entry.Id);ShowSoundMuteMenu(p,returnPage,returnIndex,page.Index,index);
            },"Freigabe normaler Sounds umschalten, ohne Abspielen. Admin-Override ist ausgenommen.",soundNumber:entry.Number,enabled:!preference.MutedSounds.Contains(entry.Id));
        if(page.Items.Count==0)builder.AddOption(new TextMenuOption("Noch keine Sounds vorhanden."));
        if(page.Index>0)BackButton(builder,owner,"Zurück - Seite "+page.Index,p=>ShowSoundMuteMenu(p,returnPage,returnIndex,page.Index-1));
        if(page.Index+1<page.Count)Button(builder,owner,"Weiter - Seite "+(page.Index+2),p=>ShowSoundMuteMenu(p,returnPage,returnIndex,page.Index+1));
        BackButton(builder,owner,"Einstellungen",p=>ShowSoundSettings(p,returnPage,returnIndex,5));
        OpenOwnedMenu(player,owner,builder);
        if(selectedIndex>0)owner.Menu!.MoveToOptionIndex(player,Math.Min(selectedIndex,owner.Menu.Options.Count-1));
    }

    private void ShowSoundStatus(IPlayer player,int returnPage,int returnIndex)
    {
        var owner=new OwnedMenu(player.Slot,player.SteamID) { SoundTitle = "Sounds "+PluginVersion };
        var builder=MenuBuilder(SoundLimitTitle(owner));
        builder.AddOption(new TextMenuOption($"Vorgeladen: {precachedSounds.Count}/{catalog.Sounds.Count} Sounds"));
        Button(builder,owner,"Katalog",p=>ShowCatalogMenu(p,q=>ShowSoundStatus(q,returnPage,returnIndex)));
        builder.AddOption(new TextMenuOption($"Lautstärke: {Listening(player).VolumePercent}% | Sounds: "+(Listening(player).Enabled?"AN":"AUS")));
        Button(builder,owner,"Status",p=>
        {
            Reply(p,"[LPLK Sounds] "+(CommonUnavailable()??$"Sounds vorgeladen: {precachedSounds.Count}/{catalog.Sounds.Count}."));
            Reply(p,$"[LPLK Sounds] Gemeinsame Pause zwischen Sounds: {config.BroadcastCooldownSeconds:0.#} Sekunden.");
            Reply(p,"[LPLK Sounds] " + SoundLimitStatus(p));
            Reply(p,"[LPLK Sounds] Tote spielen nur fuer Tote: "+(config.DeadPlayersOnlyToDead?"AN":"AUS")+".");
            Reply(p,"[LPLK Sounds] "+ListeningSummary(p));
        });
        Button(builder,owner,"Bedienung",p=>
        {
            Reply(p,"[LPLK Sounds] !sounds: 10 Sounds pro Seite. !sound s6 oeffnet Seite 6; !sound 6 spielt Sound 6, !sound 16 spielt Sound 16. !sound ohne Argument oeffnet ebenfalls das Menue.");
            Reply(p,"[LPLK Sounds] Die Tasten folgen SwiftlyS2 core.jsonc. Soundnummern sind fortlaufend; entfernte Sounds schliessen Luecken. Zurueck/Weiter wechselt die Seite.");
            Reply(p,"[LPLK Sounds] !soundsend <Nummer>: Sound nur an einen Spieler senden. Dessen Hoerfilter und die Totenregel gelten immer; ausgeschaltete Sounds werden dir gemeldet.");
            Reply(p,"[LPLK Sounds] Sounds hoeren AUS schaltet LPLK-Sounds-Sounds nur fuer dich stumm und bleibt gespeichert. Andere Spielsounds bleiben erhalten.");
            Reply(p,"[LPLK Sounds] Einstellungen: eigene Lautstaerke 0-100%, Hoeren lebend oder tot/Zuschauer und einzelne Sounds stummschalten. Alle Filter gelten zusammen; neue Sounds sind standardmaessig freigegeben.");
            Reply(p,"[LPLK Sounds] Ein ausdruecklicher Admin-Override geht an alle bei normaler Soundlautstaerke. Persoenliche Hoerfilter und die Totenregel sind dabei ausgenommen; gespeicherte Einstellungen bleiben erhalten.");
            Reply(p,"[LPLK Sounds] Ein neuer oeffentlicher Sound ersetzt den vorherigen. Waehrend der Pause meldet das Menue die verbleibende Zeit.");
            Reply(p,"[LPLK Sounds] Spieler ohne Adminberechtigung koennen hoechstens drei Sounds pro Runde oeffentlich starten. Admins haben kein Rundenlimit. Fehlversuche kosten keinen Sound. Neue Runde: wieder drei.");
            Reply(p,"[LPLK Sounds] Ist 'Tote nur fuer Tote' AN, hoeren lebende Spieler keine neu von Toten oder Zuschauern gestarteten Sounds. Lebende spielen fuer alle eingeschalteten Zuhoerer.");
        });
        Button(builder,owner,"Aktualisieren",p=>ShowSoundStatus(p,returnPage,returnIndex));
        BackButton(builder,owner,"Zurueck",p=>ShowSoundSettings(p,returnPage,returnIndex));
        OpenOwnedMenu(player,owner,builder);
    }
    private string SoundLimitStatus(IPlayer player) => CanOverrideSound(player)
        ? "Sounds in dieser Runde: unbegrenzt (Admin)."
        : $"Noch {soundRoundLimit.Remaining(player.SteamID)}/{SoundRoundLimit.PerPlayer} Sounds in dieser Runde.";
    private string SoundLimitTitle(OwnedMenu owner)
    {
        var player = Core.PlayerManager.GetPlayer(owner.Slot);
        return player is not null && player.SteamID == owner.Steam && CanOverrideSound(player)
            ? $"{owner.SoundTitle} | unbegrenzt (Admin)"
            : $"{owner.SoundTitle} | noch {soundRoundLimit.Remaining(owner.Steam)}/{SoundRoundLimit.PerPlayer}";
    }
    private void RefreshSoundLimitTitles()
    {
        foreach (var owner in ownedMenus.Values)
            if (owner.SoundTitle is not null && owner.Menu is not null) owner.Menu.Configuration.Title = MenuColors.Heading(SoundLimitTitle(owner));
    }
}
