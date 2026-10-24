// Derived from H-AN/HanZombieRiotS2 v3.2; GPL-3.0. See LICENSE and PROVENANCE.json.
namespace LPLK_Zombie;

public class HanZriotDayConfig
{
    public class Day
    {
        public string DayName { get; set; } = string.Empty;
        public int Count { get; set; } = 0;
        public int HealthBoost { get; set; } = 0;
        public int BeforeZombie { get; set; } = 0;
        public string Storyline { get; set; } = string.Empty;
        public string ZombieOverride { get; set; } = string.Empty;
    }

    public List<Day> Days { get; set; } = new List<Day>();

}
