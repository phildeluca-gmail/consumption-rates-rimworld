using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ConsumptionRates.Core
{
    // One growing-zone scan's result for one harvestedThingDef -
    // cr-architecture.md section 8.
    public struct CropAggregate
    {
        public float meanGrowth;
        public int trackedCount;
        public int projectedYieldAtFullGrowth;
    }

    // Reaches only the player's own growing zones, never the whole map -
    // architecture 3.5/8.1. Bounded by the number of cells the player has
    // actually zoned for growing, not by map size.
    public static class CropTracker
    {
        public static Dictionary<ThingDef, CropAggregate> Scan(Map map)
        {
            var plantsByDef = new Dictionary<ThingDef, List<Plant>>();

            foreach (var zone in map.zoneManager.AllZones)
            {
                if (!(zone is Zone_Growing growingZone))
                {
                    continue;
                }

                foreach (var cell in growingZone.Cells)
                {
                    // ThingGrid.ThingAt<T> is one dictionary-style lookup per
                    // cell, not a scan of everything present - verified
                    // public on Verse.ThingGrid, architecture 3.5.
                    var plant = map.thingGrid.ThingAt<Plant>(cell);
                    if (plant == null)
                    {
                        continue;
                    }

                    // IsCrop: the plant standing here is the one the zone
                    // actually wants grown, not incidental grass or weeds.
                    if (!plant.IsCrop)
                    {
                        continue;
                    }

                    // Already fully grown and ready to cut: once cut it will
                    // show up in ResourceCounter's own stock, so counting it
                    // here too would double it - architecture 8.1. This
                    // does leave the gap architecture 8.4 names rather than
                    // hides; not fixed here.
                    if (plant.HarvestableNow)
                    {
                        continue;
                    }

                    var props = plant.def.plant;
                    if (props == null || props.harvestYield <= 0f || props.harvestedThingDef == null)
                    {
                        continue;
                    }

                    if (!plantsByDef.TryGetValue(props.harvestedThingDef, out var list))
                    {
                        list = new List<Plant>();
                        plantsByDef[props.harvestedThingDef] = list;
                    }
                    list.Add(plant);
                }
            }

            var result = new Dictionary<ThingDef, CropAggregate>();
            foreach (var kv in plantsByDef)
            {
                float growthSum = 0f;
                int yieldSum = 0;
                foreach (var plant in kv.Value)
                {
                    growthSum += plant.Growth;
                    yieldSum += ProjectedYieldAtFullGrowth(plant);
                }

                result[kv.Key] = new CropAggregate
                {
                    meanGrowth = growthSum / kv.Value.Count,
                    trackedCount = kv.Value.Count,
                    projectedYieldAtFullGrowth = yieldSum
                };
            }

            return result;
        }

        // Same arithmetic as the decompiled RimWorld.Plant.YieldNow()
        // (cr-architecture.md 3.4), with growthInt forced to 1f (full
        // growth): "what this plant would yield if it finished growing
        // right now, at its current health." A fixed conversion, not a
        // rate - decision 2 governs how fast something changes over time,
        // not this arithmetic, which is the same the game itself runs at
        // actual harvest time (architecture 8.2).
        private static int ProjectedYieldAtFullGrowth(Plant plant)
        {
            var props = plant.def.plant;
            float harvestYield = props.harvestYield;
            // Mathf.InverseLerp(harvestMinGrowth, 1f, 1f) is always 1f, so
            // the "how far past the minimum" term always reaches its 100%
            // ceiling at full growth - the formula collapses to the health
            // and difficulty factors only.
            harvestYield *= Mathf.Lerp(0.5f, 1f, (float)plant.HitPoints / plant.MaxHitPoints);
            if (props.harvestYieldAffectedByDifficulty)
            {
                harvestYield *= Find.Storyteller.difficulty.cropYieldFactor;
            }
            return GenMath.RoundRandom(harvestYield);
        }
    }
}
