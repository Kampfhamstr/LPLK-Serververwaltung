// Derived from H-AN/HanZombieRiotS2 v3.2; GPL-3.0. See LICENSE and PROVENANCE.json.
using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.ProtobufDefinitions;
using SwiftlyS2.Shared.SchemaDefinitions;
using static LPLK_Zombie.ZombieDataConfig;

namespace LPLK_Zombie;

public class HanZriotHelpers
{
    private readonly ILogger<HanZriotHelpers> _logger;
    private readonly ISwiftlyCore _core;
    private readonly IOptionsMonitor<HanZriotCFG> _mainConfig;
    private readonly HanZriotGlobals _globals;
    private readonly HanZriotHud _hud;
    private readonly IStageConfigProvider _dayConfig;
    private readonly IZombieConfigProvider _zombieConfig;

    public HanZriotHelpers(ISwiftlyCore core, ILogger<HanZriotHelpers> logger,
        IOptionsMonitor<HanZriotCFG> mainConfig,
        HanZriotGlobals globals, HanZriotHud hud,
        IStageConfigProvider dayConfig,
        IZombieConfigProvider zombieConfig)
    {
        _core = core;
        _logger = logger;
        _mainConfig = mainConfig;
        _globals = globals;
        _hud = hud;
        _dayConfig = dayConfig;
        _zombieConfig = zombieConfig;
    }


    public HanZriotDayConfig.Day GetCurrentDay(int RiotDay)
    {
        var config = _dayConfig.GetConfig();

        if (config.Days == null || config.Days.Count == 0)
            throw new InvalidOperationException($"{_core.Localizer["NoDayData"]}");

        if (RiotDay <= 0 || RiotDay > config.Days.Count)
        {
            RiotDay = 1;
        }

        return config.Days[RiotDay - 1];
    }

    public void ChangeBotTeam()
    {
        var allPlayers = _core.PlayerManager.GetAllPlayers();
        foreach (var player in allPlayers)
        {
            if (player != null && player.IsValid && player.IsFakeClient)
            {
                var Controller = player.Controller;
                if (Controller != null && Controller.IsValid)
                {
                    if (Controller.TeamNum == 3)
                    {
                        player.SwitchTeam(Team.T);
                        Controller.TeamNum = 2;
                        Controller.TeamNumUpdated();
                    }
                }
            }

        }

    }


    public void RespawnAllZombie()
    {
        var allPlayers = _core.PlayerManager.GetAllPlayers();
        foreach (var player in allPlayers)
        {
            if (player != null && player.IsValid)
            {
                var Controller = player.Controller;
                if (Controller != null && Controller.IsValid)
                {

                    if (Controller.TeamNum == 2)
                    {

                        if (!Controller.PawnIsAlive)
                        {

                            Controller.Respawn();
                        }
                    }
                }

            }
        }
    }


    public void PlayAmbSound(string sounds) //播放环境音乐
    {
        if (!string.IsNullOrWhiteSpace(sounds))
        {
            var toPlay = RandomSelectSound(sounds);
            if (toPlay != null)
            {
                EmitSoundToAll(toPlay);
            }
        }
    }

    public string? RandomSelectSound(string sound)
    {
        if (string.IsNullOrWhiteSpace(sound)) return null;

        var items = sound
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();

        if (items.Length == 0) return null;

        return items.Length == 1 ? items[0] : items[Random.Shared.Next(items.Length)];
    }


    public void EmitSoundToEntity(IPlayer player, string SoundPath)
    {
        if (!string.IsNullOrEmpty(SoundPath))
        {
            var sound = new SwiftlyS2.Shared.Sounds.SoundEvent(SoundPath, 1.0f, 1.0f);
            sound.SourceEntityIndex = player.PlayerID;
            sound.Recipients.AddAllPlayers();
            _globals.Scheduler.NextTick(() =>
            {
                sound.Emit();
            });
        }
    }

