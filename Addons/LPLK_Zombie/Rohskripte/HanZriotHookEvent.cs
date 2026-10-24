using SwiftlyS2.Shared.GameHooks;
// Derived from H-AN/HanZombieRiotS2 v3.2; GPL-3.0. See LICENSE and PROVENANCE.json.
using System.Numerics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace LPLK_Zombie;

public class HanZriotEvents : IDisposable
{
    private readonly ILogger<HanZriotEvents> _logger;
    private readonly ISwiftlyCore _core;
    private readonly IOptionsMonitor<HanZriotCFG> _mainConfig;
    private readonly IStageConfigProvider _dayConfig;
    private readonly IZombieConfigProvider _zombieConfig;
    private readonly HanZriotHelpers _helpers;
    private readonly HanZriotGlobals _globals;
    private readonly HanZriotHud _hud;
    private readonly HanZriotService _services;
    public HanZriotEvents(ISwiftlyCore core, ILogger<HanZriotEvents> logger,
        IOptionsMonitor<HanZriotCFG> mainConfig,
        IStageConfigProvider dayConfig,
        IZombieConfigProvider zombieConfig,
        HanZriotHelpers helpers, HanZriotGlobals globals,
        HanZriotHud hud, HanZriotService Service)
    {
        _core = core;
        _logger = logger;
        _mainConfig = mainConfig;
        _dayConfig = dayConfig;
        _zombieConfig = zombieConfig;
        _helpers = helpers;
        _globals = globals;
        _hud = hud;
        _services = Service;
    }

    private readonly List<Guid> _hooks=[];
    private bool _hooked;
    public void HookEvents()
    {
        if(_hooked)return;_hooked=true;
        // GameEvent Hook
        _hooks.Add(_core.GameEvent.HookPre<EventRoundStart>(OnRoundStart));
        _hooks.Add(_core.GameEvent.HookPre<EventPlayerHurt>(OnPlayerHurt));
        _hooks.Add(_core.GameEvent.HookPre<EventPlayerDeath>(OnPlayerDeath));
        _hooks.Add(_core.GameEvent.HookPre<EventPlayerSpawn>(OnPlayerSpawn));
        _hooks.Add(_core.GameEvent.HookPre<EventWeaponFire>(OnWeaponFire));


        // Event 系列 Hook
        _core.Event.OnPrecacheResource += Event_OnPrecacheResource;
        _core.Event.OnMapUnload += Event_MapEnd;
        _core.Event.OnClientDisconnected += Event_OnClientDisconnected;
        _core.GameHooks.Entities.TakeDamage.Pre += Damage;
        _core.Event.OnTick += Event_OnTick;
        _core.GameHooks.Weapons.CanUse.Pre += CanUse;

    }


    public void Dispose()
    {
        if(!_hooked)return;_hooked=false;
        foreach(var hook in _hooks)_core.GameEvent.Unhook(hook);_hooks.Clear();
        _core.Event.OnPrecacheResource -= Event_OnPrecacheResource;
        _core.Event.OnMapUnload -= Event_MapEnd;
        _core.Event.OnClientDisconnected -= Event_OnClientDisconnected;
        _core.GameHooks.Entities.TakeDamage.Pre -= Damage;
        _core.Event.OnTick -= Event_OnTick;
        _core.GameHooks.Weapons.CanUse.Pre -= CanUse;
    }
    private void CanUse(ref CanUseWeaponPreContext context)
    {
        if(!_globals.Active||!_globals.Ready)return;
        var player=context.Params.Player;
        if(player is not {IsValid:true}||player.Controller is not {IsValid:true} controller)return;
        string? name=context.Params.Weapon?.Entity?.DesignerName;
        if(controller.TeamNum==2&&name is not null&&name!="weapon_knife")context.SetReturn(false);
    }
    private void Damage(ref TakeDamageEntityPreContext context)
    {
        if(!_globals.Active||!_globals.Ready)return;
        var victim=context.Params.Entity;
        if(victim is not {IsValid:true}||victim.DesignerName!="player")return;
        var pawn=victim.As<CCSPlayerPawn>();
        var vc=pawn.Controller.Value?.As<CCSPlayerController>();
        if(vc is not {IsValid:true})return;
        var vp=_core.PlayerManager.GetPlayerFromController(vc);
        if(vp is not {IsValid:true}||vp.PlayerID is <0 or >=65)return;
        if(vc.TeamNum==3&&_globals.InProtect[vp.PlayerID]||vc.TeamNum==2&&!_globals.GameStart){context.Params.Info.Damage=0;return;}
        var attacker=context.Params.Info.Attacker.Value;
        if(attacker is not {IsValid:true})return;
        CCSPlayerController? ac=attacker.DesignerName switch{"player"=>attacker.As<CCSPlayerPawn>().Controller.Value?.As<CCSPlayerController>(),"cs_player_controller"=>attacker.As<CCSPlayerController>(),_=>null};
        if(ac is not {IsValid:true}||ac.TeamNum!=2||vc.TeamNum!=3)return;
        var kind=_zombieConfig.GetConfig().ZombieList.FirstOrDefault(z=>z.Name==ac.Entity?.Name);
        if(kind is null)return;
        context.Params.Info.Damage+=kind.Damage;
        vp.SendMessage(MessageType.Chat,$"{_core.Translation.GetPlayerLocalizer(vp)["ZombieDamage",ac.PlayerName,vc.PlayerName,context.Params.Info.Damage]}");
    }

