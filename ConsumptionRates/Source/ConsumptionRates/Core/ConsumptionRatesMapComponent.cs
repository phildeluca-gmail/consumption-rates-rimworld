using System.Collections.Generic;
using RimWorld;
using Verse;
using Logger = ConsumptionRates.Utility.Logger;

namespace ConsumptionRates.Core
{
    // The sampling clock and the saved history. RimWorld builds one of
    // these per map itself - Map.FillComponents finds every non-abstract
    // MapComponent subclass with a (Map) constructor and news it up
    // (verified by decompiling Verse.Map.FillComponents,
    // cr-architecture.md 3.1) - so nothing here needs a Harmony patch to
    // exist in the first place.
    public class ConsumptionRatesMapComponent : MapComponent
    {
        // Saved with the game (open question 1, settled 2026-09-20): a
        // rolling window of Sample, oldest first, trimmed to
        // ConsumptionRatesSettings.HistoryWindowSamples after every add.
        public List<Sample> History = new List<Sample>();

        // The latest growing-zone scan. NOT saved - a projected yield is
        // stateless and recomputed fresh every sample (architecture 8.2);
        // only the measured mean growth needs to be kept across time, and
        // that lives inside each Sample instead.
        public Dictionary<ThingDef, CropAggregate> LatestCrops = new Dictionary<ThingDef, CropAggregate>();

        // Counts samples since the last daily-summary line, so that line
        // fires once per full history window rather than once per sample
        // (section 11) - not saved, so a reload can push it out by at most
        // one window, which costs nothing but a slightly early or late
        // summary line.
        private int samplesSinceSummary;

        public ConsumptionRatesMapComponent(Map map) : base(map)
        {
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            var settings = ConsumptionRatesMod.Settings;
            Logger.Info("Sampling started for " + map.Parent.LabelCap + " - interval "
                + settings.sampleInterval + ", window " + settings.HistoryWindowSamples
                + " samples, " + History.Count + " restored from the save.");
        }

        public override void MapRemoved()
        {
            base.MapRemoved();
            Logger.Info("Sampling stopped for " + map.Parent.LabelCap + " - map removed.");
        }

        public override void MapComponentTick()
        {
            var settings = ConsumptionRatesMod.Settings;
            if (Find.TickManager.TicksGame % settings.SampleIntervalTicks == 0)
            {
                TakeSample(settings);
            }
        }

        private void TakeSample(ConsumptionRatesSettings settings)
        {
            var sample = new Sample { tick = Find.TickManager.TicksGame };

            // Stock counts - reads a dictionary vanilla is already keeping
            // current on its own clock (architecture 3.2); no extra scan.
            // Only the currently visible ThingDefs are copied, so hiding a
            // category also keeps the saved history small (section 6.2).
            foreach (var def in ResourceCategories.VisibleThingDefs(settings))
            {
                sample.counts[def] = map.resourceCounter.GetCount(def);
            }

            sample.nutrition = map.resourceCounter.TotalHumanEdibleNutrition;

            // Power - one pass over already-maintained lists, split by the
            // sign of PowerOutput (architecture 3.3/7). Read every sample
            // regardless of the Power toggle - it is two floats, not a
            // dictionary, so there is no size to save by skipping it.
            float generated = 0f;
            float used = 0f;
            foreach (var net in map.powerNetManager.AllNetsListForReading)
            {
                foreach (var comp in net.powerComps)
                {
                    if (!comp.PowerOn)
                    {
                        continue;
                    }
                    float output = comp.PowerOutput;
                    if (output > 0f)
                    {
                        generated += output;
                    }
                    else
                    {
                        used += -output;
                    }
                }
            }
            sample.powerGenerated = generated;
            sample.powerUsed = used;

            // Growing zones - bounded by zoned cells, not by map size
            // (architecture 3.5/8.1).
            LatestCrops = CropTracker.Scan(map);
            foreach (var kv in LatestCrops)
            {
                sample.cropMeanGrowth[kv.Key] = kv.Value.meanGrowth;
            }

            History.Add(sample);
            int cap = settings.HistoryWindowSamples;
            while (History.Count > cap)
            {
                History.RemoveAt(0);
            }

            samplesSinceSummary++;
            if (samplesSinceSummary >= cap)
            {
                LogDailySummary(settings);
                samplesSinceSummary = 0;
            }
        }

        // One line per full history window, not per sample (section 11) -
        // at the default hourly interval and one-day window that is once a
        // day, not 24 times.
        private void LogDailySummary(ConsumptionRatesSettings settings)
        {
            var rows = ItemRateBuilder.BuildAll(map, this, settings);

            ItemRate worstDeficit = null;
            ItemRate worstSurplus = null;
            int rowsOn = 0;

            foreach (var row in rows)
            {
                if (!row.HasRate)
                {
                    continue;
                }
                rowsOn++;
                if (worstDeficit == null || row.PercentOfScale < worstDeficit.PercentOfScale)
                {
                    worstDeficit = row;
                }
                if (worstSurplus == null || row.PercentOfScale > worstSurplus.PercentOfScale)
                {
                    worstSurplus = row;
                }
            }

            string deficitText = worstDeficit != null
                ? worstDeficit.Label + " (" + worstDeficit.PercentOfScale.ToString("F1") + "%/day)"
                : "none";
            string surplusText = worstSurplus != null
                ? worstSurplus.Label + " (" + worstSurplus.PercentOfScale.ToString("F1") + "%/day)"
                : "none";

            Logger.Info("Daily summary for " + map.Parent.LabelCap + ": " + rowsOn + " rows with a rate, "
                + LatestCrops.Count + " crop types tracked, worst deficit " + deficitText
                + ", best surplus " + surplusText + ".");
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref History, "history", LookMode.Deep);
            if (History == null)
            {
                History = new List<Sample>();
            }
        }
    }
}
