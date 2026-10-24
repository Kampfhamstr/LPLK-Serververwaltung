// Derived from H-AN/HanZombieRiotS2 v3.2; GPL-3.0. See LICENSE and PROVENANCE.json.
namespace LPLK_Zombie;

public class HanZriotCFG
{
    public bool Enabled {get;set;}=false;
    public int useworkshopmap { get; set; } = 0;
    public bool HurtMoney { get; set; } = true;
    public bool HurtMoneyMessage { get; set; } = true;
    public int DeathMoney { get; set; } = 100;
    public bool DeathMoneyMessage { get; set; } = true;
    public bool SpawnProtect { get; set; } = true;
    public float SpawnProtectCount { get; set; } = 10f;
    public bool ZombieNoBlock { get; set; } = true;
    public bool HumanNoBlock { get; set; } = false;
    public float FreezeZombie { get; set; } = 20f;
    public float HumanRebornSec { get; set; } = 10f;
    public bool Soundremaining { get; set; } = true;
    public string SoundEventremaining { get; set; } = "remainingsound";

    public bool SoundCountdown { get; set; } = true;
    public string SoundEventCountdown { get; set; } = "sound1,sound2,sound3,sound4,sound5,sound6,sound7,sound8,sound9,sound10";

    public bool SoundZombieStart { get; set; } = true;
    public string SoundEventZombieStart { get; set; } = "ZombieStartsound";

    public bool SoundHumanWins { get; set; } = true;
    public string SoundEventHumanWins { get; set; } = "humanwinssound";

    public bool SoundZombieWins { get; set; } = true;
    public string SoundEventZombieWins { get; set; } = "zombiewinsound";

    public bool SoundRoundstartMusic { get; set; } = true;
    public string SoundEventRoundstartMusic { get; set; } = "roundstartsound";

    public bool SoundZombieHurt { get; set; } = true;
    public string SoundEventZombieHurt { get; set; } = "ZombieHurtsound";

    public bool SoundZombieDead { get; set; } = true;
    public string SoundEventZombieDead { get; set; } = "ZombieDeadsound";

    public bool SoundZombiePain { get; set; } = true;
    public string SoundEventZombiePain { get; set; } = "ZombiePainsound";

    public bool SoundAmbSound { get; set; } = true;
    public string SoundEventAmbSound { get; set; } = "AmbSound";
    public float AmbSoundLoopTime { get; set; } = 30f;

    public string AdminCommandPermission { get; set; } = string.Empty;

    public string PrecacheSoundEvent { get; set; } = string.Empty;




}