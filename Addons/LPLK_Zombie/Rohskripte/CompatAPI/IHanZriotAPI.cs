using SwiftlyS2.Shared.Players;

namespace HanZombieRiotS2;

/// <summary>
/// Han Zombie Riot API.
/// Han 僵尸暴动 API.
/// </summary>
public interface IHanZriotAPI
{
    /// <summary>
    /// Get GameStart.
    /// 获取游戏开始状态.
    /// </summary>
    bool GameStart { get; }

    /// <summary>
    /// Get CurrentDay.
    /// 获取当前天数.
    /// </summary>
    int CurrentDay { get; }

    /// <summary>
    /// Get NeedKillZombie.
    /// 获取当前需要击杀多少只丧尸.
    /// </summary>
    int NeedKillZombie { get; }

    /// <summary>
    /// Get ZombieKill.
    /// 获取当前所有玩家已经击杀了多少只丧尸.
    /// </summary>
    int ZombieKill { get; }

    /// <summary>
    /// Get ZombiesLeft.
    /// 获取还剩余多少只丧尸通关.
    /// </summary>
    int ZombiesLeft { get; }

    /// <summary>
    /// Get HumansAlive.
    /// 获取当前存活人类数量.
    /// </summary>
    int HumansAlive { get; }

    /// <summary>
    /// Get MaxDay.
    /// 获取当前地图最大关卡天数.
    /// </summary>
    int MaxDay { get; }

    /// <summary>
    /// change player to human.
    /// 指定某个玩家成为人类.
    /// </summary>
    void ZRiot_Human(IPlayer player);

    /// <summary>
    /// change player to human.
    /// 指定某个玩家成为丧尸.
    /// </summary>
    void ZRiot_Zombie(IPlayer player);

}