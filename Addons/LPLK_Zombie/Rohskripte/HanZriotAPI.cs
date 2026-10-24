using HanZombieRiotS2;
// Derived from H-AN/HanZombieRiotS2 v3.2; GPL-3.0. See LICENSE and PROVENANCE.json.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Helpers;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;

namespace LPLK_Zombie;

public class HanZriotAPI : IHanZriotAPI, IDisposable
{

    private bool _disposed = false;
    private readonly ILogger<HanZriotAPI> _logger;
    private readonly ISwiftlyCore _core;
    private readonly HanZriotGlobals _globals;
    private readonly IStageConfigProvider _dayConfig;
    private void ThrowIfDisposed()
    {
        if (_disposed || !_globals.Active || !_globals.Ready)
        {
            throw new ObjectDisposedException(nameof(HanZriotAPI));
        }
    }
    public HanZriotAPI(ISwiftlyCore core, ILogger<HanZriotAPI> logger,
        HanZriotGlobals globals, IStageConfigProvider dayConfig)
    {
        _core = core;
        _logger = logger;
        _globals = globals;
        _dayConfig = dayConfig;
    }

    public bool GameStart => _globals.GameStart;
    public int CurrentDay => _globals.RiotDay;
    public int NeedKillZombie => _globals.NeedKillZombie;
    public int ZombieKill => _globals.ZombieKill;
    public int ZombiesLeft => Math.Max(0, _globals.NeedKillZombie - _globals.ZombieKill);
    public int HumansAlive => _core.PlayerManager.GetCTAlive().Count();
    public int MaxDay => _dayConfig.GetConfig().Days?.Count ?? 0;

    public void ZRiot_Human(IPlayer player)
    {
        ThrowIfDisposed();
        if (player == null || !player.IsValid)
            return;

        var pawn = player.PlayerPawn;
        if (pawn == null || !pawn.IsValid)
            return;

        if (pawn.TeamNum != 3)
        {
            player.ChangeTeam(Team.CT);
        }
    }

    public void ZRiot_Zombie(IPlayer player)
    {
        ThrowIfDisposed();

        if (player == null || !player.IsValid)
            return;

        var pawn = player.PlayerPawn;
        if (pawn == null || !pawn.IsValid)
            return;

        if (pawn.TeamNum != 2)
        {
            player.ChangeTeam(Team.T);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        
    }

    
}