    public void EmitSoundToAll(string SoundPath)
    {
        if (!string.IsNullOrEmpty(SoundPath))
        {
            var sound = new SwiftlyS2.Shared.Sounds.SoundEvent(SoundPath, 1.0f, 1.0f);
            sound.SourceEntityIndex = -1;
            sound.Recipients.AddAllPlayers();
            _globals.Scheduler.NextTick(() =>
            {
                sound.Emit();
            });
        }
    }

    public void RemoveRoundObjective()
    {
        var objectivelist = new List<string>() { "func_bomb_target", "func_hostage_rescue", "hostage_entity", "c4" };

        foreach (string objectivename in objectivelist)
        {
            var entityIndex = _core.EntitySystem.GetAllEntitiesByDesignerName<CEntityInstance>(objectivename).ToArray();

            foreach (var entity in entityIndex)
            {
                if (entity != null && entity.IsValid)
                {
                    entity.AcceptInput("Kill", 0, null, null);
                }

            }
        }
    }

    public void TeleportZombie(IPlayer player) //传送僵尸 回合结束隐藏僵尸
    {
        if (player == null || !player.IsValid)
            return;

        var clientpawn = player.PlayerPawn;
        if (clientpawn == null)
            return;

        if (player.IsFakeClient)
        {
            var entities = _core.EntitySystem.GetAllEntitiesByDesignerName<SpawnPoint>("info_player_terrorist").FirstOrDefault();
            var position = entities?.AbsOrigin;
            var EntAngle = entities?.AbsRotation;
            var EntVelocity = entities?.AbsVelocity;
            if (position != null)
            {
                player?.Teleport((SwiftlyS2.Shared.Natives.Vector)position, (SwiftlyS2.Shared.Natives.QAngle)EntAngle!, (SwiftlyS2.Shared.Natives.Vector)EntVelocity!);
            }

        }

    }

    public void ChangeMap()
    {
        var CFG = _mainConfig.CurrentValue;

        string baseConfig = _core.Configuration.GetConfigPath("");
        string mapsConfig = Path.Combine(baseConfig, "mapsconfig");

        // 如果 mapsconfig 文件夹不存在 → 使用默认地图
        if (!Directory.Exists(mapsConfig))
        {
            FallbackToDefault();
            return;
        }

        // 根据主配置读取对应文件
        string fileName = CFG.useworkshopmap switch
        {
            1 => "RandomMapConfig.jsonc",   // 混合集
            2 => "WorkShopMapConfig.jsonc", // 只读取工坊
            3 => "MapConfig.jsonc",         // 只读取官图
            _ => "MapConfig.jsonc"
        };

        string fullPath = Path.Combine(mapsConfig, fileName);

        // 文件不存在 → 使用默认地图
        if (!File.Exists(fullPath))
        {
            _core.Logger.LogWarning($"{_core.Localizer["MapCfgError"]}: {fullPath}");
            FallbackToDefault();
            return;
        }

        // 读取有效地图行
        var mapList = File.ReadAllLines(fullPath)
            .Select(line => line.Trim())
            .Where(ZombieProfile.SafeMapTarget)
            .ToList();

        // 如果配置为空 → 默认地图
        if (mapList.Count == 0)
        {
            _core.Logger.LogWarning($"{_core.Localizer["MapCfgEmpty"]}: {fullPath}");
            FallbackToDefault();
            return;
        }

        // 随机选择一个地图项
        string selected = mapList[Random.Shared.Next(mapList.Count)];

        // 判断是 官图 OR 工坊地图
        bool isWorkshop = selected.All(char.IsDigit);

        _logger.LogInformation($"{_core.Localizer["MapRandomSelect"]}: {selected} (Workshop: {isWorkshop})");

        // 执行更换地图
        _globals.Scheduler.DelayBySeconds(1.0f, () =>
        {
            if (isWorkshop)
            {
                // → 工坊地图：使用 host_workshop_map
                _core.Engine.ExecuteCommand($"host_workshop_map {selected}");
            }
            else
            {
                // → 普通地图：使用 changelevel
                _core.Engine.ExecuteCommand($"changelevel {selected}");
            }
        });
    }


