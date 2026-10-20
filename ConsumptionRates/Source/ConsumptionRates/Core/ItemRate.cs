using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ConsumptionRates.Core
{
    // One chart row's computed numbers, from the current history buffer -
    // never stored itself, rebuilt at draw time (cr-architecture.md 6.3).
    public class ItemRate
    {
        public string Label;
        public ThingDef Def;          // null for the Nutrition row
        public bool IsPower;
        public bool IsNutrition;

        // Two figures, not one net number - the settled answer to open
        // question 6 (cr-architecture.md 13.6): each carries whatever a
        // crop's projected contribution adds, so Creation - Consumption
        // always equals NetPerDay exactly.
        public double CreationPerDay;
        public double ConsumptionPerDay;
        public double NetPerDay;

        // The part of CreationPerDay that is a growing field's future
        // yield rather than something already in stock - architecture 8.3,
        // drawn as a distinguishable extension of the bar.
        public double ProjectedComponentPerDay;

        // Stock count, nutrition, or - for power, since 2026-09-21 (item 4)
        // - the larger of generated/used, i.e. the same scale
        // PercentOfScale is already computed against. DrawNetBar reads this
        // for every row, power included, so power now draws through the
        // same single-net-bar code every other row uses.
        public double CurrentValue;
        public double PercentOfScale; // the ranking rule, open question 2
        public bool HasRate;

        // Power only - two independent instantaneous readings in watts,
        // kept for the row's label text (architecture section 7 carve-out).
        // 2026-09-21: no longer drawn as two separate bars - see NetPerDay
        // and CurrentValue above.
        public double PowerGenerated;
        public double PowerUsed;
    }

    public static class ItemRateBuilder
    {
        private const double TicksPerDay = 60000.0;

        public static List<ItemRate> BuildAll(Map map, ConsumptionRatesMapComponent comp, ConsumptionRatesSettings settings)
        {
            var rows = new List<ItemRate>();

            foreach (var def in ResourceCategories.VisibleThingDefs(settings))
            {
                rows.Add(BuildItemRow(map, comp, def));
            }

            if (!settings.IsHidden(ConsumptionRatesSettings.NutritionCategoryKey))
            {
                rows.Add(BuildNutritionRow(map, comp));
            }

            // The single aggregate meat row (item 1, 2026-09-21) - summed
            // across every plain-meat ThingDef (MeatDefs.PlainMeat),
            // human meat and insect meat excluded and still shown through
            // BuildItemRow above via their own real category.
            if (!settings.IsHidden(ConsumptionRatesSettings.MeatCategoryKey))
            {
                rows.Add(BuildMeatRow(map, comp));
            }

            if (!settings.IsHidden(ConsumptionRatesSettings.PowerCategoryKey))
            {
                rows.Add(BuildPowerRow(map));
            }

            // Worst first, by how far each row sits from its OWN scale, not
            // by raw magnitude - open question 2, settled 2026-09-20: watts
            // and kilograms per day are not comparable, so every row is
            // ranked by the same unit-less percentage instead. A row with
            // no rate yet sorts at 0, in the middle, rather than reading as
            // the worst deficit by default.
            rows.Sort((a, b) => a.PercentOfScale.CompareTo(b.PercentOfScale));
            return rows;
        }

        // ---- item / nutrition rows --------------------------------------

        private static ItemRate BuildItemRow(Map map, ConsumptionRatesMapComponent comp, ThingDef def)
        {
            ComputeCountSeries(comp.History, def, out double creationTotal, out double consumptionTotal, out double spanDays, out bool hasRate);

            double measuredCreation = hasRate ? creationTotal / spanDays : 0.0;
            double measuredConsumption = hasRate ? consumptionTotal / spanDays : 0.0;
            double projected = ProjectedComponent(comp, def);

            double currentStock = map.resourceCounter.GetCount(def);
            bool anyRate = hasRate || projected > 0.0;

            var row = new ItemRate
            {
                Label = def.LabelCap,
                Def = def,
                CreationPerDay = measuredCreation + projected,
                ConsumptionPerDay = measuredConsumption,
                ProjectedComponentPerDay = projected,
                NetPerDay = measuredCreation + projected - measuredConsumption,
                CurrentValue = currentStock,
                HasRate = anyRate
            };
            row.PercentOfScale = ComputePercent(row.NetPerDay, currentStock, anyRate);
            return row;
        }

        private static ItemRate BuildNutritionRow(Map map, ConsumptionRatesMapComponent comp)
        {
            ComputeFloatSeries(comp.History, s => s.nutrition, out double creationTotal, out double consumptionTotal, out double spanDays, out bool hasRate);

            double measuredCreation = hasRate ? creationTotal / spanDays : 0.0;
            double measuredConsumption = hasRate ? consumptionTotal / spanDays : 0.0;

            // Every tracked crop that is human-edible adds its projected
            // nutrition-per-day too (architecture 8.3, second bullet).
            double projected = 0.0;
            foreach (var kv in comp.LatestCrops)
            {
                ThingDef def = kv.Key;
                if (!def.IsNutritionGivingIngestible || def.ingestible == null || !def.ingestible.HumanEdible)
                {
                    continue;
                }
                double perDayCount = ProjectedComponent(comp, def);
                if (perDayCount <= 0.0)
                {
                    continue;
                }
                projected += perDayCount * def.GetStatValueAbstract(StatDefOf.Nutrition);
            }

            double currentNutrition = map.resourceCounter.TotalHumanEdibleNutrition;
            bool anyRate = hasRate || projected > 0.0;

            var row = new ItemRate
            {
                Label = "Nutrition",
                IsNutrition = true,
                CreationPerDay = measuredCreation + projected,
                ConsumptionPerDay = measuredConsumption,
                ProjectedComponentPerDay = projected,
                NetPerDay = measuredCreation + projected - measuredConsumption,
                CurrentValue = currentNutrition,
                HasRate = anyRate
            };
            row.PercentOfScale = ComputePercent(row.NetPerDay, currentNutrition, anyRate);
            return row;
        }

        // ---- meat aggregate row (item 1, 2026-09-21) ---------------------

        // One row, summed the same way ItemRate sums any item: across
        // history for the rate, live for the current stock. Human meat and
        // insect meat are excluded from MeatDefs.PlainMeat and never enter
        // this sum - they keep their own ordinary rows.
        private static ItemRate BuildMeatRow(Map map, ConsumptionRatesMapComponent comp)
        {
            var meatDefs = MeatDefs.PlainMeat();

            ComputeGroupCountSeries(comp.History, meatDefs, out double creationTotal, out double consumptionTotal, out double spanDays, out bool hasRate);

            double measuredCreation = hasRate ? creationTotal / spanDays : 0.0;
            double measuredConsumption = hasRate ? consumptionTotal / spanDays : 0.0;

            double currentStock = 0.0;
            foreach (var def in meatDefs)
            {
                currentStock += map.resourceCounter.GetCount(def);
            }

            var row = new ItemRate
            {
                Label = "Meat",
                CreationPerDay = measuredCreation,
                ConsumptionPerDay = measuredConsumption,
                NetPerDay = measuredCreation - measuredConsumption,
                CurrentValue = currentStock,
                HasRate = hasRate
            };
            row.PercentOfScale = ComputePercent(row.NetPerDay, currentStock, hasRate);
            return row;
        }

        // Same telescoping-sum logic as ComputeCountSeries, but summed
        // across a group of ThingDefs at each sample instead of reading one
        // - the meat aggregate's per-sample total, not a second measurement.
        private static void ComputeGroupCountSeries(List<Sample> history, List<ThingDef> defs, out double creationTotal, out double consumptionTotal, out double spanDays, out bool hasRate)
        {
            creationTotal = 0.0;
            consumptionTotal = 0.0;
            spanDays = 0.0;
            hasRate = false;

            bool any = false;
            int firstTick = 0, lastTick = 0, prevValue = 0;

            foreach (var sample in history)
            {
                int value = 0;
                foreach (var def in defs)
                {
                    if (sample.counts.TryGetValue(def, out int c))
                    {
                        value += c;
                    }
                }

                if (!any)
                {
                    firstTick = sample.tick;
                    prevValue = value;
                    any = true;
                    continue;
                }

                int delta = value - prevValue;
                if (delta > 0)
                {
                    creationTotal += delta;
                }
                else if (delta < 0)
                {
                    consumptionTotal += -delta;
                }
                prevValue = value;
                lastTick = sample.tick;
                hasRate = true;
            }

            if (hasRate)
            {
                spanDays = (lastTick - firstTick) / TicksPerDay;
                if (spanDays <= 0.0)
                {
                    hasRate = false;
                }
            }
        }

        // ---- power row - its own treatment, architecture section 7 -----

        private static ItemRate BuildPowerRow(Map map)
        {
            double generated = 0.0;
            double used = 0.0;

            // Same lists vanilla already maintains (PowerNet.powerComps),
            // walked once, split by the sign of PowerOutput - not summed
            // with CurrentEnergyGainRate(), which would only give the net
            // (architecture 3.3).
            foreach (var powerNet in map.powerNetManager.AllNetsListForReading)
            {
                foreach (var comp in powerNet.powerComps)
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

            // "Total capacity for power" (open question 2's own wording) is
            // not a number this design otherwise computes - the scale used
            // here is the larger of the two live readings, i.e. how far
            // out of balance the grid is relative to its own throughput.
            // A necessary implementation choice, not settled by the user.
            double scale = Math.Max(generated, used);
            double net = generated - used;

            var row = new ItemRate
            {
                Label = "Power",
                IsPower = true,
                PowerGenerated = generated,
                PowerUsed = used,
                NetPerDay = net,
                // 2026-09-21 (item 4): CurrentValue carries the same scale
                // PercentOfScale is computed against, so DrawNetBar can
                // draw power through the ordinary single-net-bar path.
                CurrentValue = scale,
                HasRate = true
            };
            row.PercentOfScale = scale > 0.0001 ? net / scale * 100.0 : 0.0;
            return row;
        }

        // ---- shared series arithmetic ------------------------------------

        // Walks the history buffer in tick order and sums only the positive
        // steps as "creation" and only the negative steps (as a positive
        // magnitude) as "consumption." Because a telescoping sum of signed
        // deltas always equals (last - first), Creation - Consumption is
        // always exactly the same net figure section 6.3's plain diff would
        // give - this is the mechanism behind the settled answer to open
        // question 6, not a second, different rate.
        private static void ComputeCountSeries(List<Sample> history, ThingDef def, out double creationTotal, out double consumptionTotal, out double spanDays, out bool hasRate)
        {
            creationTotal = 0.0;
            consumptionTotal = 0.0;
            spanDays = 0.0;
            hasRate = false;

            bool any = false;
            int firstTick = 0, lastTick = 0, prevValue = 0;

            foreach (var sample in history)
            {
                if (!sample.counts.TryGetValue(def, out int value))
                {
                    continue;
                }

                if (!any)
                {
                    firstTick = sample.tick;
                    prevValue = value;
                    any = true;
                    continue;
                }

                int delta = value - prevValue;
                if (delta > 0)
                {
                    creationTotal += delta;
                }
                else if (delta < 0)
                {
                    consumptionTotal += -delta;
                }
                prevValue = value;
                lastTick = sample.tick;
                hasRate = true;
            }

            if (hasRate)
            {
                spanDays = (lastTick - firstTick) / TicksPerDay;
                if (spanDays <= 0.0)
                {
                    hasRate = false;
                }
            }
        }

        private static void ComputeFloatSeries(List<Sample> history, Func<Sample, float> selector, out double creationTotal, out double consumptionTotal, out double spanDays, out bool hasRate)
        {
            creationTotal = 0.0;
            consumptionTotal = 0.0;
            spanDays = 0.0;
            hasRate = false;

            bool any = false;
            int firstTick = 0, lastTick = 0;
            float prevValue = 0f;

            foreach (var sample in history)
            {
                float value = selector(sample);

                if (!any)
                {
                    firstTick = sample.tick;
                    prevValue = value;
                    any = true;
                    continue;
                }

                float delta = value - prevValue;
                if (delta > 0f)
                {
                    creationTotal += delta;
                }
                else if (delta < 0f)
                {
                    consumptionTotal += -delta;
                }
                prevValue = value;
                lastTick = sample.tick;
                hasRate = true;
            }

            if (hasRate)
            {
                spanDays = (lastTick - firstTick) / TicksPerDay;
                if (spanDays <= 0.0)
                {
                    hasRate = false;
                }
            }
        }

        // Architecture 8.3: projected yield at full growth, spread over the
        // estimated remaining days to get there, from CropTracker's latest
        // scan and the measured growth speed (8.2). Zero when there is no
        // tracked field of this type, or no measured growth speed yet.
        private static double ProjectedComponent(ConsumptionRatesMapComponent comp, ThingDef harvestedDef)
        {
            if (!comp.LatestCrops.TryGetValue(harvestedDef, out var agg))
            {
                return 0.0;
            }
            if (agg.trackedCount == 0 || agg.projectedYieldAtFullGrowth <= 0)
            {
                return 0.0;
            }
            if (!TryGrowthRatePerDay(comp.History, harvestedDef, out double growthRatePerDay) || growthRatePerDay <= 0.0)
            {
                return 0.0;
            }

            double remainingDays = (1.0 - agg.meanGrowth) / growthRatePerDay;
            remainingDays = Math.Max(remainingDays, 0.01); // guard a field that is essentially done growing
            return agg.projectedYieldAtFullGrowth / remainingDays;
        }

        // Measured, not calculated (decision 2 / open question 3): the mean
        // Growth sampled each interval, diffed like any other row.
        private static bool TryGrowthRatePerDay(List<Sample> history, ThingDef harvestedDef, out double ratePerDay)
        {
            ratePerDay = 0.0;
            bool any = false;
            int firstTick = 0, lastTick = 0;
            float firstValue = 0f, lastValue = 0f;

            foreach (var sample in history)
            {
                if (!sample.cropMeanGrowth.TryGetValue(harvestedDef, out float g))
                {
                    continue;
                }
                if (!any)
                {
                    firstTick = sample.tick;
                    firstValue = g;
                    any = true;
                }
                lastTick = sample.tick;
                lastValue = g;
            }

            if (!any || lastTick == firstTick)
            {
                return false;
            }

            double spanDays = (lastTick - firstTick) / TicksPerDay;
            if (spanDays <= 0.0)
            {
                return false;
            }

            ratePerDay = (lastValue - firstValue) / spanDays;
            return true;
        }

        // Open question 2's ranking rule: a row's own scale is its current
        // stock (or nutrition amount); a deficit against zero stock is the
        // worst possible reading, and a surplus or zero net against zero
        // stock is not a deficit at all.
        private static double ComputePercent(double netPerDay, double scaleValue, bool hasRate)
        {
            if (!hasRate)
            {
                return 0.0;
            }
            if (scaleValue > 0.0001)
            {
                return netPerDay / scaleValue * 100.0;
            }
            return netPerDay < 0.0 ? -999.0 : 0.0;
        }
    }
}
