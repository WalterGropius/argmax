#nullable disable
namespace Argmax.Sim
{
    /// <summary>
    /// Every tuning number in the game. Integers only: the simulation must be bit-identical
    /// inside the SpacetimeDB wasm module, the Unity client and the ML-Agents gym.
    /// </summary>
    public static class Rules
    {
        public const int TickHz = 10;
        public const int TickMs = 1000 / TickHz;
        public const int MapW = 64, MapH = 64;

        // 2048: a mote's value is 2^tier. Two equal motes fuse into the next tier.
        public const int MaxTier = 11; // 2048, the Singularity
        public const int SeatCount = 3; // 0,1 = players, 2 = the Noise (feral code)
        public const byte NoiseSeat = 2;
        public const byte NoWinner = 255, Draw = 254;

        public static int Value(int tier) => 1 << tier;
        public static int MaxHp(int tier) => 10 << (tier - 1);
        public static int Damage(int tier) => 3 << (tier - 1);
        public static int Armor(int tier) => (tier - 1) / 2;
        public static int Range(int tier) => RangeT[tier];
        public static int Cooldown(int tier) => CooldownT[tier];
        public static int MovePeriod(int tier) => MoveT[tier];
        public static int Capacity(int tier) => tier >= 4 ? 8 : 1 << (tier - 1);
        public const int SplashTier = 4; // tier >= 4: half damage to every enemy adjacent to the target

        static readonly byte[] RangeT = { 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4 };
        static readonly byte[] CooldownT = { 0, 3, 3, 4, 4, 5, 6, 7, 8, 9, 10, 11 };
        static readonly byte[] MoveT = { 0, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4 };

        public const int CoreHp = 3000, CoreArmor = 2, CoreRange = 3, CoreDamage = 12, CoreCooldown = 5;
        public const int StartScrap = 30;
        public const int MoteCost = 2; // 1 scrap = 1 value
        public const int FreeRules = 4; // every rule past this costs +1 scrap per mote: the instruction tax
        public const int MaxRules = 8;
        public const int MaxLiterals = 4;
        public const int LoadoutSlots = 6;
        public const int PassiveIncomeEvery = 10;
        public const int SpawnEvery = 3;
        public const int MaxMotesPerSeat = 400;
        public const int SenseRadius = 4;
        public const int FlockRadius = 2; // the 5x5 receptive field of the Jev classifier
        public const int OverrideTicks = 60;
        public const int SingularityHold = 150;
        public const int TimeLimit = 18000; // 30 minutes
        public const int MetaEvery = 10;

        // Coherence: a self spread too thin dissolves (Markov blanket).
        public const int CoherenceEvery = 20;
        public const int LonelyGrace = 40;
        public const int BlanketRadius = 6; // motes this close to their own core never decohere
        public const int RegenAllies = 3;

        // Stigmergy: motes write to the grid, other motes read it (turmites).
        public const int TrailMark = 48, TrailDecayEvery = 6;

        // The Noise: feral programs that crawl out of wells.
        public const int WellEvery = 90, MaxFerals = 48, FeralBountyPct = 50;
        public const uint FeralProgramBase = 0xF0000000u;
        public const int FeralPrograms = 6;

        public const uint ChamberProgramId = 0x00F00000u;
    }
}