    private void FallbackToDefault()
    {
        _core.Logger.LogWarning($"{_core.Localizer["UseDefaultMap"]}：de_dust2");
        _globals.Scheduler.DelayBySeconds(1.0f, () =>
        {
            _core.Engine.ExecuteCommand("changelevel de_dust2");
        });
    }



    public List<Zombie> GetZombiesForCurrentLevel(int currentDayIndex)
    {
        var Dayconfig = _dayConfig.GetConfig();
        var Zombieconfig = _zombieConfig.GetConfig();
        //_core.Logger.LogInformation($"[僵尸选择] 当前Day索引: {currentDayIndex}");

        if (currentDayIndex < 0 || currentDayIndex >= Dayconfig.Days.Count)
        {
            //_core.Logger.LogInformation("[僵尸选择] 索引无效，返回空列表");
            return new List<Zombie>();
        }

        var day = Dayconfig.Days[currentDayIndex];
        List<Zombie> zombiesForLevel = new List<Zombie>();

        //_core.Logger.LogInformation($"[僵尸选择] 当前关卡名: {day.DayName}, ZombieOverride = {day.ZombieOverride}");

        if (!string.IsNullOrEmpty(day.ZombieOverride))
        {
            var zombieNames = day.ZombieOverride.Split(',');
            //_core.Logger.LogInformation($"[僵尸选择] 指定僵尸数量: {zombieNames.Length}");

            foreach (var zombieName in zombieNames)
            {
                //_core.Logger.LogInformation($"[僵尸选择] 查找僵尸: {zombieName}");
                var zombie = Zombieconfig.ZombieList.FirstOrDefault(
                    z => z.Name.Equals(zombieName, StringComparison.OrdinalIgnoreCase)
                );

                if (zombie != null)
                {
                    //_core.Logger.LogInformation($"[僵尸选择] 找到僵尸: {zombie.Name}");
                    zombiesForLevel.Add(zombie);
                }
                else
                {
                    _core.Logger.LogWarning($"{_core.Localizer["NoZombieByName", zombieName]}");
                }
            }
        }
        else
        {
            if (Zombieconfig != null)
            {
                //_core.Logger.LogInformation($"[僵尸选择] 没指定僵尸，使用默认列表，共 {Zombieconfig.ZombieList.Count} 个。");
                zombiesForLevel = Zombieconfig.ZombieList;
            }
            else
            {
                _core.Logger.LogWarning($"{_core.Localizer["ZombieCfgError"]}");
            }
        }

        //_core.Logger.LogInformation($"[僵尸选择] 最终僵尸数量: {zombiesForLevel.Count}");
        return zombiesForLevel;
    }


    public void UpdateKillCount()
    {
        if (_globals.CurrentMapIsHighDiff)
        {
            // 如果当前地图是高难度，击杀数不会增加
            return;
        }

        // 允许普通难度增加击杀数
        _globals.KillCount++;

        // 计算击杀百分比
        _globals.KillPercent = MathF.Round(_globals.KillCount * 0.1f, 1);

        // 当击杀百分比达到 100% 时，标记下一张地图为高难度
        if (_globals.KillPercent >= 100f)
        {
            _globals.HightDiff = true;
        }
        else
        {
            _globals.HightDiff = false; // **如果击杀百分比低于 100%，重置为普通难度**
        }
    }


    public void RespawnClient(CCSPlayerController Controller)
    {
        if (!Controller.IsValid || Controller.PawnIsAlive)
            return;

        Controller.Respawn();
    }

