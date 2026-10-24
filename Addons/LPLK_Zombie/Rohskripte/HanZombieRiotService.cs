// Derived from H-AN/HanZombieRiotS2 v3.2; GPL-3.0. See LICENSE and PROVENANCE.json.
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using static LPLK_Zombie.ZombieDataConfig;


namespace LPLK_Zombie;

public class HanZriotService
{
    private readonly ILogger<HanZriotService> _logger;
    private readonly ISwiftlyCore _core;
    private readonly IOptionsMonitor<HanZriotCFG> _mainConfig;
    private readonly IStageConfigProvider _dayConfig;
    private readonly IZombieConfigProvider _zombieConfig;
    private readonly HanZriotHelpers _helpers;
    private readonly HanZriotGlobals _globals;


    public HanZriotService(ISwiftlyCore core, ILogger<HanZriotService> logger,
        IOptionsMonitor<HanZriotCFG> mainConfig,
        IStageConfigProvider dayConfig,
        IZombieConfigProvider zombieConfig,
        HanZriotHelpers helpers, HanZriotGlobals globals)
    {
        _core = core;
        _logger = logger;
        _mainConfig = mainConfig;
        _dayConfig = dayConfig;
        _zombieConfig = zombieConfig;
        _helpers = helpers;
        _globals = globals;
    }

    public void PossZombie(IPlayer client) //应用僵尸各项属性
    {
        if (client == null || !client.IsValid)
            return;

        var clientpawn = client.PlayerPawn;
        if (clientpawn == null || !clientpawn.IsValid)
            return;

        var Controller = client.Controller;
        if (Controller == null || !Controller.IsValid)
            return;

        var CFG = _mainConfig.CurrentValue;
        var Dayconfig = _dayConfig.GetConfig(); //.CurrentValue;

        clientpawn.ItemServices!.GiveItem<CCSWeaponBase>("weapon_knife");


        if (CFG.ZombieNoBlock)
        {
            _helpers.NoBlock(clientpawn);
        }

        // 获取当前关卡的僵尸数据
        var currentDay = _helpers.GetCurrentDay(_globals.RiotDay);// RiotDay 从 1 开始
        int currentDayIndex = Dayconfig.Days.IndexOf(currentDay); // 获取当前关卡的索引
        var zombiesForLevel = _helpers.GetZombiesForCurrentLevel(currentDayIndex); // 传递索引


        // 随机选择一个僵尸
        if (zombiesForLevel.Count > 0)
        {
            var randomZombie = zombiesForLevel[Random.Shared.Next(zombiesForLevel.Count)]; // 随机选择一个僵尸

            int maxhealth;
            if (currentDay.HealthBoost > 0)
            {
                maxhealth = currentDay.HealthBoost + randomZombie.Health;
            }
            else
            {
                maxhealth = randomZombie.Health;
            }

            clientpawn.SetModel(randomZombie.Model); // 设置模型
            // 设置属性
            if (currentDay.HealthBoost > 0)
            {

                clientpawn.Health = randomZombie.Health + currentDay.HealthBoost;
                clientpawn.HealthUpdated();

            }
            else
            {

                clientpawn.Health = randomZombie.Health;
                clientpawn.HealthUpdated();


            }
            
            clientpawn.VelocityModifier = randomZombie.Speed > 0 ? randomZombie.Speed : 1.0f;
            clientpawn.ActualGravityScale = randomZombie.Gravity > 0 ? randomZombie.Gravity : 0.8f;

            if (randomZombie.HealthRevive > 0)
            {
                var now = Environment.TickCount / 1000f;
                _globals.g_ZombieRegenStates[client.PlayerID] = new ZombieRegenState
                {
                    PlayerID = client.PlayerID,
                    RegenAmount = randomZombie.HealthReviveHp,
                    RegenInterval = randomZombie.HealthReviveSec,
                    NextRegenTime = now + randomZombie.HealthReviveSec // 下一次回血时间
                };
            }

            var ControllerEntity = Controller.Entity;
            if (ControllerEntity != null && ControllerEntity.IsValid)
            {
                ControllerEntity.Name = randomZombie.Name;
            }
        }
        else
        {
            _core.Logger.LogError($"{_core.Localizer["NoZombieData"]}");
        }

    }
    public void Round_Countdown()
    {
        var CFG = _mainConfig.CurrentValue;
        int currentDisplay = _globals.Countdown;

        if (_globals.Countdown > 0) _globals.Countdown--;

        if (currentDisplay <= 10 && currentDisplay >= 1 && CFG.SoundCountdown)
        {
            var soundList = CFG.SoundEventCountdown.Split(',');

            int soundIndex = currentDisplay - 1;

            if (soundIndex >= 0 && soundIndex < soundList.Length)
            {
                _helpers.EmitSoundToAll(soundList[soundIndex].Trim());
            }
        }
        else if (currentDisplay == 20 && CFG.Soundremaining && !string.IsNullOrWhiteSpace(CFG.SoundEventremaining))
        {
            var remaining = _helpers.RandomSelectSound(CFG.SoundEventremaining);
            if (remaining != null)
            {
                _helpers.EmitSoundToAll(remaining);
            }
        }

        if (currentDisplay <= 0)
        {
            _globals.g_hCountdown?.Cancel();
            _globals.g_hCountdown = null;
            _globals.GameStart = true;
            _helpers.SetAllZombieUnFreeze();
            if (CFG.SoundZombieStart && !string.IsNullOrWhiteSpace(CFG.SoundEventZombieStart))
            {
                var ZombieStart = _helpers.RandomSelectSound(CFG.SoundEventZombieStart);
                if (ZombieStart != null)
                {
                    _helpers.EmitSoundToAll(ZombieStart);
                }
            }
            return;
        }

        foreach (var player in _core.PlayerManager.GetCTAlive())
        {
            if (player is { IsValid: true } && !player.IsFakeClient)
            {
                player.SendMessage(MessageType.Center, $"{currentDisplay} {_core.Translation.GetPlayerLocalizer(player)["MoveZombie"]}");
            }
        }
    }


