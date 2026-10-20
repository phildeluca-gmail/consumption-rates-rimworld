using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ConsumptionRates.Core
{
    // One row's grouping for the toggle checkboxes (section 9) - every
    // ThingCategoryDef that owns at least one currently resource-counted
    // ThingDef, grouped by that ThingDef's FIRST listed category
    // (ThingDef.thingCategories[0], verified public - cr-architecture.md
    // does not say which category a ThingDef with more than one belongs
    // under for this purpose, so the first one is the simplest reading and
    // is the choice made here). Built once and cached - defs do not change
    // within a running session.
    public class ResourceCategory
    {
        public string Key;
        public string Label;
        public List<ThingDef> Defs = new List<ThingDef>();
    }

    public static class ResourceCategories
    {
        private static List<ResourceCategory> cached;

        // 2026-09-21: every plain-meat ThingDef (MeatDefs.PlainMeat, below)
        // is folded into the single synthetic "Meat" row instead of
        // appearing here as its own category member - built by
        // ItemRateBuilder.BuildMeatRow, toggled by
        // ConsumptionRatesSettings.MeatCategoryKey. Human meat and insect
        // meat are excluded from that fold (MeatDefs.Excluded) and keep
        // showing up through the ordinary path below, under whichever real
        // category they belong to.
        //
        // Also 2026-09-21: any category whose own label is "misc" (case-
        // insensitive) is dropped entirely - see MainTabWindow's toggle
        // list, and cr-architecture.md section 9 for what this removed.
        // Verified against the installed game's Core Defs (not the DLL,
        // which carries no XML defs): ThingCategoryDefs\ThingCategories.xml
        // defines ItemsMisc/ApparelMisc/BuildingsMisc, each labelled
        // "misc" - RimWorld's own grab-bag for things that don't belong
        // anywhere else. Of the three, only ItemsMisc currently owns any
        // resource-counted (CountAsResource) ThingDef in Core: Skull,
        // ReinforcedBarrel, AIPersonaCore, TechprofSubpersonaCore,
        // ThrumboHorn, ElephantTusk - six otherwise-unrelated items sharing
        // only "didn't fit elsewhere." ApparelMisc and BuildingsMisc own no
        // resource-counted def in Core (apparel and buildings default to
        // Uncounted), so this label check is what actually excludes them
        // too, not a name-specific one.
        public static List<ResourceCategory> All()
        {
            if (cached != null)
            {
                return cached;
            }

            var byKey = new Dictionary<string, ResourceCategory>();
            var plainMeat = MeatDefs.PlainMeat();

            // Same set ResourceCounter itself counts - CountAsResource is
            // resourceReadoutPriority != Uncounted, cr-architecture.md 3.2.
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!def.CountAsResource)
                {
                    continue;
                }

                if (plainMeat.Contains(def))
                {
                    continue;
                }

                ThingCategoryDef cat = (def.thingCategories != null && def.thingCategories.Count > 0)
                    ? def.thingCategories[0]
                    : null;

                if (cat != null && cat.label != null && cat.label.Trim().ToLowerInvariant() == "misc")
                {
                    continue;
                }

                string key = cat != null ? cat.defName : "Other";
                string label = cat != null ? cat.label : "Other";

                if (!byKey.TryGetValue(key, out var entry))
                {
                    entry = new ResourceCategory { Key = key, Label = label };
                    byKey[key] = entry;
                }
                entry.Defs.Add(def);
            }

            cached = byKey.Values.OrderBy(c => c.Label).ToList();
            return cached;
        }

        public static IEnumerable<ThingDef> VisibleThingDefs(ConsumptionRatesSettings settings)
        {
            foreach (var cat in All())
            {
                if (settings.IsHidden(cat.Key))
                {
                    continue;
                }
                foreach (var def in cat.Defs)
                {
                    yield return def;
                }
            }

            // Plain meat (item 1, 2026-09-21) is sampled and saved the same
            // way as any other visible ThingDef, gated by its own synthetic
            // toggle rather than by a real category - ItemRateBuilder sums
            // these into one "Meat" row.
            if (!settings.IsHidden(ConsumptionRatesSettings.MeatCategoryKey))
            {
                foreach (var def in MeatDefs.PlainMeat())
                {
                    yield return def;
                }
            }
        }
    }

    // 2026-09-21: identifies "plain" meat for the single aggregate "Meat"
    // row (item 1) - every ThingDef.IsMeat def (public property, verified
    // by decompiling Assembly-CSharp.dll: category == Item &&
    // thingCategories.Contains(ThingCategoryDefOf.MeatRaw)) except human
    // meat and insect meat, which stay as their own rows.
    //
    // Exclusion is a property test, not a hardcoded def list: for every
    // ThingDef with a race (Verse.RaceProperties, verified public), if
    // race.Humanlike (public, "(int)intelligence >= 2") or race.Insect
    // (public, "FleshType == FleshTypeDefOf.Insectoid") is true, that
    // race's race.meatDef (public) is excluded. This is exactly how the
    // game itself decides Meat_Human belongs to a humanlike race and
    // Meat_Megaspider ("insect meat") belongs to an insectoid one, so a
    // modded humanlike or insectoid race's meat is excluded the same way
    // without naming it.
    public static class MeatDefs
    {
        private static List<ThingDef> cachedPlain;
        private static HashSet<ThingDef> cachedExcluded;

        public static List<ThingDef> PlainMeat()
        {
            if (cachedPlain == null)
            {
                Compute();
            }
            return cachedPlain;
        }

        public static HashSet<ThingDef> Excluded()
        {
            if (cachedExcluded == null)
            {
                Compute();
            }
            return cachedExcluded;
        }

        private static void Compute()
        {
            var excluded = new HashSet<ThingDef>();
            foreach (var raceDef in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (raceDef.race == null || raceDef.race.meatDef == null)
                {
                    continue;
                }
                if (raceDef.race.Humanlike || raceDef.race.Insect)
                {
                    excluded.Add(raceDef.race.meatDef);
                }
            }

            var plain = new List<ThingDef>();
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.IsMeat && !excluded.Contains(def))
                {
                    plain.Add(def);
                }
            }

            cachedExcluded = excluded;
            cachedPlain = plain;
        }
    }

    // One point in time. Copied out of vanilla's already-live data
    // (ResourceCounter, PowerNet, CropTracker) at TakeSample() - never
    // computed from a formula (decision 2). Saved with the game (section
    // 6.4/open question 1, settled 2026-09-20): only the toggled-on
    // ThingDefs are copied into "counts," so hiding a category also keeps
    // the save small, not only the runtime cost (section 6.2).
    public class Sample : IExposable
    {
        public int tick;

        public Dictionary<ThingDef, int> counts = new Dictionary<ThingDef, int>();

        public float nutrition;

        // Watt-days/tick, summed from live CompPowerTrader readings each
        // sample - not diffed against an earlier sample (section 7, power is
        // an instant value, not a stock).
        public float powerGenerated;
        public float powerUsed;

        // Mean RimWorld.Plant.Growth (0..1) per harvestedThingDef, across
        // every tracked plant CropTracker found this sample (section 8.2).
        // Only the mean is kept - the projected yield-at-full-growth number
        // is stateless and recomputed fresh every sample, so it is not part
        // of the saved history.
        public Dictionary<ThingDef, float> cropMeanGrowth = new Dictionary<ThingDef, float>();

        // Parameterless constructor required by Scribe_Collections.Look's
        // LookMode.Deep, which news up each entry before calling ExposeData.
        public Sample()
        {
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref tick, "tick", 0);
            Scribe_Values.Look(ref nutrition, "nutrition", 0f);
            Scribe_Values.Look(ref powerGenerated, "powerGenerated", 0f);
            Scribe_Values.Look(ref powerUsed, "powerUsed", 0f);
            Scribe_Collections.Look(ref counts, "counts", LookMode.Def, LookMode.Value);
            Scribe_Collections.Look(ref cropMeanGrowth, "cropMeanGrowth", LookMode.Def, LookMode.Value);

            if (counts == null)
            {
                counts = new Dictionary<ThingDef, int>();
            }
            if (cropMeanGrowth == null)
            {
                cropMeanGrowth = new Dictionary<ThingDef, float>();
            }
        }
    }
}