    private void Event_OnTick()
    {
        if(!_globals.Active||!_globals.Ready)return;

        var gameRules = _core.EntitySystem.GetGameRules();
        if (gameRules is not { IsValid: true, WarmupPeriod: false })
            return;

        gameRules.GameRestart = gameRules.RestartRoundTime.Value < _core.Engine.GlobalVars.CurrentTime;
    }

    private void Event_MapEnd(IOnMapUnloadEvent @event)
    {
        _globals.Ready=false;_globals.Scheduler.Advance();
        _globals.RiotDay = 1;
        _core.Engine.ExecuteCommand("bot_quota 0");
    }

    private HookResult OnRoundStart(EventRoundStart @event)
    {
        if(!_globals.Active||!_globals.Ready)return HookResult.Continue;
        _globals.Scheduler.Advance();

        _core.Engine.ExecuteCommand("bot_quota_mode fill");
        _core.Engine.ExecuteCommand("bot_quota 20");

        _globals.Scheduler.DelayBySeconds(1.0f, () =>
        {
            _helpers.GlobalHudTimer();
            _helpers.ZombieRegenTimer();
            _helpers.HumanDeathCountDown();
        });


        var allPlayers = _core.PlayerManager.GetAllPlayers();

        var currentDay = _helpers.GetCurrentDay(_globals.RiotDay);

        _globals.NeedKillZombie = currentDay.Count;
        _globals.ZombieKill = 0;


        _globals.SpawnAllZombie?.Cancel();
        _globals.SpawnAllZombie = null;
        _globals.SpawnAllZombie = _globals.Scheduler.DelayAndRepeatBySeconds(0.5f, 5.0f, () =>
        {
            _helpers.ChangeBotTeam();
            _helpers.RespawnAllZombie();
        });
        _globals.Scheduler.StopOnMapChange(_globals.SpawnAllZombie);



        _globals.g_DeathCheck?.Cancel();
        _globals.g_DeathCheck = null;
        _globals.g_DeathCheck = _globals.Scheduler.RepeatBySeconds(0.3f, () =>
        {
            _services.CheckHumanAlive();
        });
        _globals.Scheduler.StopOnMapChange(_globals.g_DeathCheck);

        var CFG = _mainConfig.CurrentValue;
        if (CFG.SoundRoundstartMusic && !string.IsNullOrWhiteSpace(CFG.SoundEventRoundstartMusic))
        {
            var RoundstartMusic = _helpers.RandomSelectSound(CFG.SoundEventRoundstartMusic);
            if (RoundstartMusic != null)
            {
                _helpers.EmitSoundToAll(RoundstartMusic);
            }
        }
        if (CFG.SoundAmbSound && !string.IsNullOrWhiteSpace(CFG.SoundEventAmbSound))
        {
            _globals.g_hAmbMusic?.Cancel();
            _globals.g_hAmbMusic = null;
            _globals.g_hAmbMusic = _globals.Scheduler.DelayAndRepeatBySeconds(0.1f, CFG.AmbSoundLoopTime, () => _helpers.PlayAmbSound(CFG.SoundEventAmbSound));
            _globals.Scheduler.StopOnMapChange(_globals.g_hAmbMusic);
        }


        if (CFG.FreezeZombie > 0)
        {
            _globals.Countdown = (int)Math.Ceiling(CFG.FreezeZombie);

            _globals.g_hCountdown?.Cancel();
            _globals.g_hCountdown = null;
            _globals.g_hCountdown = _globals.Scheduler.DelayAndRepeatBySeconds(0.2f, 1.0f, () => _services.Round_Countdown());
            _globals.Scheduler.StopOnMapChange(_globals.g_hCountdown);
        }
        else
        {
            _globals.GameStart = true;

            _helpers.SetAllZombieUnFreeze();

            foreach (var player in allPlayers)
            {
                if (player == null || !player.IsValid)
                    continue;

                if (!player.IsFakeClient)
                    continue; 

                var pawn = player.PlayerPawn;
                if (pawn == null || !pawn.IsValid)
                    continue;

                var team = pawn.TeamNum;
                if (team == 3)
                {
                    player.SendMessage(MessageType.CenterHTML, $"{_core.Translation.GetPlayerLocalizer(player)["ZombieStartMove"]}");
                }
            }
            if (CFG.SoundZombieStart && !string.IsNullOrWhiteSpace(CFG.SoundEventZombieStart))
            {
                var ZombieStart = _helpers.RandomSelectSound(CFG.SoundEventZombieStart);
                if (ZombieStart != null)
                {
                    _helpers.EmitSoundToAll(ZombieStart);
                }
                
            }
        }

        foreach (var player in allPlayers)
        {
            if (player == null || !player.IsValid)
                continue;

            var pawn = player.PlayerPawn;
            if (pawn == null || !pawn.IsValid)
                continue;

            var team = pawn.TeamNum;

            var slot = player.PlayerID;

            if (player.IsFakeClient)
            {
                if (team == 2)
                {
                    if (_globals.Countdown > 0)
                    {
                        _helpers.SetFreezeState(player, true);
                        _globals.Scheduler.DelayBySeconds((float)_globals.Countdown, () =>
                        {
                            _helpers.SetFreezeState(player, false);
                        });
                    }
                }
            }
            else
            {
                if (_globals.AllowHumanZombie)
                {
                    _globals.Scheduler.DelayBySeconds(1f, _globals.Scheduler.ForPlayer(player, () => { _globals.BeAZombie[slot] = currentDay.BeforeZombie; }));
                }
            }
            _globals.g_DeadCountDown[slot]?.Cancel();
            _globals.g_DeadCountDown[slot] = null;
        }

        _helpers.RemoveRoundObjective();

        return HookResult.Continue;
    }

