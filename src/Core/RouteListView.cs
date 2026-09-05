using System;
using System.Collections.Generic;

namespace RouteRunner.Core
{
    public sealed class RouteRow
    {
        public readonly RouteSummary Entry;
        public readonly string Label;
        public RouteRow(RouteSummary entry)
        {
            Entry = entry;
            Label = entry.Name + "  |  " + entry.Map + "  |  " + entry.Duration.ToString("0.00") + "s";
        }
    }
    public sealed class RouteListView
    {
        public const int Limit = 200;
        public readonly List<RouteRow> Rows = new List<RouteRow>(Limit);
        public bool HasMore { get; private set; }
        IReadOnlyList<RouteSummary> source;
        string map, search;
        bool allMaps;

        public bool Refresh(IReadOnlyList<RouteSummary> entries, string currentMap, string query, bool showAll)
        {
            if (ReferenceEquals(source, entries) && map == currentMap && search == query && allMaps == showAll) return false;
            source = entries; map = currentMap; search = query; allMaps = showAll;
            Rows.Clear(); HasMore = false;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (!showAll && entry.Map != currentMap) continue;
                if (query.Length != 0 && (entry.Name + " " + entry.Map).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (Rows.Count == Limit) { HasMore = true; break; }
                Rows.Add(new RouteRow(entry));
            }
            return true;
        }
    }
}
