using System;
using System.Collections.Generic;
using System.Globalization;
using ADOFAI;
using Kiner.ADOFAIEditorQoL.Core;
using Kiner.ADOFAIEditorQoL.Patches;
using KineticNapier.ADOFAIWorkbench;

namespace Kiner.ADOFAIEditorQoL.UI
{
    internal static class EditorQoLWorkbenchIntegration
    {
        private static readonly EditorQoLWorkbenchPaneProvider Provider = new EditorQoLWorkbenchPaneProvider();
        private static bool registered;

        internal static void Initialize()
        {
            if (registered) return;
            Workbench.RegisterPaneProvider(Provider);
            registered = true;
        }

        internal static void SetEditor(scnEditor editor)
        {
            Provider.Pane.SetEditor(editor);
        }

        internal static void NotifyPerformanceProfileUpdated()
        {
            if (!registered) return;
            Workbench.PublishPane(Provider.Pane.Id);
        }

        internal static void Shutdown()
        {
            if (!registered) return;
            Workbench.UnregisterPaneProvider(Provider);
            registered = false;
        }
    }

    internal sealed class EditorQoLWorkbenchPaneProvider : IDockablePaneProvider
    {
        internal readonly EditorQoLWorkbenchPane Pane = new EditorQoLWorkbenchPane();

        public IEnumerable<IDockablePane> CreatePanes()
        {
            yield return Pane;
        }
    }

    internal sealed class EditorQoLWorkbenchPane : IDockablePane
    {
        private scnEditor editor;
        private bool anglesExpanded = true;
        private bool trackExpanded = true;
        private bool performanceExpanded = true;
        private string multiplier = "0.5";
        private string snapIncrement = "0.01";
        private string trackAppear = FirstEnumValue(typeof(TrackAnimationType));
        private string trackDisappear = FirstEnumValue(typeof(TrackAnimationType2));
        private string beatsAhead = "3";
        private string beatsBehind = "4";
        private string status = "譜面エディタを待っています。";

        public string Id { get { return "editor-qol.tiles"; } }
        public string Title { get { return "Editor QoL: タイル・トラック"; } }
        public bool CanClose { get { return true; } }

        internal void SetEditor(scnEditor value)
        {
            editor = value;
            if (editor != null && editor.levelData != null)
            {
                trackAppear = editor.levelData.trackAnimation.ToString();
                trackDisappear = editor.levelData.trackDisappearAnimation.ToString();
                beatsAhead = editor.levelData.trackBeatsAhead.ToString("0.######", CultureInfo.InvariantCulture);
                beatsBehind = editor.levelData.trackBeatsBehind.ToString("0.######", CultureInfo.InvariantCulture);
                status = "準備完了 v" + ModVersion.Current;
            }
            Workbench.PublishPane(Id);
        }

        public WorkbenchPaneView BuildView()
        {
            WorkbenchPaneView view = new WorkbenchPaneView()
                .Text("タイル・トラック", 16f, true)
                .BeginRow()
                    .Text(SelectionText(), 10f, false)
                    .Button("更新", "refresh", string.Empty, false)
                .EndRow()
                .Spacer(4)
                .BeginSection("相対角度変形", "toggle-section", "angles", anglesExpanded)
                    .Text("999（Midspin）は維持します。", 10f, false)
                    .BeginRow()
                        .Text("倍率", 10f, false)
                        .Input(multiplier, "set-multiplier")
                        .Button("倍率変換", "multiply", string.Empty, false)
                    .EndRow()
                    .Text("例: 0.5で90°→45°、360°→180°", 9f, false)
                    .BeginRow()
                        .Text("刻み角度", 10f, false)
                        .Input(snapIncrement, "set-snap")
                        .Button("丸める", "snap", string.Empty, false)
                    .EndRow()
                    .Button("相対角度列を逆順にする", "reverse", string.Empty, false)
                .EndSection()
                .BeginSection("トラックアニメーション範囲", "toggle-section", "track", trackExpanded)
                    .Text("選択範囲だけに適用し、範囲終了後は元の状態へ戻します。", 10f, false)
                    .Text("出現", 10f, false)
                    .Dropdown(trackAppear, "set-track-appear", Enum.GetNames(typeof(TrackAnimationType)))
                    .Text("消失", 10f, false)
                    .Dropdown(trackDisappear, "set-track-disappear", Enum.GetNames(typeof(TrackAnimationType2)))
                    .BeginRow()
                        .Text("前方", 10f, false)
                        .Input(beatsAhead, "set-beats-ahead")
                        .Text("後方", 10f, false)
                        .Input(beatsBehind, "set-beats-behind")
                    .EndRow()
                    .Button("選択範囲にアニメーションを適用", "apply-track", string.Empty, false)
                .EndSection();

            AppendPerformanceProfile(view);

            view.Spacer(6)
                .Text(status, 10f, false);
            return view;
        }

