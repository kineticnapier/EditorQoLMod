using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ADOFAI;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Core
{
    internal sealed class TutorialBackgroundCommand
    {
        public int Floor;
        public bool TileEnabled;
        public bool ShapeEnabled;
        public bool CameraEnabled;
        public Color TileColor;
        public Color ShapeColor;
        public Color CameraColor;
        public float DurationBeats;
        public string Ease;
    }

    internal static class TutorialBackgroundOperations
    {
        internal const string Marker = "[EditorQoL:TutorialBackground]";

        internal static string[] GetEaseNames()
        {
            return new[]
            {
                "Linear", "InSine", "OutSine", "InOutSine",
                "InQuad", "OutQuad", "InOutQuad",
                "InCubic", "OutCubic", "InOutCubic"
            };
        }

        public static string AddOrReplace(scnEditor editor,
            bool tileEnabled, string tileColor,
            bool shapeEnabled, string shapeColor,
            bool cameraEnabled, string cameraColor,
            float durationBeats, string ease)
        {
            if (editor == null) throw new ArgumentNullException("editor");
            if (!tileEnabled && !shapeEnabled && !cameraEnabled)
                throw new InvalidOperationException("変更する対象を1つ以上有効にしてください。");
            if (durationBeats < 0f || float.IsNaN(durationBeats) || float.IsInfinity(durationBeats))
                throw new ArgumentOutOfRangeException("durationBeats", "変化時間は0以上で指定してください。");
            if (!GetEaseNames().Contains(ease)) ease = "Linear";

            FloorRange range = EditorSelection.GetRange(editor, true);
            TutorialBackgroundCommand command = new TutorialBackgroundCommand
            {
                Floor = range.Start,
                TileEnabled = tileEnabled,
                ShapeEnabled = shapeEnabled,
                CameraEnabled = cameraEnabled,
                TileColor = NormalizeColor(tileColor),
                ShapeColor = NormalizeColor(shapeColor),
                CameraColor = NormalizeColor(cameraColor),
                DurationBeats = durationBeats,
                Ease = ease
            };
            string comment = Encode(command);

            using (new EditorUndoScope(editor))
            {
                LevelEvent existing = editor.events.LastOrDefault(x => x.floor == range.Start &&
                    x.eventType == LevelEventType.EditorComment && IsMarkerComment(x));
                if (existing == null)
                {
                    existing = new LevelEvent(range.Start, LevelEventType.EditorComment);
                    editor.events.Add(existing);
                }
                SetEnabled(existing, "comment", comment);
                editor.ApplyEventsToFloors();
            }

            return range.Start + "番タイルにチュートリアル背景色の変更を設定しました。";
        }

        public static string RemoveAtSelection(scnEditor editor)
        {
            if (editor == null) throw new ArgumentNullException("editor");
            FloorRange range = EditorSelection.GetRange(editor, true);
            List<LevelEvent> remove = editor.events.Where(x => x.floor >= range.Start && x.floor <= range.End &&
                x.eventType == LevelEventType.EditorComment && IsMarkerComment(x)).ToList();
            if (remove.Count == 0)
                throw new InvalidOperationException("選択範囲にチュートリアル背景色の設定はありません。");

            using (new EditorUndoScope(editor))
            {
                foreach (LevelEvent evnt in remove) editor.events.Remove(evnt);
                editor.ApplyEventsToFloors();
            }
            return remove.Count + "個のチュートリアル背景色設定を削除しました。";
        }

        internal static bool TryDecode(LevelEvent evnt, out TutorialBackgroundCommand command)
        {
            command = null;
            if (evnt == null || evnt.eventType != LevelEventType.EditorComment) return false;
            object raw;
            if (!evnt.data.TryGetValue("comment", out raw) || raw == null) return false;
            string text = Convert.ToString(raw);
            if (string.IsNullOrEmpty(text) || !text.StartsWith(Marker, StringComparison.Ordinal)) return false;

            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string body = text.Substring(Marker.Length).TrimStart(' ', ';');
            foreach (string part in body.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = part.IndexOf('=');
                if (equals <= 0) continue;
                values[part.Substring(0, equals).Trim()] = part.Substring(equals + 1).Trim();
            }

            Color tile;
            Color shape;
            Color camera;
            if (!TryParseColor(Get(values, "tile", "-"), out tile)) tile = Color.white;
            if (!TryParseColor(Get(values, "shape", "-"), out shape)) shape = Color.white;
            if (!TryParseColor(Get(values, "camera", "-"), out camera)) camera = Color.black;
            float duration;
            if (!float.TryParse(Get(values, "duration", "0"), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out duration)) duration = 0f;
            string ease = Get(values, "ease", "Linear");
            if (!GetEaseNames().Contains(ease)) ease = "Linear";

            command = new TutorialBackgroundCommand
            {
                Floor = evnt.floor,
                TileEnabled = Get(values, "tile", "-") != "-",
                ShapeEnabled = Get(values, "shape", "-") != "-",
                CameraEnabled = Get(values, "camera", "-") != "-",
                TileColor = tile,
                ShapeColor = shape,
                CameraColor = camera,
                DurationBeats = Mathf.Max(0f, duration),
                Ease = ease
            };
            return true;
        }

        private static bool IsMarkerComment(LevelEvent evnt)
        {
            TutorialBackgroundCommand ignored;
            return TryDecode(evnt, out ignored);
        }

        private static string Encode(TutorialBackgroundCommand command)
        {
            return Marker + ";tile=" + (command.TileEnabled ? ColorUtility.ToHtmlStringRGBA(command.TileColor) : "-") +
                   ";shape=" + (command.ShapeEnabled ? ColorUtility.ToHtmlStringRGBA(command.ShapeColor) : "-") +
                   ";camera=" + (command.CameraEnabled ? ColorUtility.ToHtmlStringRGBA(command.CameraColor) : "-") +
                   ";duration=" + command.DurationBeats.ToString("0.###", CultureInfo.InvariantCulture) +
                   ";ease=" + command.Ease;
        }

        private static Color NormalizeColor(string value)
        {
            Color color;
            if (!TryParseColor(value, out color))
                throw new FormatException("色コードが正しくありません: " + value);
            return color;
        }

        private static bool TryParseColor(string value, out Color color)
        {
            string text = (value ?? string.Empty).Trim();
            if (text == "-")
            {
                color = default(Color);
                return false;
            }
            if (!text.StartsWith("#", StringComparison.Ordinal)) text = "#" + text;
            return ColorUtility.TryParseHtmlString(text, out color);
        }

        private static string Get(Dictionary<string, string> values, string key, string fallback)
        {
            string value;
            return values.TryGetValue(key, out value) ? value : fallback;
        }

        private static void SetEnabled(LevelEvent evnt, string key, object value)
        {
            evnt.data[key] = value;
            if (evnt.disabled.ContainsKey(key)) evnt.disabled[key] = false;
        }
    }
}
