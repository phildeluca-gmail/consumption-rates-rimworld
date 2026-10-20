using UnityEngine;
using Verse;
using Logger = ConsumptionRates.Utility.Logger;

namespace ConsumptionRates.Core
{
    // Entry point. RimWorld builds one of these per load and hands it the
    // ModContentPack.
    //
    // No Harmony instance and no PatchAll - this mod patches nothing at
    // all. The bottom-row tab comes from a MainButtonDef plus a
    // MainTabWindow subclass, and sampling is a plain
    // MapComponent.MapComponentTick() override - cr-architecture.md
    // section 12.
    public class ConsumptionRatesMod : Mod
    {
        public static ConsumptionRatesSettings Settings;

        public ConsumptionRatesMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<ConsumptionRatesSettings>();
            Logger.Loaded("Loaded.");
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.DoWindowContents(inRect);
        }

        // Must match About.xml's <name> exactly - the Mod options list is
        // drawn from this string, NOT from About.xml (CLAUDE.md section 0,
        // the 2026-09-12 mistake).
        public override string SettingsCategory()
        {
            return "Ketjak's Consumption Rates";
        }
    }
}