    private HookResult OnPlayerHurt(EventPlayerHurt @event)
    {
        if(!_globals.Active||!_globals.Ready)return HookResult.Continue;
        var player = @event.UserIdPlayer;
        if (player == null || !player.IsValid)
            return HookResult.Continue;

        var playerController = @event.UserIdController;
        if (playerController == null || !playerController.IsValid)
            return HookResult.Continue;

        var playerPawn = player.PlayerPawn;
        if (playerPawn == null || !playerPawn.IsValid)
            return HookResult.Continue;

        var attacker = @event.AttackerPlayer;
        if (attacker == null || !attacker.IsValid)
            return HookResult.Continue;

        var attackerPawn = attacker.PlayerPawn;
        if (attackerPawn == null || !attackerPawn.IsValid)
            return HookResult.Continue;

        var attackerController = attacker.Controller;
        if (attackerController == null || !attackerController.IsValid)
            return HookResult.Continue;

        var weapon = @event.Weapon;
        var dmgHealth = @event.ActualDmgHealth;
        var hitgroup = @event.ActualHitGroup;

        if (attackerPawn.TeamNum == 3 && playerPawn.TeamNum == 2)
        {
            int LeftZombie = _globals.NeedKillZombie - _globals.ZombieKill;
            var remainingHP = playerPawn.Health;

            var CFG = _mainConfig.CurrentValue;

            if (CFG.HurtMoney)
            {
                _helpers.GiveCash(attacker, dmgHealth, "hurt");
            }

            if (hitgroup == HitGroup_t.HITGROUP_HEAD)
            {
                if (CFG.SoundZombiePain && !string.IsNullOrWhiteSpace(CFG.SoundEventZombiePain))
                {
                    int randomPain = Random.Shared.Next(0, 5);
                    if (randomPain == 1)
                    {
                        var PainSounds = _helpers.RandomSelectSound(CFG.SoundEventZombiePain);
                        if (PainSounds != null)
                        {
                            _helpers.EmitSoundToEntity(player, PainSounds);
                        }
                    }
                }
            }
            else
            {
                if (CFG.SoundZombieHurt && !string.IsNullOrWhiteSpace(CFG.SoundEventZombieHurt))
                {
                    var HurtSounds = _helpers.RandomSelectSound(CFG.SoundEventZombieHurt);
                    if (HurtSounds != null)
                    {
                        _helpers.EmitSoundToEntity(player, HurtSounds);
                    }
                }

            }

            if (_globals.g_hCountdown == null)
            {
                if (!_globals.PlayerDmgHud[attacker.PlayerID])
                {
                    attacker.SendMessage(MessageType.Center, $"\n\n{_core.Translation.GetPlayerLocalizer(attacker)["Target", playerController.PlayerName, remainingHP]}");
                }

            }
        }
        if (attackerPawn.TeamNum == 2 && playerPawn.TeamNum == 3)
        {
            player.SendMessage(MessageType.Chat, $"{_core.Translation.GetPlayerLocalizer(player)["Attacked", attackerController.PlayerName, dmgHealth]}");
        }
        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event)
    {
        if(!_globals.Active||!_globals.Ready)return HookResult.Continue;
        var attacker = @event.AttackerPlayer;
        if (attacker == null || !attacker.IsValid)
            return HookResult.Continue;

        var Deather = @event.UserIdPlayer;
        if (Deather == null || !Deather.IsValid)
            return HookResult.Continue;

        var DeatherController = @event.UserIdController;
        if (DeatherController == null || !DeatherController.IsValid)
            return HookResult.Continue;

        var DeatherPawn = Deather.PlayerPawn;
        if (DeatherPawn == null || !DeatherPawn.IsValid)
            return HookResult.Continue;


        var Dayconfig = _dayConfig.GetConfig();

        var CFG = _mainConfig.CurrentValue;
        int maxDay = Dayconfig.Days.Count;

        _globals.Scheduler.NextTick(() =>
        {

            var HumanCount = _core.PlayerManager.GetCTAlive().Count();

            if (DeatherPawn.TeamNum == 2)
            {
                var DeatherControllerEntity = DeatherController.Entity;
                if (DeatherControllerEntity != null && DeatherControllerEntity.IsValid)
                {
                    DeatherControllerEntity.Name = "";
                }
                _globals.g_ZombieRegenStates.Remove(Deather.PlayerID);

                if (_globals.GameStart == true)
                {

                    _globals.Scheduler.DelayBySeconds(1.0f, _globals.Scheduler.ForPlayer(Deather, () => { if(!Deather.IsAlive)_helpers.RespawnClient(DeatherController); }));
                    if (CFG.DeathMoney > 0)
                    {
                        _helpers.GiveCash(attacker, CFG.DeathMoney, "death");
                    }

                    if (CFG.SoundZombieDead && !string.IsNullOrWhiteSpace(CFG.SoundEventZombieDead))
                    {
                        var DeadSounds = _helpers.RandomSelectSound(CFG.SoundEventZombieDead);
                        if (DeadSounds != null)
                        {
                            _helpers.EmitSoundToEntity(Deather, DeadSounds);
                        }
                    }
                    _globals.ZombieKill++;
                    _helpers.UpdateKillCount();
                    if (_globals.ZombieKill >= _globals.NeedKillZombie)
                    {
                        _globals.ZombieKill = 0;
                        if (_globals.RiotDay < maxDay)
                        {
                            _services.FakeCtswin();
                            _globals.RiotDay++;
                        }
                        else
                        {
                            _services.FakeCtswin();
                            _globals.RiotDay = maxDay;
                        }

                    }
                }
            }
            if (DeatherPawn.TeamNum == 3)
            {
                if (_globals.GameStart)
                {
                    if (_globals.AllowHumanZombie)
                    {
                        if (_globals.BeAZombie[Deather.PlayerID] > -1)
                        {
                            _globals.BeAZombie[Deather.PlayerID]--;
                        }
                    }

                    if (HumanCount > 0)
                    {
                        if (_globals.AllowHumanZombie)
                        {
                            if (_globals.BeAZombie[Deather.PlayerID] < 0)
                            {
                                _globals.Scheduler.DelayBySeconds(1.0f, _globals.Scheduler.ForPlayer(Deather, () => { if(!Deather.IsAlive)_helpers.RespawnClient(DeatherController); }));
                            }
                            else
                            {
                                Deather.SendMessage(MessageType.Chat, $"{_core.Translation.GetPlayerLocalizer(Deather)["DeathInfo", (int)_globals.RebornSec[Deather.PlayerID]]}");
                                var now = Environment.TickCount;
                                _globals.DeathTime[Deather.PlayerID] = now + ((int)_globals.RebornSec[Deather.PlayerID] * 1000);
                            }
                        }
                        else
                        {
                            Deather.SendMessage(MessageType.Chat, $"{_core.Translation.GetPlayerLocalizer(Deather)["DeathInfo", (int)_globals.RebornSec[Deather.PlayerID]]}");

                            var now = Environment.TickCount;
                            _globals.DeathTime[Deather.PlayerID] = now + ((int)_globals.RebornSec[Deather.PlayerID] * 1000);

                        }

                    }
                }
                else
                {
                    _globals.Scheduler.DelayBySeconds(1.0f, _globals.Scheduler.ForPlayer(Deather, () => { if(!Deather.IsAlive)DeatherController.Respawn(); }));

                }


            }


        });

        return HookResult.Continue;
    }