        public void HandleAction(string actionId, string argument)
        {
            switch (actionId)
            {
                case "refresh": Publish(); return;
                case "toggle-section":
                    if (argument == "angles") anglesExpanded = !anglesExpanded;
                    else if (argument == "track") trackExpanded = !trackExpanded;
                    else if (argument == "performance") performanceExpanded = !performanceExpanded;
                    Publish();
                    return;
                case "clear-performance":
                    LargeLevelLoadProfiler.ClearLatest();
                    Publish();
                    return;
                case "set-multiplier": multiplier = argument; Publish(); return;
                case "set-snap": snapIncrement = argument; Publish(); return;
                case "set-track-appear": trackAppear = argument; Publish(); return;
                case "set-track-disappear": trackDisappear = argument; Publish(); return;
                case "set-beats-ahead": beatsAhead = argument; Publish(); return;
                case "set-beats-behind": beatsBehind = argument; Publish(); return;
                case "multiply":
                    Run(delegate
                    {
                        return TileTransformOperations.MultiplyRelativeAngles(RequireEditor(),
                            ParseFloat(multiplier, 0.000001f, 100000f));
                    });
                    return;
                case "snap":
                    Run(delegate
                    {
                        return TileTransformOperations.SnapAngles(RequireEditor(),
                            ParseFloat(snapIncrement, 0.000001f, 360f));
                    });
                    return;
                case "reverse":
                    Run(delegate { return TileTransformOperations.ReverseRelativeAngles(RequireEditor()); });
                    return;
                case "apply-track":
                    Run(delegate
                    {
                        return TrackAnimationRangeOperations.ApplyToSelection(RequireEditor(),
                            ParseEnum<TrackAnimationType>(trackAppear),
                            ParseEnum<TrackAnimationType2>(trackDisappear),
                            ParseFloat(beatsAhead, 0f, 100000f),
                            ParseFloat(beatsBehind, 0f, 100000f));
                    });
                    return;
            }
        }

