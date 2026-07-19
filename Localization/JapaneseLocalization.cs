using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Kiner.ADOFAIEditorQoL
{
    internal static class JapaneseLocalization
    {
        private static readonly Dictionary<string, string> EventNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "SetSpeed", "速度変更" },
            { "Twirl", "旋回" },
            { "Checkpoint", "チェックポイント" },
            { "LevelSettings", "レベル設定" },
            { "SongSettings", "楽曲設定" },
            { "TrackSettings", "トラック設定" },
            { "BackgroundSettings", "背景設定" },
            { "CameraSettings", "カメラ設定" },
            { "MiscSettings", "その他設定" },
            { "EventSettings", "イベント設定" },
            { "DecorationSettings", "装飾設定" },
            { "MoveCamera", "カメラ移動" },
            { "CustomBackground", "背景変更" },
            { "ChangeTrack", "トラック変更" },
            { "ColorTrack", "トラック色変更" },
            { "AnimateTrack", "トラックアニメーション" },
            { "RecolorTrack", "トラック再着色" },
            { "MoveTrack", "トラック移動" },
            { "AddDecoration", "装飾追加" },
            { "AddText", "テキスト追加" },
            { "SetText", "テキスト設定" },
            { "Flash", "フラッシュ" },
            { "SetHitsound", "ヒット音設定" },
            { "SetFilter", "フィルター設定" },
            { "SetFilterAdvanced", "高度なフィルター設定" },
            { "SetPlanetRotation", "惑星回転設定" },
            { "HallOfMirrors", "ミラー効果" },
            { "ShakeScreen", "画面揺れ" },
            { "MoveDecorations", "装飾移動" },
            { "PositionTrack", "トラック位置" },
            { "RepeatEvents", "イベント反復" },
            { "Bloom", "ブルーム" },
            { "Hold", "ホールド" },
            { "SetHoldSound", "ホールド音設定" },
            { "SetConditionalEvents", "条件イベント設定" },
            { "ScreenTile", "画面タイル" },
            { "ScreenScroll", "画面スクロール" },
            { "EditorComment", "エディタコメント" },
            { "Bookmark", "ブックマーク" },
            { "CallMethod", "メソッド呼び出し" },
            { "AddComponent", "コンポーネント追加" },
            { "PlaySound", "音声再生" },
            { "MultiPlanet", "複数惑星" },
            { "FreeRoam", "自由移動" },
            { "FreeRoamTwirl", "自由移動の旋回" },
            { "FreeRoamRemove", "自由移動解除" },
            { "FreeRoamWarning", "自由移動警告" },
            { "Pause", "一時停止" },
            { "AutoPlayTiles", "自動演奏タイル" },
            { "Hide", "非表示" },
            { "ScaleMargin", "余白の拡大縮小" },
            { "ScaleRadius", "半径の拡大縮小" },
            { "Multitap", "マルチタップ" },
            { "TileDimensions", "タイル寸法" },
            { "KillPlayer", "プレイヤー強制失敗" },
            { "ScalePlanets", "惑星サイズ変更" },
            { "SetFloorIcon", "タイルアイコン設定" },
            { "AddObject", "オブジェクト追加" },
            { "SetObject", "オブジェクト設定" },
            { "SetDefaultText", "既定テキスト設定" },
            { "SetFrameRate", "フレームレート設定" },
            { "AddParticle", "パーティクル追加" },
            { "SetParticle", "パーティクル設定" },
            { "EmitParticle", "パーティクル放出" },
            { "SetInputEvent", "入力イベント設定" }
        };

        private static readonly Dictionary<string, string> PropertyNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "beatsPerMinute", "BPM" },
            { "bpmMultiplier", "BPM倍率" },
            { "duration", "継続時間" },
            { "angleOffset", "角度オフセット" },
            { "rotation", "回転角度" },
            { "rotationOffset", "回転オフセット" },
            { "zoom", "ズーム" },
            { "intensity", "強度" },
            { "threshold", "しきい値" },
            { "amount", "量" },
            { "opacity", "不透明度" },
            { "volume", "音量" },
            { "pitch", "ピッチ" },
            { "pan", "左右位置" },
            { "count", "回数" },
            { "interval", "間隔" },
            { "beatsAhead", "前方距離（拍）" },
            { "beatsBehind", "後方距離（拍）" },
            { "floor", "タイル番号" },
            { "startTile", "開始タイル" },
            { "endTile", "終了タイル" },
            { "scale", "拡大率" },
            { "radius", "半径" },
            { "margin", "余白" },
            { "strength", "強さ" },
            { "speed", "速度" },
            { "frequency", "周波数" },
            { "frequencyHz", "周波数（Hz）" },
            { "decay", "減衰" },
            { "iterations", "反復回数" },
            { "repeatCount", "反復回数" },
            { "tileCount", "タイル数" },
            { "frameRate", "フレームレート" },
            { "parallax", "視差" },
            { "depth", "深度" },
            { "maskingDepth", "マスク深度" },
            { "colorAnimationDuration", "色アニメーション時間" },
            { "holdLength", "ホールド長" },
            { "pauseDuration", "停止時間" },
            { "planetCount", "惑星数" },
            { "hitboxSize", "当たり判定サイズ" }
        };

        private static readonly Dictionary<string, string> EnumNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "None", "なし" },
            { "Forward", "前方" },
            { "Backward", "後方" },
            { "Assemble", "組み立て" },
            { "Assemble_Far", "遠方から組み立て" },
            { "Extend", "伸長" },
            { "Grow", "拡大" },
            { "Grow_Spin", "回転しながら拡大" },
            { "Fade", "フェード" },
            { "Drop", "落下" },
            { "Rise", "上昇" },
            { "Scatter", "散開" },
            { "Scatter_Far", "遠方へ散開" },
            { "Retract", "収縮" },
            { "Shrink", "縮小" },
            { "Shrink_Spin", "回転しながら縮小" },
            { "Single", "単色" },
            { "Glow", "発光" },
            { "Blink", "点滅" },
            { "Switch", "切り替え" },
            { "Rainbow", "虹色" },
            { "Volume", "音量連動" }
        };

        public static string EventLabel(string value)
        {
            string translated;
            if (EventNames.TryGetValue(value ?? string.Empty, out translated))
                return translated + "（" + value + "）";
            return SplitIdentifier(value);
        }

        public static string PropertyLabel(string value)
        {
            string translated;
            if (PropertyNames.TryGetValue(value ?? string.Empty, out translated))
                return translated + "（" + value + "）";
            return SplitIdentifier(value) + "（" + value + "）";
        }

        public static string EnumLabel(Type enumType, string value)
        {
            string translated;
            if (EnumNames.TryGetValue(value ?? string.Empty, out translated))
                return translated + "（" + value + "）";
            return SplitIdentifier(value);
        }

        public static IEnumerable<KeyValuePair<string, string>> EventOptions(IEnumerable<string> values)
        {
            return values.Select(value => new KeyValuePair<string, string>(value, EventLabel(value)));
        }

        public static IEnumerable<KeyValuePair<string, string>> PropertyOptions(IEnumerable<string> values)
        {
            return values.Select(value => new KeyValuePair<string, string>(value, PropertyLabel(value)));
        }

        public static IEnumerable<KeyValuePair<string, string>> EnumOptions(Type enumType, IEnumerable<string> values)
        {
            return values.Select(value => new KeyValuePair<string, string>(value, EnumLabel(enumType, value)));
        }

        public static string SplitIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            StringBuilder result = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '_')
                {
                    result.Append(' ');
                    continue;
                }
                if (i > 0 && char.IsUpper(c) && value[i - 1] != '_' && !char.IsUpper(value[i - 1]))
                    result.Append(' ');
                result.Append(c);
            }
            return result.ToString();
        }
    }
}
