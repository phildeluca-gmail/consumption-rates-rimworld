using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using ConsumptionRates.Core;
using Logger = ConsumptionRates.Utility.Logger;

namespace ConsumptionRates.UI
{
    // The bottom-row window. Reached from Defs/MainButtonDefs -
    // MainButtonDef.tabWindowClass takes the full type name, which is why
    // the XML says ConsumptionRates.UI.MainTabWindow_ConsumptionRates and
    // not the bare name vanilla gets away with.
    //
    // Draws the toggle checkboxes (section 9) and one horizontal bar per
    // visible row, worst first (decision 3, ranked by open question 2's
    // percent-of-scale rule). All the arithmetic lives in ItemRateBuilder -
    // this class only lays it out.
    public class MainTabWindow_ConsumptionRates : MainTabWindow
    {
        private Vector2 scroll = Vector2.zero;

        private const float ToggleWidth = 170f;
        private const float ToggleHeight = 24f;
        private const float RowHeight = 50f;
        private const float RowPad = 4f;
        private const float LabelWidth = 150f;
        private const float ScrollBarWidth = 20f;

        private static readonly Color CreationColor = new Color(0.35f, 0.70f, 0.35f);
        private static readonly Color CreationColorProjected = new Color(0.35f, 0.70f, 0.35f, 0.45f);
        private static readonly Color ConsumptionColor = new Color(0.80f, 0.35f, 0.35f);
        private static readonly Color BaselineColor = new Color(0.5f, 0.5f, 0.5f);

        public override Vector2 RequestedTabSize
        {
            get { return new Vector2(900f, 700f); }
        }

        public override void PostOpen()
        {
            base.PostOpen();
            // A button pressed - worth a line, per CLAUDE.md's own example
            // of what is worth recording (section 11).
            Logger.Info("Window opened for " + (Find.CurrentMap != null ? Find.CurrentMap.Parent.LabelCap : "no map") + ".");
        }

        public override void DoWindowContents(Rect inRect)
        {
            var map = Find.CurrentMap;
            if (map == null)
            {
                Widgets.Label(inRect, "No map is open.");
                return;
            }

            var comp = map.GetComponent<ConsumptionRatesMapComponent>();
            if (comp == null)
            {
                Widgets.Label(inRect, "Consumption Rates is not tracking this map yet.");
                return;
            }

            var settings = ConsumptionRatesMod.Settings;

            float y = 0f;
            float toggleAreaHeight = DrawToggles(new Rect(0f, 0f, inRect.width, inRect.height), settings);
            y += toggleAreaHeight + RowPad;

            Widgets.DrawLineHorizontal(0f, y, inRect.width);
            y += RowPad;

            var rows = ItemRateBuilder.BuildAll(map, comp, settings);

            var outRect = new Rect(0f, y, inRect.width, inRect.height - y);
            var viewRect = new Rect(0f, 0f, inRect.width - ScrollBarWidth, rows.Count * (RowHeight + RowPad));

            Widgets.BeginScrollView(outRect, ref scroll, viewRect, true);
            float ry = 0f;
            foreach (var row in rows)
            {
                DrawRow(row, viewRect.width, ry);
                ry += RowHeight + RowPad;
            }
            Widgets.EndScrollView();
        }

        // ---- toggles (section 9) ----------------------------------------

        private float DrawToggles(Rect area, ConsumptionRatesSettings settings)
        {
            float x = 0f;
            float y = 0f;

            DrawOneToggle(area.width, ref x, ref y, "Power", ConsumptionRatesSettings.PowerCategoryKey, settings);
            DrawOneToggle(area.width, ref x, ref y, "Nutrition", ConsumptionRatesSettings.NutritionCategoryKey, settings);
            DrawOneToggle(area.width, ref x, ref y, "Meat", ConsumptionRatesSettings.MeatCategoryKey, settings);
            foreach (var cat in ResourceCategories.All())
            {
                DrawOneToggle(area.width, ref x, ref y, cat.Label.CapitalizeFirst(), cat.Key, settings);
            }

            return y + ToggleHeight;
        }

