// Derived from H-AN/HanZombieRiotS2 v3.2; GPL-3.0. See LICENSE and PROVENANCE.json.
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;

namespace LPLK_Zombie;

public class HanZriotHud
{
    private readonly ILogger<HanZriotHud> _logger;
    private readonly ISwiftlyCore _core;
    private readonly IOptionsMonitor<HanZriotCFG> _mainConfig;
    private readonly IStageConfigProvider _dayConfig;
    private readonly IZombieConfigProvider _zombieConfig;
    private readonly HanZriotGlobals _globals;


    public HanZriotHud(ISwiftlyCore core, ILogger<HanZriotHud> logger,
        IOptionsMonitor<HanZriotCFG> mainConfig,
        IStageConfigProvider dayConfig,
        IZombieConfigProvider zombieConfig,
        HanZriotGlobals globals)
    {
        _core = core;
        _logger = logger;
        _mainConfig = mainConfig;
        _dayConfig = dayConfig;
        _zombieConfig = zombieConfig;
        _globals = globals;
    }
    public void Show(IPlayer player)
    {

        if (player is not { IsValid: true } || _globals.PlayerHud[player.PlayerID] || player.IsFakeClient)
            return;

        var pawn = player.PlayerPawn;
        var controller = player.Controller;
        if (pawn is not { IsValid: true } || controller is not { IsValid: true })
            return;


        int humanCount = _core.PlayerManager.GetCTAlive().Count();
        int aliveZombie = _core.PlayerManager.GetTAlive().Count();

        int leftZombie = _globals.NeedKillZombie - _globals.ZombieKill;
        var dayConfig = _dayConfig.GetConfig();
        int maxDay = dayConfig.Days.Count;
        var currentDay = HudGetCurrentDay(_globals.RiotDay);

        var localizer = _core.Translation.GetPlayerLocalizer(player);

        string diffMessage;
        // string wDiffMessage; 

        if (!_globals.CurrentMapIsHighDiff)
        {
            if (_globals.KillPercent <= 100f)
                diffMessage = $"<span><font color='#E22D2D'>[{localizer["PollutionCount"]}:{_globals.KillPercent}％]</font></span><br>";
            else
                diffMessage = $"<span><font color='#E22D2D'>[{localizer["HardMode"]}]</font></span><br>";
        }
        else
        {
            diffMessage = $"<span><font color='#E22D2D'>[{localizer["ZombiePowerful"]}]</font></span><br>";
        }


        string message = $"<span><font color='#E22D2D'>{currentDay.DayName}</font></span><br>" +
            $"<span><font color='#FFFFE0'>{localizer["Stage"]}:[{localizer["Progress"]}</font><font color='#87CEEB'>{_globals.RiotDay}</font><font color='#FFFFE0'>/</font><font color='#87CEEB'>{maxDay}</font><font color='#FFFFE0'>{localizer["Days"]}]</font></span><br>" +
            $"<span><font color='#FFFFE0'>{localizer["ZombiesLeft"]}:</font> <font color='#E22D2D'>{leftZombie}</font> <font color='#FFFFE0'>{localizer["ZCount"]}</font></span><br>" +
            $"<span><font color='#FFFFE0'>{localizer["HumanLeft"]}:</font> <font color='#00FF00'>{humanCount}</font> <font color='#FFFFE0'>{localizer["HCount"]}</font></span><br>" +
            $"{diffMessage}" +
            $"<span><font color='#FFFFE0'>{currentDay.Storyline}</font></span>";

        if (_globals.GameStart && controller.PawnIsAlive && pawn.TeamNum == 3)
        {
            player.SendMessage(MessageType.CenterHTML, message);
        }
    }

    public HanZriotDayConfig.Day HudGetCurrentDay(int riotDay)
    {
        var config = _dayConfig.GetConfig(); //CurrentValue;

        if (config.Days == null || config.Days.Count == 0)
            throw new InvalidOperationException($"{_core.Localizer["ServerHudError"]}");

        if (riotDay < 1 || riotDay > config.Days.Count)
            riotDay = 1;

        return config.Days[riotDay - 1];
    }


}