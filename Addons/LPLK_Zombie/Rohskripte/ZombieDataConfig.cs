// Derived from H-AN/HanZombieRiotS2 v3.2; GPL-3.0. See LICENSE and PROVENANCE.json.

namespace LPLK_Zombie;

public class ZombieDataConfig
{
    public class Zombie
    {
        public string Name { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public int Health { get; set; } = 0;
        public float Speed { get; set; } = 0f;
        public float Damage { get; set; } = 0f;
        public float Gravity { get; set; } = 0f;
        public int HealthRevive { get; set; } = 0;
        public float HealthReviveSec { get; set; } = 0f;
        public int HealthReviveHp { get; set; } = 0;
        public int Percent { get; set; } = 0;  // 出现概率
        public float ZombieScale { get; set; } = 0f; 

    }
    public List<Zombie> ZombieList { get; set; } = new List<Zombie>();

}



