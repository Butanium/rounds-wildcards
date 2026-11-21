using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnboundLib;
using UnboundLib.Cards;

namespace WildCards
{
    [BepInDependency("com.willis.rounds.unbound", BepInDependency.DependencyFlags.HardDependency)]
    [BepInPlugin(ModId, ModName, Version)]
    [BepInProcess("Rounds.exe")]
    public class WildCardsPlugin : BaseUnityPlugin
    {
        public const string ModId = "com.wildcards.rounds.cards";
        public const string ModName = "WildCards";
        public const string Version = "1.3.2";
        public const string ModInitials = "WILD";

        public static WildCardsPlugin instance;
        public static ConfigEntry<float> WildChance;

        void Awake()
        {
            instance = this;
            WildChance = Config.Bind("Testing", "WildCardChance", 0.6f,
                "Chance (0-1) that each offered card is replaced by a random WildCards card. 0 = normal odds. " +
                "Applies to the hands YOU are offered. Disabled cards (Toggle Cards menu) are never offered.");
            new Harmony(ModId).PatchAll();
        }

        void Start()
        {
            CardRegistry.RegisterAll();
        }
    }
}