    public HookResult OnPlayerSpawn(EventPlayerSpawn @event)
    {
        if(!_globals.Active||!_globals.Ready)return HookResult.Continue;
        var player = @event.UserIdPlayer;
        if (player == null || !player.IsValid)
            return HookResult.Continue;

        var playerpawn = player.PlayerPawn;
        if (playerpawn == null || !playerpawn.IsValid)
            return HookResult.Continue;

        var playerController = player.Controller;
        if (playerController == null || !playerController.IsValid)
            return HookResult.Continue;

        var CFG = _mainConfig.CurrentValue;

        if (!player.IsFakeClient)
        {
            if (_globals.AllowHumanZombie)
            {
                if (_globals.BeAZombie[player.PlayerID] < 0)
                {
                    if (_globals.RiotDay != 1)
                    {
                        player.SwitchTeam(Team.T);
                        _globals.Scheduler.DelayBySeconds(0.2f, _globals.Scheduler.ForPlayer(player, () => _services.PossZombie(player)));
                        _globals.BeAZombie[player.PlayerID] = 0;
                    }
                    else
                    {
                        _globals.RebornSec[player.PlayerID] = (int)Math.Ceiling(CFG.HumanRebornSec);
                        if (playerpawn.TeamNum != 3)
                        {
                            player.SwitchTeam(Team.CT);
                        }

                        if (CFG.SpawnProtect)
                        {
                            if (playerpawn != null)
                            {
                                _globals.Scheduler.DelayBySeconds(0.2f, _globals.Scheduler.ForPlayer(player, () => { _globals.InProtect[player.PlayerID] = true; }));
                            }

                            _globals.SpawnProtect[player.PlayerID]?.Cancel();
                            _globals.SpawnProtect[player.PlayerID] = null;
                            _globals.SpawnProtect[player.PlayerID] = _globals.Scheduler.DelayBySeconds(CFG.SpawnProtectCount, _globals.Scheduler.ForPlayer(player, () => { _helpers.DeleSpawnProtect(player); }));

                            player.SendMessage(MessageType.Chat, $"{_core.Translation.GetPlayerLocalizer(player)["SpawnProtect", CFG.SpawnProtectCount]}");
                        }
                    }
                }
                else
                {
                    _globals.RebornSec[player.PlayerID] = (int)Math.Ceiling(CFG.HumanRebornSec);
                    if (playerpawn.TeamNum != 3)
                    {
                        player.SwitchTeam(Team.CT);
                    }
                }
            }
            else
            {
                _globals.RebornSec[player.PlayerID] = (int)Math.Ceiling(CFG.HumanRebornSec);
                if (playerpawn.TeamNum != 3)
                {
                    player.SwitchTeam(Team.CT);
                }

                if (CFG.SpawnProtect)
                {
                    if (playerpawn != null)
                        _globals.Scheduler.DelayBySeconds(0.2f, _globals.Scheduler.ForPlayer(player, () => { _globals.InProtect[player.PlayerID] = true; }));

                    _globals.SpawnProtect[player.PlayerID]?.Cancel();
                    _globals.SpawnProtect[player.PlayerID] = null;
                    _globals.SpawnProtect[player.PlayerID] = _globals.Scheduler.DelayBySeconds(CFG.SpawnProtectCount, _globals.Scheduler.ForPlayer(player, () => { _helpers.DeleSpawnProtect(player); }));
                    player.SendMessage(MessageType.Chat, $"{_core.Translation.GetPlayerLocalizer(player)["SpawnProtect", CFG.SpawnProtectCount]}");
                }


            }

            if (CFG.HumanNoBlock)
            {
                var pawn = player.PlayerPawn;
                if (pawn != null && pawn.IsValid)
                {
                    _globals.Scheduler.NextTick(() =>
                    {
                        _helpers.NoBlock(pawn);
                    });
                }
            }

        }
        else
        {
            if (playerpawn.TeamNum != 2)
            {
                player.SwitchTeam(Team.T);
            }
            _globals.Scheduler.DelayBySeconds(0.2f, _globals.Scheduler.ForPlayer(player, () => _services.PossZombie(player)));
            if (!_globals.GameStart)
            {
                _globals.Scheduler.DelayBySeconds(0.5f, _globals.Scheduler.ForPlayer(player, () => _helpers.SetFreezeState(player, true)));
            }
        }

        return HookResult.Continue;
    }