        private void DrawOneToggle(float maxWidth, ref float x, ref float y, string label, string key, ConsumptionRatesSettings settings)
        {
            if (x + ToggleWidth > maxWidth && x > 0f)
            {
                x = 0f;
                y += ToggleHeight;
            }

            bool shown = !settings.IsHidden(key);
            bool before = shown;
            Widgets.CheckboxLabeled(new Rect(x, y, ToggleWidth, ToggleHeight), label, ref shown);
            if (shown != before)
            {
                settings.SetHidden(key, !shown);
                // A player action, not a tick - the same reasoning Pawn
                // Hotgroups' per-keypress logging uses (section 11).
                Logger.Info("Toggle changed: " + label + " now " + (shown ? "shown" : "hidden") + ".");
            }

            x += ToggleWidth;
        }

        // ---- one chart row -----------------------------------------------

        // 2026-09-21 (item 3): the separate "+creation/day" and
        // "-consumption/day" figures beside each bar are gone - a row shows
        // only its label, current value, and the net bar. Power (item 4)
        // now draws through this same path instead of its own two-bar
        // layout - see BuildPowerRow's CurrentValue and NetPerDay.
        private void DrawRow(ItemRate row, float width, float y)
        {
            string valueLine = row.IsPower
                ? "generated " + row.PowerGenerated.ToString("F0") + " W / used " + row.PowerUsed.ToString("F0") + " W"
                : FormatValue(row.CurrentValue);

            var labelRect = new Rect(0f, y, LabelWidth, RowHeight);
            Widgets.Label(labelRect, row.Label + "\n" + valueLine);

            float barX = LabelWidth;
            float barWidth = width - barX;
            var barRect = new Rect(barX, y + 8f, barWidth, RowHeight - 16f);
            DrawNetBar(barRect, row);

            if (!row.HasRate)
            {
                GUI.color = BaselineColor;
                Widgets.Label(new Rect(barX, y, barWidth, RowHeight), "  (not enough history yet)");
                GUI.color = Color.white;
            }
        }

        // Right of centre for a surplus, left for a deficit, worst first
        // (decision 3). Length is the row's percent-of-scale, clamped to
        // +/-100 for drawing - the same number the sort order uses, so a
        // longer bar always means a worse or better reading, never just a
        // bigger unit. The projected component (architecture 8.3) is the
        // lighter outer band, drawn behind the solid measured-only band so
        // "will exist" is never shown identically to "already exists."
        private void DrawNetBar(Rect rect, ItemRate row)
        {
            float midX = rect.x + rect.width / 2f;
            float halfWidth = rect.width / 2f;

            Widgets.DrawLineVertical(midX, rect.y, rect.height);

            if (!row.HasRate)
            {
                return;
            }

            double scale = row.CurrentValue;
            double measuredNet = row.NetPerDay - row.ProjectedComponentPerDay;
            double totalPercent = Mathf.Clamp((float)row.PercentOfScale, -100f, 100f);
            double measuredPercent = scale > 0.0001 ? measuredNet / scale * 100.0 : (measuredNet < 0 ? -100.0 : 0.0);
            measuredPercent = Mathf.Clamp((float)measuredPercent, -100f, 100f);

            bool totalPositive = totalPercent >= 0;
            bool measuredPositive = measuredPercent >= 0;

            float totalLen = (float)System.Math.Abs(totalPercent) / 100f * halfWidth;
            float measuredLen = (float)System.Math.Abs(measuredPercent) / 100f * halfWidth;

            // Outer band (measured + projected together), lighter shade -
            // only visible beyond the solid band when the projected
            // component adds length in the same direction.
            Color outer = totalPositive ? CreationColorProjected : new Color(ConsumptionColor.r, ConsumptionColor.g, ConsumptionColor.b, 0.45f);
            Color inner = measuredPositive ? CreationColor : ConsumptionColor;

            if (totalPositive)
            {
                Widgets.DrawBoxSolid(new Rect(midX, rect.y, totalLen, rect.height), outer);
            }
            else
            {
                Widgets.DrawBoxSolid(new Rect(midX - totalLen, rect.y, totalLen, rect.height), outer);
            }

            if (measuredPositive)
            {
                Widgets.DrawBoxSolid(new Rect(midX, rect.y, measuredLen, rect.height), inner);
            }
            else
            {
                Widgets.DrawBoxSolid(new Rect(midX - measuredLen, rect.y, measuredLen, rect.height), inner);
            }
        }

        private static string FormatValue(double v)
        {
            return v.ToString("F0");
        }
    }
}
