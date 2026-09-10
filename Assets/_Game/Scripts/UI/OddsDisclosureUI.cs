using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace JetFighter.UI
{
    /// <summary>
    /// Odds disclosure for randomized paid content.
    ///
    /// Ships dormant on purpose (ADR-006). Nothing randomized is sold at
    /// launch, so this renders for nobody -- and building it anyway is the
    /// decision, not an oversight. Apple Guideline 3.1.1 requires published
    /// odds for randomized paid content, and the expensive moment to discover
    /// there is no disclosure UI is during review, with a build submitted and
    /// a release date already communicated.
    ///
    /// It is dormant, not absent, and those differ: a dormant component is
    /// tested, rendered in a harness, and can be pointed at a real item in an
    /// afternoon. An absent one is a week of work under review pressure.
    ///
    /// Deliberately not wired to any live purchase flow. A disclosure attached
    /// to nothing cannot go stale; one attached to a flow that later changes
    /// its odds silently becomes a false statement, which is worse than
    /// missing.
    /// </summary>
    public class OddsDisclosureUI : MonoBehaviour
    {
        /// <summary>One outcome and its published probability.</summary>
        [Serializable]
        public struct Entry
        {
            public string outcomeName;

            [Tooltip("Relative weight, matching the drop table's units. Converted to a percentage for display.")]
            [Min(0f)]
            public float weight;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        [Tooltip("Decimal places shown. Guideline 3.1.1 wants odds a player can act on, not marketing rounding.")]
        [Range(0, 4)]
        [SerializeField] private int decimalPlaces = 2;

        /// <summary>Whether anything randomized is actually being sold. False at launch (ADR-006).</summary>
        public bool IsDormant => entries.Count == 0;

        public IReadOnlyList<Entry> Entries => entries;

        public int DecimalPlaces
        {
            get => decimalPlaces;
            set => decimalPlaces = Mathf.Clamp(value, 0, 4);
        }

        public void SetEntries(IEnumerable<Entry> newEntries)
        {
            entries.Clear();
            if (newEntries != null)
            {
                entries.AddRange(newEntries);
            }
        }

        /// <summary>Sum of every usable weight. Zero means nothing to disclose.</summary>
        public float TotalWeight
        {
            get
            {
                float total = 0f;
                foreach (Entry entry in entries)
                {
                    if (entry.weight > 0f && !string.IsNullOrWhiteSpace(entry.outcomeName))
                    {
                        total += entry.weight;
                    }
                }
                return total;
            }
        }

        /// <summary>
        /// Percentage for one outcome, as it will be displayed.
        ///
        /// Rounded to the configured places, and computed from the same
        /// weights the drop table uses so the disclosure cannot drift from the
        /// behaviour. A disclosure that disagrees with the game is a
        /// compliance problem, not a display bug.
        /// </summary>
        public float PercentFor(Entry entry)
        {
            float total = TotalWeight;
            if (total <= 0f || entry.weight <= 0f)
            {
                return 0f;
            }
            float raw = entry.weight / total * 100f;
            float scale = Mathf.Pow(10f, decimalPlaces);
            return Mathf.Round(raw * scale) / scale;
        }

        /// <summary>
        /// The disclosure text.
        ///
        /// Built as a string rather than assembled by a prefab so it can be
        /// asserted in a test harness while the component is dormant, which is
        /// this cell's whole acceptance criterion.
        /// </summary>
        public string BuildDisclosureText()
        {
            if (IsDormant)
            {
                // Not an error and not blank: a screen that renders nothing
                // looks broken, and a reviewer opening this component should
                // see why it is empty.
                return "No randomized items are offered.";
            }

            var builder = new StringBuilder();
            builder.AppendLine("Chance of each outcome:");
            foreach (Entry entry in entries)
            {
                if (entry.weight <= 0f || string.IsNullOrWhiteSpace(entry.outcomeName))
                {
                    continue;
                }
                builder.AppendLine($"{entry.outcomeName}: {PercentFor(entry).ToString("F" + decimalPlaces)}%");
            }
            return builder.ToString().TrimEnd();
        }

        /// <summary>
        /// Problems that would make this disclosure non-compliant or
        /// misleading. Empty means it is safe to show.
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            var problems = new List<string>();
            if (IsDormant)
            {
                return problems;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            float displayedTotal = 0f;
            foreach (Entry entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.outcomeName))
                {
                    problems.Add("an outcome has no name, so its odds disclose nothing");
                    continue;
                }
                if (!seen.Add(entry.outcomeName))
                {
                    // Two rows for one outcome understate its real chance,
                    // which is the misleading direction.
                    problems.Add($"duplicate outcome '{entry.outcomeName}'");
                }
                if (entry.weight <= 0f)
                {
                    problems.Add($"'{entry.outcomeName}' has no weight, so it can never be won but is listed");
                }
                displayedTotal += PercentFor(entry);
            }

            // Rounding can make the published percentages sum to something
            // other than 100. A player who adds them up and gets 99.7 has
            // found a real discrepancy, and it is cheaper to catch here than
            // in a review note.
            if (entries.Count > 0 && Mathf.Abs(displayedTotal - 100f) > 0.5f)
            {
                problems.Add($"displayed odds sum to {displayedTotal:F2}%, not 100%");
            }
            return problems;
        }
    }
}
