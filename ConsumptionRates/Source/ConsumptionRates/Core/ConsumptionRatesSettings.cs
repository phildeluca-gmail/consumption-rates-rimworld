using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ConsumptionRates.Core
{
    // Hourly is the default because it is exactly one in-game day / 24 - see
    // SampleIntervalTicks below and cr-architecture.md 6.1.
    public enum SampleInterval
    {
        Hourly,
        SixHourly,
        Daily
    }

    // Expressed to the player in hours, then turned into a sample count at
    // whatever the current interval is - architecture 6.3.
    public enum HistoryWindowLength
    {
        SixHours,
        OneDay,
        ThreeDays
    }

    // Mod settings - a display preference, not a fact about one colony, the
    // same reasoning architecture section 9 gives for keeping toggle state
    // here rather than in the save. The two performance-facing numbers
    // (interval, window) live here for the same reason.
    public class ConsumptionRatesSettings : ModSettings
    {
        public SampleInterval sampleInterval = SampleInterval.Hourly;
        public HistoryWindowLength historyWindow = HistoryWindowLength.OneDay;

        // Category keys the player has hidden. Two synthetic keys, "Power"
        // and "Nutrition", sit alongside every ThingCategoryDef.defName that
        // owns at least one currently resource-counted ThingDef - section 9.
        // Empty by default: everything shown until the player hides
        // something.
        public HashSet<string> hiddenCategories = new HashSet<string>();

        public const string PowerCategoryKey = "Power";
        public const string NutritionCategoryKey = "Nutrition";

        // The single aggregate meat row (item 1, 2026-09-21) - a third
        // synthetic key alongside Power and Nutrition, not a real
        // ThingCategoryDef.
        public const string MeatCategoryKey = "Meat";

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref sampleInterval, "sampleInterval", SampleInterval.Hourly);
            Scribe_Values.Look(ref historyWindow, "historyWindow", HistoryWindowLength.OneDay);
            Scribe_Collections.Look(ref hiddenCategories, "hiddenCategories", LookMode.Value);
            if (hiddenCategories == null)
            {
                hiddenCategories = new HashSet<string>();
            }
        }

        public bool IsHidden(string categoryKey)
        {
            return hiddenCategories.Contains(categoryKey);
        }

        public void SetHidden(string categoryKey, bool hidden)
        {
            if (hidden)
            {
                hiddenCategories.Add(categoryKey);
            }
            else
            {
                hiddenCategories.Remove(categoryKey);
            }
        }

        // ---- interval / window, converted for the sampler ---------------

        // 2500 ticks is one in-game hour - confirmed from the decompiled
        // GrowthPerTick formula (60000f * def.plant.growDays) in
        // cr-architecture.md 3.4/6.1: 60000 ticks per in-game day, so 2500 is
        // exactly 1/24 of a day regardless of game speed.
        public int SampleIntervalTicks
        {
            get
            {
                switch (sampleInterval)
                {
                    case SampleInterval.SixHourly:
                        return 15000;
                    case SampleInterval.Daily:
                        return 60000;
                    default:
                        return 2500;
                }
            }
        }

        // A combination that would round to fewer than 2 samples (no rate
        // can be computed from a single point, section 6.4) is floored at 2.
        public int HistoryWindowSamples
        {
            get
            {
                int hours;
                switch (historyWindow)
                {
                    case HistoryWindowLength.SixHours:
                        hours = 6;
                        break;
                    case HistoryWindowLength.ThreeDays:
                        hours = 72;
                        break;
                    default:
                        hours = 24;
                        break;
                }
                int windowTicks = hours * 2500;
                int samples = windowTicks / SampleIntervalTicks;
                return Mathf.Max(2, samples);
            }
        }

        public void DoWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.Label("Sample interval - how often a reading is taken:");
            if (listing.RadioButton("Hourly", sampleInterval == SampleInterval.Hourly))
            {
                sampleInterval = SampleInterval.Hourly;
            }
            if (listing.RadioButton("Every six hours", sampleInterval == SampleInterval.SixHourly))
            {
                sampleInterval = SampleInterval.SixHourly;
            }
            if (listing.RadioButton("Daily", sampleInterval == SampleInterval.Daily))
            {
                sampleInterval = SampleInterval.Daily;
            }

            listing.Gap();

            listing.Label("History window - how far back a rate is measured:");
            if (listing.RadioButton("Six hours", historyWindow == HistoryWindowLength.SixHours))
            {
                historyWindow = HistoryWindowLength.SixHours;
            }
            if (listing.RadioButton("One day", historyWindow == HistoryWindowLength.OneDay))
            {
                historyWindow = HistoryWindowLength.OneDay;
            }
            if (listing.RadioButton("Three days", historyWindow == HistoryWindowLength.ThreeDays))
            {
                historyWindow = HistoryWindowLength.ThreeDays;
            }

            listing.Gap();
            listing.Label("Which rows appear is chosen in the Consumption Rates window itself, on the bottom tab row.");

            listing.End();
        }
    }
}