    public void DeleSpawnProtect(IPlayer player) //删除重生保护
    {
        if (player == null || !player.IsValid)
            return;
        var pawn = player.PlayerPawn;
        if (pawn == null || !pawn.IsValid)
            return;

        if (pawn.TeamNum == 3 && _globals.InProtect[player.PlayerID])
        {
            _globals.InProtect[player.PlayerID] = false;
            player.SendMessage(MessageType.Chat, $"{_core.Translation.GetPlayerLocalizer(player)["RemoveProtect"]}");
        }
    }

    public void NoBlock(CCSPlayerPawn pawn) //碰撞体积关闭
    {
        if (pawn == null || !pawn.IsValid)
            return;

        pawn.Collision.CollisionGroup = (byte)CollisionGroup.Debris;
        pawn.CollisionRulesChanged();
    }

    public void GiveCash(IPlayer player, int account, string info) //给予金钱
    {
        if (player == null || !player.IsValid)
            return;

        var controller = player.Controller;
        if (controller == null || !controller.IsValid)
            return;

        var CFG = _mainConfig.CurrentValue;

        var Ims = controller.InGameMoneyServices;
        if (Ims == null || !Ims.IsValid)
            return;

        int current = Ims.Account;
        int max = _core.ConVar.Find<int>("mp_maxmoney")?.Value ?? 16000;
        int newMoney = (int)Math.Clamp((long)current + Math.Max(0,account),0,Math.Max(0,max));
        Ims.Account = newMoney;
        controller.InGameMoneyServicesUpdated();

        var loc = _core.Translation.GetPlayerLocalizer(player);

        if (info == "hurt" && CFG.HurtMoneyMessage)
        {
            player.SendMessage(MessageType.Chat, $"{loc["HurtMoney", account]}");
        }
        else if (info == "death" && CFG.DeathMoneyMessage)
        {
            player.SendMessage(MessageType.Chat, $"{loc["KillMoney", account]}");
        }
    }

    /*
    
    public void SetPlayerScale(IPlayer player, float scale)
    {
        if (player == null || !player.IsValid)
            return;

        var pawn = player.PlayerPawn;
        if (pawn == null || !pawn.IsValid)
            return;

        var skeletonInstance = pawn!.CBodyComponent?.SceneNode?.GetSkeletonInstance();
        if (skeletonInstance != null)
        {
            skeletonInstance.Scale = scale;
        }
        pawn.SetScale(scale);
    }
    
    */

    public void SetTeamScore(Team team) //设置队伍分数
    {
        var teamManagers = _core.EntitySystem.GetAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");

        foreach (var teamManager in teamManagers)
        {
            if ((int)team == teamManager.TeamNum)
            {
                teamManager.Score += 1;
                teamManager.ScoreUpdated();
            }
        }
    }









    public void TerminateRound(RoundEndReason reason, float delay)
    {
        var gameRules = _core.EntitySystem.GetGameRules();
        if (gameRules is not { IsValid: true, WarmupPeriod: false })
            return;

        gameRules.TerminateRound(reason, delay);
    }



    public void SetFreezeState(IPlayer player, bool freeze)
    {
        if (!player.IsValid)
            return;

        var controller = player.Controller;
        if (controller == null || !controller.IsValid)
            return;

        var pawn = player.PlayerPawn;
        if (pawn == null || !pawn.IsValid) return;

        var moveType = freeze ? MoveType_t.MOVETYPE_NONE : MoveType_t.MOVETYPE_WALK;
        pawn.MoveType = moveType;
        pawn.ActualMoveType = moveType;
        pawn.MoveTypeUpdated();
    }

    public void SetAllZombieUnFreeze()
    {
        var allPlayers = _core.PlayerManager.GetAllPlayers();
        foreach (var player in allPlayers)
        {
            if (!player.IsValid)
                return;

            var controller = player.Controller;
            if (controller == null || !controller.IsValid)
                continue;

            var pawn = player.PlayerPawn;
            if (pawn == null || !pawn.IsValid)
                return;

            var moveType = MoveType_t.MOVETYPE_WALK;
            pawn.MoveType = moveType;
            pawn.ActualMoveType = moveType;
            pawn.MoveTypeUpdated();
        }
    }