    public void FakeCtswin()
    {
        var CFG = _mainConfig.CurrentValue;
        var Dayconfig = _dayConfig.GetConfig();
        var currentDay = _helpers.GetCurrentDay(_globals.RiotDay);

        int maxDay = Dayconfig.Days.Count; // 获取关卡总数
        _globals.GameStart = false;

        var allPlayers = _core.PlayerManager.GetAllPlayers();
        foreach (var player in allPlayers)
        {
            if (!player.IsFakeClient)
            {
                if (_globals.RiotDay == maxDay)
                {
                    string Message = $"<span><font color='#E22D2D'>{_core.Translation.GetPlayerLocalizer(player)["ClearZombieOnMap"]}</font></span><br>" +
                    $"<span><span><font color='#E22D2D'>{_core.Translation.GetPlayerLocalizer(player)["HumanTakeOver"]}</font></span><br>" +
                    $"<span><span><font color='#00FF00'>{_core.Translation.GetPlayerLocalizer(player)["NextMap"]}</font></span>";
                    player.SendMessage(MessageType.CenterHTML, $"{Message}");
                }
                else
                {
                    string Message = $"<span><font color='#E22D2D'>{_core.Translation.GetPlayerLocalizer(player)["HumanWins"]}</font></span><br>" +
                    $"<span><font color='#E22D2D'>{_core.Translation.GetPlayerLocalizer(player)["ZombieClear"]}</font></span><br>" +
                    $"<span><font color='#00FF00'>{_core.Translation.GetPlayerLocalizer(player)["NextDay"]}</font></span>";
                    player.SendMessage(MessageType.CenterHTML, $"{Message}");
                }

                _globals.InProtect[player.PlayerID] = true;

                _globals.BeAZombie[player.PlayerID] = currentDay.BeforeZombie;
                _globals.DeathTime[player.PlayerID] = 0;
            }
            else
            {
                _helpers.SetFreezeState(player, true);
                _helpers.TeleportZombie(player);
            }

        }
        _globals.g_DeathCheck?.Cancel();
        _globals.g_DeathCheck = null;

        if (CFG.SoundHumanWins && !string.IsNullOrWhiteSpace(CFG.SoundEventHumanWins))
        {
            var HumanWins = _helpers.RandomSelectSound(CFG.SoundEventHumanWins);
            if (HumanWins != null)
            {
                _helpers.EmitSoundToAll(HumanWins);
            }
        }


        if (_globals.RiotDay == maxDay)
        {
            _globals.Scheduler.DelayBySeconds(5.0f, () => { _helpers.ChangeMap(); });
        }
        else
        {
            _helpers.SetTeamScore(Team.CT);
            _helpers.TerminateRound(RoundEndReason.CTsWin, 8.0f);
        }
    }
    public void Faketswin()
    {
        var CFG = _mainConfig.CurrentValue;
        var Dayconfig = _dayConfig.GetConfig();
        _globals.GameStart = false;
        var currentDay = _helpers.GetCurrentDay(_globals.RiotDay);
        var allPlayers = _core.PlayerManager.GetAllPlayers();
        foreach (var player in allPlayers)
        {
            if (!player.IsFakeClient) // 人类玩家
            {
                player.SendMessage(MessageType.CenterHTML, $"{_core.Translation.GetPlayerLocalizer(player)["ZombieWins"]}");
                _globals.BeAZombie[player.PlayerID] = currentDay.BeforeZombie;
                _globals.DeathTime[player.PlayerID] = 0;
            }
        }

        if (CFG.SoundZombieWins && !string.IsNullOrWhiteSpace(CFG.SoundEventZombieWins))
        {
            var ZombieWins = _helpers.RandomSelectSound(CFG.SoundEventZombieWins);
            if (ZombieWins != null)
            {
                _helpers.EmitSoundToAll(ZombieWins);
            }
        }

        _globals.g_DeathCheck?.Cancel();
        _globals.g_DeathCheck = null;

        _helpers.SetTeamScore(Team.T);
        _helpers.TerminateRound(RoundEndReason.TerroristsWin, 8.0f);

    }

    public void ForceDayEnd()
    {
        _globals.GameStart = false;

        _globals.g_hCountdown?.Cancel();
        _globals.g_hCountdown = null;

        _globals.ZombieKill = 0;

        _helpers.TerminateRound(RoundEndReason.RoundDraw, 8.0f);
    }

    public void JoinTeamCheck(IPlayer player)
    {
        if (player is not { IsValid: true } || player.Controller is not { IsValid: true } ctrl)
            return;

        if (!_globals.GameStart)
        {
            if (!ctrl.PawnIsAlive)
            {
                ctrl.Respawn();
            }
            return;
        }

        var humanCount = _core.PlayerManager.GetCTAlive().Count();

        if (humanCount > 0)
        {
            if (!ctrl.PawnIsAlive)
            {
                ctrl.Respawn();
            }
        }
        else
        {
            if (_globals.g_DeathCheck != null)
            {
                Faketswin();
            }
        }
    }


    public void CheckHumanAlive()
    {
        var humanCount = _core.PlayerManager.GetCTAlive().Count();
        if (humanCount <= 0 && _globals.GameStart)
        {
            Faketswin();
            _globals.GameStart = false;
        }
    }

}
