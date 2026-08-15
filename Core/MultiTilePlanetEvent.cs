using System;
using System.Collections.Generic;
using System.Globalization;
using ADOFAI;

namespace Kiner.ADOFAIEditorQoL.Core
{
    internal sealed class MultiTilePlanetCommand
    {
        internal int StartFloor;
        internal int EndFloor;
        internal int TileCount;
        internal string TileGroup;
        internal string PlanetGroup;
    }

    /// <summary>
    /// Persistent metadata for the multi-tile planet preview. EditorComment is used as
    /// the carrier because it is inert in an unmodded game and survives save/load without
    /// introducing an unknown LevelEventType into ADOFAI's event dictionaries.
    /// </summary>
    internal static class MultiTilePlanetEvent
    {
        internal const string Marker = "[EditorQoL:MultiTilePlanets:v1]";

        internal static LevelEvent Create(int startFloor, int endFloor, int tileCount,
            string tileGroup, string planetGroup)
        {
            if (tileCount < 2) throw new ArgumentOutOfRangeException("tileCount");
            LevelEvent marker = new LevelEvent(startFloor, LevelEventType.EditorComment);
            string comment = Marker +
                             ";tile=" + tileGroup +
                             ";planet=" + planetGroup +
                             ";end=" + endFloor.ToString(CultureInfo.InvariantCulture) +
                             ";count=" + tileCount.ToString(CultureInfo.InvariantCulture);
            marker.GetEventData()["comment"] = comment;
            if (marker.disabled.ContainsKey("comment")) marker.disabled["comment"] = false;
            return marker;
        }

        internal static bool TryDecode(LevelEvent evnt, out MultiTilePlanetCommand command)
        {
            command = null;
            if (evnt == null || evnt.eventType != LevelEventType.EditorComment) return false;

            object raw;
            if (!evnt.GetEventData().TryGetValue("comment", out raw) || raw == null) return false;
            string text = Convert.ToString(raw);
            if (string.IsNullOrEmpty(text) ||
                !text.StartsWith(Marker, StringComparison.Ordinal)) return false;

            Dictionary<string, string> values = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            string body = text.Substring(Marker.Length).TrimStart(' ', ';');
            foreach (string part in body.Split(new[] { ';' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = part.IndexOf('=');
                if (equals <= 0) continue;
                values[part.Substring(0, equals).Trim()] = part.Substring(equals + 1).Trim();
            }

            string tileGroup;
            string planetGroup;
            int endFloor;
            int tileCount;
            if (!values.TryGetValue("tile", out tileGroup) ||
                !values.TryGetValue("planet", out planetGroup) ||
                !int.TryParse(Get(values, "end"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out endFloor) ||
                !int.TryParse(Get(values, "count"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out tileCount) ||
                string.IsNullOrWhiteSpace(tileGroup) ||
                string.IsNullOrWhiteSpace(planetGroup) ||
                tileCount < 2 || endFloor < evnt.floor)
                return false;

            // Count is authoritative for the decoration sequence. Clamp EndFloor to the
            // matching source range if a hand-edited marker contains inconsistent values.
            int expectedEnd = evnt.floor + tileCount - 1;
            command = new MultiTilePlanetCommand
            {
                StartFloor = evnt.floor,
                EndFloor = Math.Min(endFloor, expectedEnd),
                TileCount = tileCount,
                TileGroup = tileGroup,
                PlanetGroup = planetGroup
            };
            return true;
        }

        private static string Get(IDictionary<string, string> values, string key)
        {
            string value;
            return values.TryGetValue(key, out value) ? value : string.Empty;
        }
    }
}