    public void HumanDeathCountDown()
    {
        _globals.g_DeathCountDown?.Cancel();
        _globals.g_DeathCountDown = null;

        _globals.g_DeathCountDown = _globals.Scheduler.RepeatBySeconds(1.0f, () =>
        {
            int now = Environment.TickCount;
            var ctPlayers = _core.PlayerManager.GetCT();
            foreach (var player in ctPlayers)
            {
                try
                {
                    if (player == null || !player.IsValid || player.IsFakeClient)
                        continue;

                    int target = _globals.DeathTime[player.PlayerID];

                    if (target <= 0)
                        continue;

                    int remainMs = target - now;
                    int remainSec = remainMs / 1000;

                    if (remainMs <= 0)
                    {
                        _globals.DeathTime[player.PlayerID] = 0;

                        _globals.Scheduler.NextTick(() =>
                        {
                            if (player is { IsValid: true } && player.Controller is { IsValid: true } ctrl)
                            {
                                RespawnClient(ctrl);
                                player.SendMessage(MessageType.Chat, $"{_core.Translation.GetPlayerLocalizer(player)["Spawned"]}");
                            }
                        });
                    }
                    else
                    {
                        int displaySec = remainSec < 0 ? 0 : remainSec;
                        player.SendMessage(MessageType.CenterHTML, $"{_core.Translation.GetPlayerLocalizer(player)["ReSpawn", displaySec]}");
                    }
                }
                catch (Exception ex)
                {
                    _core.Logger.LogError($"DeathTimer Individual Player Error: {ex.Message}");
                }
            }
        });

        _globals.Scheduler.StopOnMapChange(_globals.g_DeathCountDown);
    }


    public void ZombieRegenTimer()
    {
        _globals.g_ZombieRegenTimer?.Cancel();
        _globals.g_ZombieRegenTimer = null;

        _globals.g_ZombieRegenTimer = _globals.Scheduler.RepeatBySeconds(0.2f, () =>
        {
            int now = Environment.TickCount;
            var aliveZombies = _core.PlayerManager.GetTAlive();

            foreach (var player in aliveZombies)
            {
                try
                {
                    if (!_globals.g_ZombieRegenStates.TryGetValue(player.PlayerID, out var state))
                        continue;

                    var pawn = player.PlayerPawn;
                    if (pawn == null || !pawn.IsValid)
                        continue;

                    int maxHealth = pawn.MaxHealth;
                    if (pawn.Health >= maxHealth)
                        continue;

                    if (now < state.NextRegenTime)
                        continue;

                    pawn.Health = Math.Min(pawn.Health + state.RegenAmount, maxHealth);
                    pawn.HealthUpdated();

                    state.NextRegenTime = now + state.RegenInterval;
                }
                catch (Exception ex)
                {
                    _core.Logger.LogError($"Regen Error: {ex.Message}");
                }
            }
        });

        _globals.Scheduler.StopOnMapChange(_globals.g_ZombieRegenTimer);
    }

    public void GlobalHudTimer()
    {
        _globals.g_HUDTimer?.Cancel();
        _globals.g_HUDTimer = null;

        _globals.g_HUDTimer = _globals.Scheduler.RepeatBySeconds(0.1f, () =>
        {

            var aliveHumans = _core.PlayerManager.GetCTAlive();

            foreach (var player in aliveHumans)
            {
                try
                {
                    if (player.IsFakeClient)
                        continue;

                    _hud.Show(player);
                }
                catch (Exception ex)
                {
                    _core.Logger.LogError($"HUD Timer Error: {ex.Message}");
                }
            }
        });

        _globals.Scheduler.StopOnMapChange(_globals.g_HUDTimer);
    }
}
