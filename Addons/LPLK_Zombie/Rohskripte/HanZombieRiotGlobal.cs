// Derived from H-AN/HanZombieRiotS2 v3.2; GPL-3.0. See LICENSE and PROVENANCE.json.
using SwiftlyS2.Shared.Players;

namespace LPLK_Zombie;

public class HanZriotGlobals(SwiftlyS2.Shared.ISwiftlyCore core)
{
    public ZombieScheduler Scheduler {get;}=new(core);
    public bool Ready {get;set;}
    public bool Active => Scheduler.Scope.Active;
    // ===== 数值类 =====
    public int NeedKillZombie { get; set; }
    public int ZombieKill { get; set; }
    public int RiotDay { get; set; } = 1;
    public int Countdown { get; set; }
    public int KillCount { get; set; }
    public float KillPercent { get; set; }

    // ===== 布尔类 =====
    public bool GameStart { get; set; }
    public bool HightDiff { get; set; } = false;
    public bool CurrentMapIsHighDiff { get; set; }
    public bool AllowHumanZombie { get; set; }

    // ===== 数组类（玩家状态） =====
    public float[] RebornSec { get; } = new float[65];
    public int[] BeAZombie { get; } = new int[65];
    public int[] DeathTime { get; } = new int[65];

    public bool[] InProtect { get; } = new bool[65];

    public bool[] PlayerHud { get; set; } = new bool[65];
    public bool[] PlayerDmgHud { get; set; } = new bool[65];


    public CancellationTokenSource? SpawnAllZombie { get; set; } = null;

    public CancellationTokenSource? g_DeathCheck { get; set; } = null;
    public CancellationTokenSource? g_hCountdown { get; set; } = null;

    public CancellationTokenSource? g_hAmbMusic { get; set; } = null;



    public CancellationTokenSource?[] SpawnProtect { get; set; } = new CancellationTokenSource?[65];
    public CancellationTokenSource?[] g_DeadCountDown { get; set; } = new CancellationTokenSource?[65];


    public CancellationTokenSource? g_HUDTimer { get; set; } = null;
    public CancellationTokenSource? g_DeathCountDown { get; set; } = null;

    public Dictionary<int, ZombieRegenState> g_ZombieRegenStates = new();

    public CancellationTokenSource? g_ZombieRegenTimer = null;

}

public class ZombieRegenState
{
    public int PlayerID;
    public int RegenAmount;       // 每次回血量
    public float RegenInterval;   // 间隔秒数
    public float NextRegenTime;   // 下一次回血时间戳（秒）
}