    private void Event_OnClientDisconnected(IOnClientDisconnectedEvent @event)
    {
        var client = @event.PlayerId;
        if(client<0||client>=65)return;

        _globals.g_DeadCountDown[client]?.Cancel();
        _globals.g_DeadCountDown[client] = null;

        _globals.g_ZombieRegenStates.Remove(client);


        _globals.SpawnProtect[client]?.Cancel();
        _globals.SpawnProtect[client] = null;

        _globals.RebornSec[client] = 0.0f;
        _globals.BeAZombie[client] = 0;
        _globals.DeathTime[client] = 0;
        _globals.InProtect[client] = false;
        _globals.PlayerHud[client] = false;
        _globals.PlayerDmgHud[client] = false;

        _services.CheckHumanAlive();
    }

    private void Event_OnPrecacheResource(IOnPrecacheResourceEvent @event)
    {
        var CFG = _mainConfig.CurrentValue;
        var ZombieCFG = _zombieConfig.GetConfig();

        var ZombieList = ZombieCFG.ZombieList;
        foreach (var zombie in ZombieList)
        {
            @event.AddItem(zombie.Model);
            Console.WriteLine($"PrecacheMod: {zombie.Model}");
        }
        if (!string.IsNullOrEmpty(CFG.PrecacheSoundEvent))
        {
            var soundList = CFG.PrecacheSoundEvent
                .Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s));

            foreach (var sound in soundList)
            {
                @event.AddItem(sound);
                Console.WriteLine($"PrecacheSound: {sound}");
            }
        }

    }
    private HookResult OnWeaponFire(EventWeaponFire @event)
    {
        if(!_globals.Active||!_globals.Ready)return HookResult.Continue;
        var player = @event.UserIdPlayer;
        if (player == null || !player.IsValid)
            return HookResult.Continue;

        var controller = player.Controller;
        if (controller == null || !controller.IsValid)
            return HookResult.Continue;

        var pawn = player.PlayerPawn;
        if (pawn == null || !pawn.IsValid)
            return HookResult.Continue;

        if (pawn.TeamNum == 3 && _globals.InProtect[player.PlayerID])
        {
            _globals.InProtect[player.PlayerID] = false;
            player.SendMessage(MessageType.Chat, $"{_core.Translation.GetPlayerLocalizer(player)["RemoveProtect"]}");
        }

        return HookResult.Continue;
    }

}