        private void AppendPerformanceProfile(WorkbenchPaneView view)
        {
            view.BeginSection("大規模譜面プロファイラ", "toggle-section", "performance", performanceExpanded);

            LargeLevelPerformanceSnapshot snapshot = LargeLevelLoadProfiler.Latest;
            if (snapshot == null)
            {
                view.Text("10,000タイル以上のフル RemakePath を待っています。", 10f, false)
                    .Text("譜面を開き直すと自動で更新されます。", 9f, false);
            }
            else
            {
                view.Text("最新計測: " + snapshot.FloorCount.ToString("N0", CultureInfo.InvariantCulture) + " タイル", 10f, true)
                    .Text(PerformanceLine("RemakePath 合計", snapshot.RemakePathMs, 1), 10f, false)
                    .Text(PerformanceLine("MakeLevel", snapshot.MakeLevelMs, snapshot.MakeLevelCalls), 10f, false)
                    .Text(PerformanceLine("└ InstantiateFloatFloors", snapshot.InstantiateFloatFloorsMs, snapshot.InstantiateFloatFloorsCalls), 10f, false);

                if (LargeLevelFloorCreationDiagnostics.LastFastPathUsed)
                {
                    view.Text("  高速床生成: 使用（開始時 " +
                              LargeLevelFloorCreationDiagnostics.LastInitialFloorCount.ToString("N0", CultureInfo.InvariantCulture) + " 床）", 9f, true)
                        .Text(PerformanceLine("  ├ Object.Instantiate<scrFloor>",
                            LargeLevelFloorCreationDiagnostics.LastSpawnMs,
                            LargeLevelFloorCreationDiagnostics.LastSpawnCalls), 9f, false)
                        .Text(PerformanceLine("  └ scrFloor.Awake 累計",
                            LargeLevelFloorCreationDiagnostics.LastAwakeMs,
                            LargeLevelFloorCreationDiagnostics.LastAwakeCalls), 9f, false);
                }
                else
                {
                    view.Text("  高速床生成: 未使用", 9f, false);
                }

                view.Text(PerformanceLine("ApplyEventsToFloors", snapshot.ApplyEventsToFloorsMs, snapshot.ApplyEventsToFloorsCalls), 10f, false)
                    .Text(PerformanceLine("└ ApplyCoreEventsToFloors", snapshot.ApplyCoreEventsToFloorsMs, snapshot.ApplyCoreEventsToFloorsCalls), 10f, false)
                    .Text(PerformanceLine("└ CalculateFloorEntryTimes", snapshot.CalculateFloorEntryTimesMs, snapshot.CalculateFloorEntryTimesCalls), 10f, false)
                    .Text(PerformanceLine("DrawHolds", snapshot.DrawHoldsMs, snapshot.DrawHoldsCalls), 10f, false)
                    .Text(PerformanceLine("DrawMultiPlanet", snapshot.DrawMultiPlanetMs, snapshot.DrawMultiPlanetCalls), 10f, false)
                    .Text("※ 内訳は包含関係があるため、単純加算しても合計にはなりません。", 9f, false);
            }

            view.Button("計測結果をクリア", "clear-performance", string.Empty, false)
                .EndSection();
        }

        private static string PerformanceLine(string label, double milliseconds, int calls)
        {
            string time;
            if (milliseconds >= 1000.0)
            {
                time = (milliseconds / 1000.0).ToString("0.000", CultureInfo.InvariantCulture) + " s";
            }
            else
            {
                time = milliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms";
            }

            if (calls > 1)
            {
                time += " / " + calls.ToString(CultureInfo.InvariantCulture) + " 回";
            }
            return label + ": " + time;
        }

        private void Run(Func<string> action)
        {
            try
            {
                status = action();
            }
            catch (Exception ex)
            {
                status = "エラー: " + ex.Message;
                if (Main.Logger != null) Main.Logger.Error("Workbench pane action failed: " + ex);
            }
            Publish();
        }

        private scnEditor RequireEditor()
        {
            if (editor == null) throw new InvalidOperationException("譜面エディタが開かれていません。");
            return editor;
        }

        private string SelectionText()
        {
            try
            {
                FloorRange range = EditorSelection.GetRange(editor, true);
                return "選択: " + range.Start + "～" + range.End + "（" + range.Count + "タイル）";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private void Publish()
        {
            Workbench.PublishPane(Id);
        }

        private static float ParseFloat(string text, float min, float max)
        {
            float value;
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                value < min || value > max || float.IsNaN(value) || float.IsInfinity(value))
                throw new FormatException(min.ToString(CultureInfo.InvariantCulture) + " 以上 " +
                    max.ToString(CultureInfo.InvariantCulture) + " 以下の数値を入力してください。");
            return value;
        }

        private static T ParseEnum<T>(string text) where T : struct
        {
            T value;
            if (!Enum.TryParse(text, out value)) throw new FormatException("不明な選択肢です: " + text);
            return value;
        }

        private static string FirstEnumValue(Type type)
        {
            string[] values = Enum.GetNames(type);
            return values.Length == 0 ? string.Empty : values[0];
        }
    }
}
