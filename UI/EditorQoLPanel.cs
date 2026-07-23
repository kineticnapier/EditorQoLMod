using System;
using System.Collections.Generic;
using System.Linq;
using ADOFAI;
using Kiner.ADOFAIEditorQoL.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Kiner.ADOFAIEditorQoL.UI
{
    internal sealed class EditorQoLPanel : MonoBehaviour
    {
        private sealed class CommandEntry
        {
            internal string Label;
            internal string Category;
            internal string Section;
            internal Button Button;
            internal SectionGroup Group;

            internal string Id
            {
                get { return Category + "|" + (Section ?? string.Empty) + "|" + Label; }
            }

            internal string DisplayName
            {
                get
                {
                    return string.IsNullOrEmpty(Section) ? Label : Section + " › " + Label;
                }
            }
        }

        private sealed class SectionGroup
        {
            internal string Id;
            internal string Category;
            internal string Title;
            internal Button Header;
            internal TMP_Text HeaderText;
            internal GameObject Content;
            internal bool Collapsed;
        }

        private static EditorQoLPanel instance;

        internal static bool CapturesKeyboard
        {
            get { return instance != null && instance.ShouldCaptureKeyboard(); }
        }

        internal static bool CapturesMouse
        {
            get { return instance != null && instance.ShouldCaptureMouse(); }
        }

        internal static bool IsQoLShowing
        {
            get { return instance != null && instance.showingQoL; }
        }

        internal static void RestoreAfterUndo(scnEditor editor, bool reopen)
        {
            if (!Main.Enabled || editor == null) return;
            Attach(editor);
            if (reopen && instance != null) instance.reopenAfterUndoFrames = 2;
        }

        private scnEditor editor;
        private InspectorPanel inspector;
        private RectTransform inspectorRect;

        private GameObject tabObject;
        private InspectorTab tabVisual;
        private Button tabButton;
        private GameObject contentRoot;
        private bool showingQoL;

        private readonly List<TMP_InputField> inputFields = new List<TMP_InputField>();
        private readonly List<NativeDropdown> dropdowns = new List<NativeDropdown>();
        private readonly List<CommandEntry> commandEntries = new List<CommandEntry>();
        private readonly Dictionary<string, SectionGroup> sectionGroups =
            new Dictionary<string, SectionGroup>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<GameObject>> categoryItems =
            new Dictionary<string, List<GameObject>>(StringComparer.Ordinal);
        private ScrollRect contentScroll;
        private NativeDropdown toolCategory;
        private string buildingCategory;
        private string buildingSection;
        private Transform buildingSectionParent;
        private Transform buildingSectionContent;
        private SectionGroup buildingSectionGroup;
        private int categoryStartChildIndex;
        private TMP_InputField commandSearch;
        private GameObject commandSearchResults;
        private GameObject quickFavorites;
        private GameObject quickRecents;
        private RectTransform pendingCommandTarget;
        private int pendingCommandScrollFrames;
        private Toggle operationPreviewToggle;
        private GameObject operationPreviewBox;
        private TMP_Text operationPreviewText;
        private Func<string> pendingOperation;
        private CommandEntry pendingOperationCommand;
        private string pendingOperationScope;
        private float pendingOperationScrollPosition;
        private bool hasPendingOperationScrollPosition;
        private CommandEntry activeCommand;
        private TMP_Text operationHistoryText;

        private TMP_Text status;
        private TMP_Text selection;
        private TMP_InputField repeatCount;
        private NativeDropdown duplicateMode;
        private TMP_InputField pattern;
        private TMP_InputField moveDestination;
        private TMP_InputField rotateDelta;
        private TMP_InputField replaceAngleFrom;
        private TMP_InputField replaceAngleTo;
        private TMP_InputField replaceAngleTolerance;
        private TMP_InputField angleMultiplier;
        private TMP_InputField snapAngle;
        private TMP_InputField decorationNudgeX;
        private TMP_InputField decorationNudgeY;
        private NativeDropdown decorationInterpolateProperty;
        private TMP_InputField multiTileGroup;
        private Toggle multiTilePlanets;
        private NativeDropdown eventType;
        private TMP_InputField eventOffset;
        private NativeDropdown shiftOutOfRangeMode;
        private TMP_InputField eventInterval;
        private TMP_InputField eventPhase;
        private Toggle overwriteSoloToggle;
        private NativeDropdown eventPasteMode;
        private NativeDropdown eventStateOperation;
        private NativeDropdown interpolationEventType;
        private NativeDropdown interpolationProperty;
        private TMP_InputField interpolationStart;
        private TMP_InputField interpolationEnd;
        private TMP_InputField presetName;
        private NativeDropdown presetDropdown;
        private TMP_InputField presetInterval;
        private TMP_InputField floorNumber;
        private TMP_InputField relativeJump;
        private TMP_InputField selectCount;
        private NativeDropdown bookmarkDropdown;
        private TMP_InputField selectionRangeName;
        private NativeDropdown selectionRangeDropdown;
        private NativeDropdown eventSearchType;
        private Toggle eventSearchDecorationsToggle;
        private TMP_InputField eventSearchPage;
        private NativeDropdown eventSearchDropdown;
        private TMP_Text eventSearchSummary;
        private Toggle decorationsToggle;
        private Toggle statsSelectionToggle;
        private TMP_Text statisticsText;
        private TMP_Text diagnosticsText;

        private TMP_InputField scrollX;
        private TMP_InputField scrollY;
        private TMP_InputField scrollBeats;
        private NativeDropdown trackAppear;
        private NativeDropdown trackDisappear;
        private TMP_InputField trackBeatsAhead;
        private TMP_InputField trackBeatsBehind;
        private NativeDropdown waveDirection;
        private NativeDropdown waveColorType;
        private NativeDropdown waveEase;
        private TMP_InputField wavePrimary;
        private TMP_InputField waveSecondary;
        private TMP_InputField waveInterval;
        private TMP_InputField waveDuration;
        private TMP_InputField waveColorAnimDuration;
        private NativeDropdown formulaEventType;
        private NativeDropdown formulaProperty;
        private TMP_InputField formulaExpression;
        private TMP_InputField formulaVariables;
        private TMP_Text favoriteCurrent;
        private TMP_Text favoriteList;
        private NativeDropdown textMaskMode;
        private NativeDropdown systemFont;
        private TMP_InputField systemFontSearch;
        private string[] systemFontNames = new string[0];
        private Toggle tutorialTileToggle;
        private Toggle tutorialShapeToggle;
        private Toggle tutorialCameraToggle;
        private TMP_InputField tutorialTileColor;
        private TMP_InputField tutorialShapeColor;
        private TMP_InputField tutorialCameraColor;
        private TMP_InputField tutorialDuration;
        private NativeDropdown tutorialEase;
        private int reopenAfterUndoFrames;

        private bool IsOpen
        {
            get
            {
                return Main.Enabled && showingQoL && contentRoot != null && contentRoot.activeInHierarchy &&
                       inspector != null && inspector.showInspector;
            }
        }

        internal static void Attach(scnEditor editor)
        {
            if (editor == null) return;
            if (instance != null)
            {
                bool sameEditor = instance.editor == editor;
                bool uiAlive = instance.tabObject != null && instance.contentRoot != null &&
                               instance.inspector == editor.settingsPanel;
                if (sameEditor && uiAlive) return;
                DestroyCurrent();
            }

            if (editor.settingsPanel == null)
            {
                Main.Logger.Error("左側の設定パネルが見つかりませんでした。");
                return;
            }

            GameObject host = new GameObject("ADOFAI Editor QoL Host");
            Transform hostParent = editor.settingsPanel.transform.parent != null
                ? editor.settingsPanel.transform.parent
                : editor.settingsPanel.transform;
            host.transform.SetParent(hostParent, false);
            instance = host.AddComponent<EditorQoLPanel>();
            instance.editor = editor;
            instance.inspector = editor.settingsPanel;
            instance.inspectorRect = editor.settingsPanel.GetComponent<RectTransform>();
            instance.Build();
        }

        internal static void DestroyCurrent()
        {
            if (instance != null) UnityEngine.Object.Destroy(instance.gameObject);
            instance = null;
        }

        internal static void SetModEnabled(bool enabled)
        {
            if (instance == null) return;
            if (instance.tabObject != null) instance.tabObject.SetActive(enabled);
            if (!enabled) instance.HideQoL(true);
        }

        internal static void HandleRegularInspectorPanel(InspectorPanel panel)
        {
            if (instance == null || panel == null || panel != instance.inspector) return;
            if (instance.showingQoL) instance.HideQoL(false);
        }

        private void Build()
        {
            if (editor == null || inspector == null || inspector.tabs == null || inspector.panels == null ||
                editor.notificationOkButton == null)
            {
                Main.Logger.Error("必要なエディタUIを取得できませんでした。");
                return;
            }

            BuildInspectorTab();
            BuildContentPanel();
            RefreshSelection();
            SetStatus("準備完了 v" + ModVersion.Current);
            HideQoL(false);
        }

        private void BuildInspectorTab()
        {
            InspectorTab[] currentTabs = inspector.tabs.GetComponentsInChildren<InspectorTab>(true);
            InspectorTab template = currentTabs.FirstOrDefault();
            if (template == null)
                throw new InvalidOperationException("設定タブのひな形が見つかりませんでした。");

            int index = currentTabs.Length;
            tabObject = Instantiate(template.gameObject, inspector.tabs, false);
            tabObject.name = "Editor QoL Inspector Tab";
            tabObject.SetActive(true);
            tabObject.transform.SetAsLastSibling();

            RectTransform tabRect = tabObject.GetComponent<RectTransform>();
            tabRect.anchoredPosition = new Vector2(tabRect.anchoredPosition.x, -68f * index);

            tabVisual = tabObject.GetComponent<InspectorTab>();
            if (tabVisual != null)
            {
                tabVisual.enabled = false;
                tabVisual.levelEventType = LevelEventType.None;
                tabVisual.panel = inspector;
                if (tabVisual.cycleButtons != null) tabVisual.cycleButtons.gameObject.SetActive(false);
                if (tabVisual.icon != null) tabVisual.icon.enabled = false;
                tabButton = tabVisual.button;
            }
            if (tabButton == null) tabButton = tabObject.GetComponentInChildren<Button>(true);
            if (tabButton == null) throw new InvalidOperationException("設定タブのボタンが見つかりませんでした。");

            tabButton.onClick.RemoveAllListeners();
            tabButton.onClick.AddListener(ToggleQoL);

            GameObject labelObject = new GameObject("QoL Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(tabObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            Stretch(labelRect, 0f, 0f, 0f, 0f);
            TMP_Text label = labelObject.GetComponent<TMP_Text>();
            label.text = "Q";
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 28f;
            label.fontStyle = FontStyles.Bold;
            label.color = Color.white;
            TMP_Text templateText = editor.notificationOkButton.GetComponentInChildren<TMP_Text>(true);
            if (templateText != null) label.font = templateText.font;
            label.raycastTarget = false;

            SetTabSelected(false);
        }

        private void BuildContentPanel()
        {
            contentRoot = new GameObject("Editor QoL Properties Panel", typeof(RectTransform), typeof(ScrollRect));
            contentRoot.transform.SetParent(inspector.panels, false);
            contentRoot.transform.SetAsLastSibling();
            RectTransform rootRect = contentRoot.GetComponent<RectTransform>();
            Stretch(rootRect, 0f, 0f, 0f, 0f);

            GameObject viewport = CreateObject("Viewport", contentRoot.transform);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            Stretch(viewportRect, 6f, 6f, 6f, 6f);
            viewport.AddComponent<RectMask2D>();

            GameObject content = CreateVertical(viewport.transform, "Content", 7f, 10, 10, 10, 14);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;
            ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            contentScroll = contentRoot.GetComponent<ScrollRect>();
            contentScroll.viewport = viewportRect;
            contentScroll.content = contentRect;
            contentScroll.horizontal = false;
            contentScroll.vertical = true;
            contentScroll.inertia = true;
            contentScroll.decelerationRate = 0.12f;
            contentScroll.movementType = ScrollRect.MovementType.Clamped;
            contentScroll.scrollSensitivity = 45f;

            // Ensure the otherwise transparent viewport participates in UI raycasts.
            Image viewportHitArea = viewport.AddComponent<Image>();
            viewportHitArea.color = new Color(0f, 0f, 0f, 0.001f);
            viewportHitArea.raycastTarget = true;

            selection = CreateText(content.transform, "", 13f, FontStyles.Normal, TextAlignmentOptions.Left);

            CreateText(content.transform, "機能検索（Ctrl+K）", 12f, FontStyles.Bold,
                TextAlignmentOptions.Left);
            commandSearch = CreateInput(content.transform, false, 40f);
            TMP_Text commandPlaceholder = commandSearch.placeholder as TMP_Text;
            if (commandPlaceholder != null) commandPlaceholder.text = "機能名を入力してEnter";
            commandSearch.onValueChanged.AddListener(RefreshCommandSearchResults);
            commandSearch.onSubmit.AddListener(OpenFirstMatchingCommand);
            commandSearchResults = CreateVertical(content.transform, "Command search results", 4f, 0, 0, 0, 0);
            commandSearchResults.SetActive(false);

            CreateText(content.transform, "クイックアクセス", 12f, FontStyles.Bold,
                TextAlignmentOptions.Left);
            CreateText(content.transform, "検索候補や履歴の☆でお気に入り登録", 10f, FontStyles.Normal,
                TextAlignmentOptions.Left);
            CreateText(content.transform, "★ お気に入り", 11f, FontStyles.Normal,
                TextAlignmentOptions.Left);
            quickFavorites = CreateVertical(content.transform, "Favorite commands", 4f, 0, 0, 0, 0);
            CreateText(content.transform, "最近使った機能", 11f, FontStyles.Normal,
                TextAlignmentOptions.Left);
            quickRecents = CreateVertical(content.transform, "Recent commands", 4f, 0, 0, 0, 0);

            operationPreviewToggle = CreateToggle(content.transform, "譜面書き換え前にプレビュー",
                EditorQoLPreferences.PreviewEnabled, 245f);
            operationPreviewToggle.onValueChanged.AddListener(delegate(bool value)
            {
                EditorQoLPreferences.PreviewEnabled = value;
                if (!value) CancelPendingOperation(false);
            });
            operationPreviewBox = CreateVertical(content.transform, "Operation preview", 5f, 8, 8, 6, 6);
            operationPreviewText = CreateText(operationPreviewBox.transform, "", 11f, FontStyles.Normal,
                TextAlignmentOptions.TopLeft);
            GameObject previewButtons = CreateHorizontal(operationPreviewBox.transform,
                "Operation preview buttons", 5f, 38f);
            Button confirmPreview = CreatePlainButton(previewButtons.transform, "確定して実行", 0f, 38f);
            confirmPreview.onClick.AddListener(ConfirmPendingOperation);
            Button cancelPreview = CreatePlainButton(previewButtons.transform, "キャンセル", 0f, 38f);
            cancelPreview.onClick.AddListener(delegate { CancelPendingOperation(true); });
            operationPreviewBox.SetActive(false);

            CreateText(content.transform, "機能カテゴリ", 12f, FontStyles.Bold, TextAlignmentOptions.Left);
            toolCategory = CreateDropdown(content.transform, new[]
            {
                new KeyValuePair<string, string>("Tiles", "タイル・トラック"),
                new KeyValuePair<string, string>("Visuals", "装飾・見た目"),
                new KeyValuePair<string, string>("Events", "イベント編集"),
                new KeyValuePair<string, string>("Utility", "移動・情報")
            }, 40f);
            toolCategory.ValueChanged += ApplyCategoryFilter;

            GameObject sectionButtons = CreateHorizontal(content.transform, "Section visibility buttons", 5f, 38f);
            Button collapseAll = CreatePlainButton(sectionButtons.transform, "全て閉じる", 0f, 38f);
            collapseAll.onClick.AddListener(delegate { SetAllSectionsCollapsed(true); });
            Button expandAll = CreatePlainButton(sectionButtons.transform, "全て開く", 0f, 38f);
            expandAll.onClick.AddListener(delegate { SetAllSectionsCollapsed(false); });

            BeginCategory(content.transform, "Tiles");
            Section(content.transform, "パターン・タイル変形");
            CreateText(content.transform, "選択範囲を繰り返す", 12f, FontStyles.Normal, TextAlignmentOptions.Left);
            GameObject repeatRow = CreateHorizontal(content.transform, "Repeat values", 6f, 40f);
            CreateText(repeatRow.transform, "追加回数", 12f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            repeatCount = CreateInput(repeatRow.transform, false, 40f);
            repeatCount.text = "1";
            SetWidth(repeatCount.gameObject, 62f);
            duplicateMode = CreateDropdown(content.transform, new[]
            {
                new KeyValuePair<string, string>(FloorDuplicateMode.TilesOnly.ToString(), "タイルのみ"),
                new KeyValuePair<string, string>(FloorDuplicateMode.TilesAndEvents.ToString(), "タイル＋イベント"),
                new KeyValuePair<string, string>(FloorDuplicateMode.TilesEventsDecorations.ToString(), "タイル＋イベント＋装飾")
            }, 40f);
            duplicateMode.SetValue(FloorDuplicateMode.TilesEventsDecorations.ToString());
            Button repeat = CreateButton(content.transform, "選択範囲を複製", 0f, 40f);
            repeat.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    return TileTransformOperations.RepeatSelection(editor,
                        ParseInt(repeatCount.text, 1, 1000), DropdownEnum<FloorDuplicateMode>(duplicateMode));
                });
            });
            CreateText(content.transform, "床選択中のCtrl+Dは、イベント・装飾込みで直後へ1回複製します。", 10f,
                FontStyles.Normal, TextAlignmentOptions.Left);

            CreateText(content.transform, "相対角度の例: 90*16 / (45,45,90)*8 / 90*4,twirl,90*4", 11f, FontStyles.Normal, TextAlignmentOptions.Left);
            pattern = CreateInput(content.transform, true, 58f);
            pattern.text = "90*8";
            Button insert = CreateButton(content.transform, "選択範囲の後へ挿入", 0f, 40f);
            insert.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    return PatternOperations.InsertRelativePatternAfterSelection(editor, PatternParser.Parse(pattern.text));
                });
            });
            GameObject replaceDeleteRow = CreateHorizontal(content.transform, "Replace or delete tiles", 5f, 40f);
            Button replaceTiles = CreateButton(replaceDeleteRow.transform, "選択範囲を入力パターンで置換", 0f, 40f);
            replaceTiles.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    return TileTransformOperations.ReplaceSelectionWithPattern(editor, PatternParser.Parse(pattern.text));
                });
            });
            Button deleteTiles = CreateButton(replaceDeleteRow.transform, "削除", 82f, 40f);
            deleteTiles.onClick.AddListener(delegate
            {
                Run(delegate { return TileTransformOperations.DeleteSelection(editor); });
            });

            CreateText(content.transform, "選択範囲を別の位置へ移動", 12f, FontStyles.Normal, TextAlignmentOptions.Left);
            GameObject moveRow = CreateHorizontal(content.transform, "Move selection", 6f, 40f);
            CreateText(moveRow.transform, "この床の後へ", 12f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            moveDestination = CreateInput(moveRow.transform, false, 40f);
            moveDestination.text = "0";
            Button moveSelection = CreateButton(moveRow.transform, "移動", 90f, 40f);
            moveSelection.onClick.AddListener(delegate
            {
                Run(delegate { return TileTransformOperations.MoveSelectionAfter(editor, ParseInt(moveDestination.text, 0, int.MaxValue)); });
            });

            CreateText(content.transform, "絶対方向をまとめて変形します。999（Midspin）は変更しません。", 11f,
                FontStyles.Normal, TextAlignmentOptions.Left);
            GameObject rotateRow = CreateHorizontal(content.transform, "Rotate selection", 6f, 40f);
            CreateText(rotateRow.transform, "回転角度", 12f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            rotateDelta = CreateInput(rotateRow.transform, false, 40f);
            rotateDelta.text = "45";
            Button rotateSelection = CreateButton(rotateRow.transform, "回転", 90f, 40f);
            rotateSelection.onClick.AddListener(delegate
            {
                Run(delegate { return TileTransformOperations.RotateSelection(editor, ParseFloat(rotateDelta.text, -100000f, 100000f)); });
            });

            GameObject replaceRow = CreateHorizontal(content.transform, "Replace angles", 5f, 40f);
            replaceAngleFrom = CreateInput(replaceRow.transform, false, 40f);
            replaceAngleFrom.text = "45";
            replaceAngleTo = CreateInput(replaceRow.transform, false, 40f);
            replaceAngleTo.text = "60";
            replaceAngleTolerance = CreateInput(replaceRow.transform, false, 40f);
            replaceAngleTolerance.text = "0.001";
            Button replaceAngles = CreateButton(replaceRow.transform, "置換", 78f, 40f);
            replaceAngles.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    return TileTransformOperations.ReplaceAngles(editor,
                        ParseFloat(replaceAngleFrom.text, -100000f, 100000f),
                        ParseFloat(replaceAngleTo.text, -100000f, 100000f),
                        ParseFloat(replaceAngleTolerance.text, 0f, 360f));
                });
            });
            CreateText(content.transform, "左から: 置換元 / 置換先 / 許容誤差", 10f, FontStyles.Normal, TextAlignmentOptions.Left);

            GameObject multiplyAngleRow = CreateHorizontal(content.transform, "Multiply relative angles", 5f, 40f);
            CreateText(multiplyAngleRow.transform, "相対角度の倍率", 12f, FontStyles.Normal,
                TextAlignmentOptions.MidlineLeft);
            angleMultiplier = CreateInput(multiplyAngleRow.transform, false, 40f);
            angleMultiplier.text = "0.5";
            SetWidth(angleMultiplier.gameObject, 72f);
            Button multiplyAngles = CreateButton(multiplyAngleRow.transform, "倍率変換", 100f, 40f);
            multiplyAngles.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    return TileTransformOperations.MultiplyRelativeAngles(editor,
                        ParseFloat(angleMultiplier.text, 0.000001f, 100000f));
                });
            });
            CreateText(content.transform, "例: 0.5で90°→45°、360°→180°。999（Midspin）は維持します。", 10f,
                FontStyles.Normal, TextAlignmentOptions.Left);

            GameObject snapReverseRow = CreateHorizontal(content.transform, "Snap and reverse angles", 5f, 40f);
            snapAngle = CreateInput(snapReverseRow.transform, false, 40f);
            snapAngle.text = "0.01";
            Button snapAngles = CreateButton(snapReverseRow.transform, "指定角度刻みに丸める", 0f, 40f);
            snapAngles.onClick.AddListener(delegate
            {
                Run(delegate { return TileTransformOperations.SnapAngles(editor, ParseFloat(snapAngle.text, 0.000001f, 360f)); });
            });
            Button reverseAngles = CreateButton(content.transform, "相対角度列を逆順にする", 0f, 40f);
            reverseAngles.onClick.AddListener(delegate
            {
                Run(delegate { return TileTransformOperations.ReverseRelativeAngles(editor); });
            });

            Section(content.transform, "トラックアニメーション範囲");
            CreateText(content.transform, "選択範囲だけに適用し、範囲終了後は元の状態へ戻します。", 11f, FontStyles.Normal, TextAlignmentOptions.Left);
            trackAppear = CreateEnumDropdown<TrackAnimationType>(content.transform, 32f);
            trackDisappear = CreateEnumDropdown<TrackAnimationType2>(content.transform, 32f);
            GameObject trackBeatRow = CreateHorizontal(content.transform, "Track beat distances", 6f, 30f);
            CreateText(trackBeatRow.transform, "前方", 11f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            trackBeatsAhead = CreateInput(trackBeatRow.transform, false, 30f);
            trackBeatsAhead.text = "3";
            SetWidth(trackBeatsAhead.gameObject, 54f);
            CreateText(trackBeatRow.transform, "後方", 11f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            trackBeatsBehind = CreateInput(trackBeatRow.transform, false, 30f);
            trackBeatsBehind.text = "4";
            SetWidth(trackBeatsBehind.gameObject, 54f);
            Button applyTrackRange = CreateButton(content.transform, "選択範囲にアニメーションを適用", 0f, 31f);
            applyTrackRange.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    return TrackAnimationRangeOperations.ApplyToSelection(editor,
                        DropdownEnum<TrackAnimationType>(trackAppear), DropdownEnum<TrackAnimationType2>(trackDisappear),
                        ParseFloat(trackBeatsAhead.text, 0f, 100000f), ParseFloat(trackBeatsBehind.text, 0f, 100000f));
                });
            });

            Section(content.transform, "画面スクロール");
            CreateText(content.transform, "指定した拍数で、画面何個分移動するかを入力します。", 11f, FontStyles.Normal, TextAlignmentOptions.Left);
            GameObject scrollDistanceRow = CreateHorizontal(content.transform, "Scroll distance", 6f, 30f);
            CreateText(scrollDistanceRow.transform, "X", 11f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            scrollX = CreateInput(scrollDistanceRow.transform, false, 30f);
            scrollX.text = "1";
            SetWidth(scrollX.gameObject, 62f);
            CreateText(scrollDistanceRow.transform, "Y", 11f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            scrollY = CreateInput(scrollDistanceRow.transform, false, 30f);
            scrollY.text = "0";
            SetWidth(scrollY.gameObject, 62f);
            GameObject scrollBeatRow = CreateHorizontal(content.transform, "Scroll beats", 6f, 30f);
            CreateText(scrollBeatRow.transform, "拍数", 11f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            scrollBeats = CreateInput(scrollBeatRow.transform, false, 30f);
            scrollBeats.text = "4";
            Button addScroll = CreateButton(content.transform, "時間指定スクロールを追加", 0f, 31f);
            addScroll.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    return ScreenScrollOperations.AddTimedScroll(editor, ParseDouble(scrollX.text),
                        ParseDouble(scrollY.text), ParseDouble(scrollBeats.text, 0.000001d, 1000000d));
                });
            });

            BeginCategory(content.transform, "Visuals");
            Section(content.transform, "マルチタイル生成・焼き込み");
            CreateText(content.transform,
                "選択した実タイルをAddObjectの床デコレーションへ変換します。T番号は空欄なら自動採番です。",
                11f, FontStyles.Normal, TextAlignmentOptions.Left);
            multiTileGroup = CreateInput(content.transform, false, 40f);
            TMP_Text multiTileGroupPlaceholder = multiTileGroup.placeholder as TMP_Text;
            if (multiTileGroupPlaceholder != null) multiTileGroupPlaceholder.text = "グループ名（例: T0 / 空欄で自動）";
            multiTilePlanets = CreateToggle(content.transform, "始点に青・赤の惑星も生成", true, 235f);
            Button generateMultiTile = CreateButton(content.transform, "選択タイルからマルチタイル生成", 0f, 40f);
            generateMultiTile.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    string generated;
                    string result = MultiTileOperations.GenerateFromSelectedTiles(editor, multiTileGroup.text,
                        multiTilePlanets.isOn, out generated);
                    multiTileGroup.text = generated;
                    return result;
                });
            });
            CreateText(content.transform,
                "惑星は専用イベントで再生し、MoveDecorationsは生成しません。焼き込みはT番号のtrackAngleを実タイルへ適用し、専用イベントとプレビュー惑星を削除します。",
                10f, FontStyles.Normal, TextAlignmentOptions.Left);
            Button bakeMultiTile = CreateButton(content.transform, "マルチタイルのリズムを実タイルへ焼き込む", 0f, 40f);
            bakeMultiTile.onClick.AddListener(delegate
            {
                Run(delegate { return MultiTileOperations.BakeToSelectedTiles(editor, multiTileGroup.text); });
            });

            Section(content.transform, "床デコレーションの色ウェーブ");
            CreateText(content.transform, "先に床型の「オブジェクト追加」を2個以上選択してください。", 11f, FontStyles.Normal, TextAlignmentOptions.Left);
            waveDirection = CreateDropdown(content.transform, new[]
            {
                new KeyValuePair<string, string>("Forward", "前方"),
                new KeyValuePair<string, string>("Backward", "後方")
            }, 32f);
            waveColorType = CreateEnumDropdown<FloorDecorationColorType>(content.transform, 32f);
            // Ease names stay in English because those identifiers are the familiar notation.
            waveEase = CreateDropdown(content.transform, DecorationColorWaveOperations.GetEaseNames(), 32f);
            GameObject waveColorRow = CreateHorizontal(content.transform, "Wave colors", 6f, 30f);
            wavePrimary = CreateInput(waveColorRow.transform, false, 30f);
            wavePrimary.text = "FFFFFF";
            waveSecondary = CreateInput(waveColorRow.transform, false, 30f);
            waveSecondary.text = "FFFFFF";
            GameObject waveTimingRow = CreateHorizontal(content.transform, "Wave timing", 5f, 30f);
            waveInterval = CreateInput(waveTimingRow.transform, false, 30f);
            waveInterval.text = "0.125";
            waveDuration = CreateInput(waveTimingRow.transform, false, 30f);
            waveDuration.text = "0";
            waveColorAnimDuration = CreateInput(waveTimingRow.transform, false, 30f);
            waveColorAnimDuration.text = "1";
            CreateText(content.transform, "左から: 間隔（拍） / 変化時間 / 色アニメーション時間", 10f, FontStyles.Normal, TextAlignmentOptions.Left);
            Button createWave = CreateButton(content.transform, "色ウェーブを作成", 0f, 31f);
            createWave.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    return DecorationColorWaveOperations.CreateWave(editor, waveDirection.SelectedIndex == 1,
                        ParseFloat(waveInterval.text, 0f, 100000f), ParseFloat(waveDuration.text, 0f, 100000f),
                        wavePrimary.text, waveSecondary.text, DropdownEnum<FloorDecorationColorType>(waveColorType),
                        ParseFloat(waveColorAnimDuration.text, 0f, 100000f), waveEase.SelectedValue);
                });
            });

            Section(content.transform, "装飾の整列・補間");
            CreateText(content.transform, "複数選択した装飾を整列します。基準値は選択一覧の先頭です。", 11f,
                FontStyles.Normal, TextAlignmentOptions.Left);
            GameObject alignRow = CreateHorizontal(content.transform, "Decoration align", 5f, 40f);
            Button alignX = CreateButton(alignRow.transform, "Xを揃える", 0f, 40f);
            alignX.onClick.AddListener(delegate { Run(delegate { return DecorationLayoutOperations.AlignPosition(editor, true); }); });
            Button alignY = CreateButton(alignRow.transform, "Yを揃える", 0f, 40f);
            alignY.onClick.AddListener(delegate { Run(delegate { return DecorationLayoutOperations.AlignPosition(editor, false); }); });
            GameObject distributeRow = CreateHorizontal(content.transform, "Decoration distribute", 5f, 40f);
            Button distributeX = CreateButton(distributeRow.transform, "X等間隔", 0f, 40f);
            distributeX.onClick.AddListener(delegate { Run(delegate { return DecorationLayoutOperations.DistributePosition(editor, true); }); });
            Button distributeY = CreateButton(distributeRow.transform, "Y等間隔", 0f, 40f);
            distributeY.onClick.AddListener(delegate { Run(delegate { return DecorationLayoutOperations.DistributePosition(editor, false); }); });
            GameObject nudgeRow = CreateHorizontal(content.transform, "Decoration nudge", 5f, 40f);
            decorationNudgeX = CreateInput(nudgeRow.transform, false, 40f);
            decorationNudgeX.text = "0.1";
            decorationNudgeY = CreateInput(nudgeRow.transform, false, 40f);
            decorationNudgeY.text = "0";
            Button nudge = CreateButton(nudgeRow.transform, "微調整", 90f, 40f);
            nudge.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    return DecorationLayoutOperations.Nudge(editor,
                        ParseFloat(decorationNudgeX.text, -100000f, 100000f),
                        ParseFloat(decorationNudgeY.text, -100000f, 100000f));
                });
            });
            CreateText(content.transform, "左からX移動量 / Y移動量", 10f, FontStyles.Normal, TextAlignmentOptions.Left);
            GameObject matchRow = CreateHorizontal(content.transform, "Decoration match", 5f, 40f);
            Button matchRotation = CreateButton(matchRow.transform, "回転を揃える", 0f, 40f);
            matchRotation.onClick.AddListener(delegate { Run(delegate { return DecorationLayoutOperations.MatchRotation(editor); }); });
            Button matchScale = CreateButton(matchRow.transform, "拡大率を揃える", 0f, 40f);
            matchScale.onClick.AddListener(delegate { Run(delegate { return DecorationLayoutOperations.MatchScale(editor); }); });
            decorationInterpolateProperty = CreateDropdown(content.transform, new[]
            {
                new KeyValuePair<string, string>("position", "位置（position）"),
                new KeyValuePair<string, string>("rotation", "回転（rotation）"),
                new KeyValuePair<string, string>("scale", "拡大率（scale）")
            }, 40f);
            Button interpolateDecorations = CreateButton(content.transform, "先頭から末尾まで補間", 0f, 40f);
            interpolateDecorations.onClick.AddListener(delegate
            {
                Run(delegate { return DecorationLayoutOperations.Interpolate(editor, decorationInterpolateProperty.SelectedValue); });
            });

            Section(content.transform, "テキストマスク（実験的）");
            CreateText(content.transform,
                "テキスト装飾を1個、画像装飾を1個以上まとめて選択します。再生時に画像を文字の形で切り抜きます。",
                12f, FontStyles.Normal, TextAlignmentOptions.Left);
            textMaskMode = CreateDropdown(content.transform, new[]
            {
                new KeyValuePair<string, string>("Inside", "文字の内側を表示"),
                new KeyValuePair<string, string>("Outside", "文字の外側を表示")
            }, 40f);
            GameObject textMaskButtons = CreateHorizontal(content.transform, "Text mask buttons", 5f, 40f);
            Button applyTextMask = CreateButton(textMaskButtons.transform, "マスクを設定", 0f, 40f);
            applyTextMask.onClick.AddListener(delegate
            {
                Run(delegate { return TextMaskOperations.Apply(editor, textMaskMode.SelectedValue == "Outside"); });
            });
            Button removeTextMask = CreateButton(textMaskButtons.transform, "マスクを解除", 0f, 40f);
            removeTextMask.onClick.AddListener(delegate
            {
                Run(delegate { return TextMaskOperations.Remove(editor); });
            });

            Section(content.transform, "テキスト装飾フォント");
            CreateText(content.transform,
                "WindowsとUnityから取得したインストール済みフォントをすべて表示します。Windowsの表示名からフォント内部名を自動解決し、選択フォントにない文字だけ標準フォントへフォールバックします。",
                12f, FontStyles.Normal, TextAlignmentOptions.Left);
            systemFontNames = CustomFontOperations.GetInstalledFontNames();
            systemFontSearch = CreateInput(content.transform, false, 40f);
            TMP_Text fontSearchPlaceholder = systemFontSearch.placeholder as TMP_Text;
            if (fontSearchPlaceholder != null) fontSearchPlaceholder.text = "フォント名で検索";
            systemFont = CreateDropdown(content.transform, systemFontNames, 40f);
            systemFontSearch.onValueChanged.AddListener(delegate(string value)
            {
                FilterSystemFonts(value);
            });
            GameObject fontButtons = CreateHorizontal(content.transform, "Font buttons", 5f, 40f);
            Button refreshFonts = CreateButton(fontButtons.transform, "一覧を更新", 0f, 40f);
            refreshFonts.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    RefreshSystemFonts();
                    return systemFontNames.Length + "個のOSフォントを取得しました。";
                });
            });
            Button applyFont = CreateButton(fontButtons.transform, "選択テキストへ適用", 0f, 40f);
            applyFont.onClick.AddListener(delegate
            {
                Run(delegate { return CustomFontOperations.Apply(editor, SelectedSystemFont()); });
            });
            Button removeFont = CreateButton(content.transform, "標準フォントへ戻す", 0f, 40f);
            removeFont.onClick.AddListener(delegate
            {
                Run(delegate { return CustomFontOperations.Remove(editor); });
            });

            Section(content.transform, "チュートリアル背景色");
            CreateText(content.transform,
                "選択範囲の先頭タイルに、チュートリアル背景の色変更を追加します。色はRRGGBBまたはRRGGBBAAです。",
                12f, FontStyles.Normal, TextAlignmentOptions.Left);
            GameObject tutorialTileRow = CreateHorizontal(content.transform, "Tutorial tile color", 5f, 40f);
            tutorialTileToggle = CreateToggle(tutorialTileRow.transform, "背景タイル", true, 135f);
            tutorialTileColor = CreateInput(tutorialTileRow.transform, false, 40f);
            tutorialTileColor.text = "202030";
            GameObject tutorialShapeRow = CreateHorizontal(content.transform, "Tutorial shape color", 5f, 40f);
            tutorialShapeToggle = CreateToggle(tutorialShapeRow.transform, "図形", true, 135f);
            tutorialShapeColor = CreateInput(tutorialShapeRow.transform, false, 40f);
            tutorialShapeColor.text = "FFFFFF";
            GameObject tutorialCameraRow = CreateHorizontal(content.transform, "Tutorial camera color", 5f, 40f);
            tutorialCameraToggle = CreateToggle(tutorialCameraRow.transform, "奥の背景", false, 135f);
            tutorialCameraColor = CreateInput(tutorialCameraRow.transform, false, 40f);
            tutorialCameraColor.text = "000000";
            GameObject tutorialTimingRow = CreateHorizontal(content.transform, "Tutorial background timing", 5f, 40f);
            CreateText(tutorialTimingRow.transform, "変化時間（拍）", 12f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            tutorialDuration = CreateInput(tutorialTimingRow.transform, false, 40f);
            tutorialDuration.text = "1";
            SetWidth(tutorialDuration.gameObject, 85f);
            tutorialEase = CreateDropdown(content.transform, TutorialBackgroundOperations.GetEaseNames(), 40f);
            tutorialEase.SetValue("InOutSine");
            GameObject tutorialButtons = CreateHorizontal(content.transform, "Tutorial background buttons", 5f, 40f);
            Button addTutorialBackground = CreateButton(tutorialButtons.transform, "追加 / 上書き", 0f, 40f);
            addTutorialBackground.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    return TutorialBackgroundOperations.AddOrReplace(editor,
                        tutorialTileToggle.isOn, tutorialTileColor.text,
                        tutorialShapeToggle.isOn, tutorialShapeColor.text,
                        tutorialCameraToggle.isOn, tutorialCameraColor.text,
                        ParseFloat(tutorialDuration.text, 0f, 100000f), tutorialEase.SelectedValue);
                });
            });
            Button removeTutorialBackground = CreateButton(tutorialButtons.transform, "選択範囲から削除", 0f, 40f);
            removeTutorialBackground.onClick.AddListener(delegate
            {
                Run(delegate { return TutorialBackgroundOperations.RemoveAtSelection(editor); });
            });

            BeginCategory(content.transform, "Events");
            Section(content.transform, "イベント一括操作");
            CreateText(content.transform, "イベント種類", 12f, FontStyles.Normal, TextAlignmentOptions.Left);
            eventType = CreateDropdown(content.transform,
                JapaneseLocalization.EventOptions(EditableEventTypeNames()), 40f);
            eventType.SetValue(LevelEventType.SetSpeed.ToString());
            decorationsToggle = CreateToggle(content.transform, "装飾イベントも対象", true, 180f);

            GameObject offsetRow = CreateHorizontal(content.transform, "Offset", 6f, 40f);
            CreateText(offsetRow.transform, "移動タイル数", 12f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            eventOffset = CreateInput(offsetRow.transform, false, 40f);
            eventOffset.text = "1";
            SetWidth(eventOffset.gameObject, 70f);
            shiftOutOfRangeMode = CreateDropdown(content.transform, new[]
            {
                new KeyValuePair<string, string>(ShiftOutOfRangeMode.Abort.ToString(), "譜面外へ出る場合: 中止"),
                new KeyValuePair<string, string>(ShiftOutOfRangeMode.Clamp.ToString(), "譜面外へ出る場合: 端に固定"),
                new KeyValuePair<string, string>(ShiftOutOfRangeMode.Delete.ToString(), "譜面外へ出る場合: 削除")
            }, 40f);
            GameObject eventButtons = CreateHorizontal(content.transform, "Event buttons", 5f, 40f);
            Button shiftBack = CreateButton(eventButtons.transform, "-N", 0f, 40f);
            shiftBack.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    return EventOperations.ShiftInSelection(editor, SelectedEventType(),
                        -Math.Abs(ParseInt(eventOffset.text, 1, 100000)), decorationsToggle.isOn,
                        DropdownEnum<ShiftOutOfRangeMode>(shiftOutOfRangeMode));
                });
            });
            Button shiftForward = CreateButton(eventButtons.transform, "+N", 0f, 40f);
            shiftForward.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    return EventOperations.ShiftInSelection(editor, SelectedEventType(),
                        Math.Abs(ParseInt(eventOffset.text, 1, 100000)), decorationsToggle.isOn,
                        DropdownEnum<ShiftOutOfRangeMode>(shiftOutOfRangeMode));
                });
            });
            Button delete = CreateButton(eventButtons.transform, "削除", 0f, 40f);
            delete.onClick.AddListener(delegate
            {
                Run(delegate { return EventOperations.DeleteInSelection(editor, SelectedEventType(), decorationsToggle.isOn); });
            });

            CreateText(content.transform, "間隔指定（新規追加／コピー貼り付け）", 12f, FontStyles.Normal, TextAlignmentOptions.Left);
            GameObject intervalRow = CreateHorizontal(content.transform, "Event interval", 5f, 40f);
            eventInterval = CreateInput(intervalRow.transform, false, 40f);
            eventInterval.text = "8";
            eventPhase = CreateInput(intervalRow.transform, false, 40f);
            eventPhase.text = "0";
            overwriteSoloToggle = CreateToggle(intervalRow.transform, "同じ床の同種イベントを置換", false, 235f);
            CreateText(content.transform,
                "左から: 間隔 / 開始ずれ。置換をONにすると、TwirlやSetSpeedなど同じ床に1個だけ置ける種類は既存の同種イベントを削除してから追加します。",
                10f, FontStyles.Normal, TextAlignmentOptions.Left);
            eventPasteMode = CreateDropdown(content.transform, new[]
            {
                new KeyValuePair<string, string>(EventPasteMode.Add.ToString(), "コピーイベント: 既存へ追加"),
                new KeyValuePair<string, string>(EventPasteMode.OverwriteSameType.ToString(), "コピーイベント: 同種類だけ上書き"),
                new KeyValuePair<string, string>(EventPasteMode.OverwriteAllFloorEvents.ToString(), "コピーイベント: 床イベントをすべて上書き")
            }, 40f);
            Button pasteClipboardEvents = CreateButton(content.transform, "Ctrl+Shift+Cのイベントを選択範囲へ貼り付け", 0f, 40f);
            pasteClipboardEvents.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    int interval = ParseInt(eventInterval.text, 1, 100000);
                    return EventAdvancedOperations.PasteClipboardEvents(editor, interval,
                        ParseInt(eventPhase.text, 0, interval - 1), decorationsToggle.isOn,
                        DropdownEnum<EventPasteMode>(eventPasteMode));
                });
            });
            Button addIntervalEvent = CreateButton(content.transform, "選択範囲へ一定間隔で新規追加", 0f, 40f);
            addIntervalEvent.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    int interval = ParseInt(eventInterval.text, 1, 100000);
                    return EventAdvancedOperations.AddAtInterval(editor, SelectedEventType(), interval,
                        ParseInt(eventPhase.text, 0, interval - 1), overwriteSoloToggle.isOn);
                });
            });

            eventStateOperation = CreateDropdown(content.transform, new[]
            {
                new KeyValuePair<string, string>(EventStateOperation.Enable.ToString(), "有効化"),
                new KeyValuePair<string, string>(EventStateOperation.Disable.ToString(), "無効化"),
                new KeyValuePair<string, string>(EventStateOperation.Show.ToString(), "表示"),
                new KeyValuePair<string, string>(EventStateOperation.Hide.ToString(), "非表示"),
                new KeyValuePair<string, string>(EventStateOperation.Lock.ToString(), "ロック"),
                new KeyValuePair<string, string>(EventStateOperation.Unlock.ToString(), "ロック解除")
            }, 40f);
            Button applyEventState = CreateButton(content.transform, "選択範囲の状態を一括変更", 0f, 40f);
            applyEventState.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    return EventAdvancedOperations.SetStateInSelection(editor, SelectedEventType(),
                        DropdownEnum<EventStateOperation>(eventStateOperation), decorationsToggle.isOn);
                });
            });

            Button batchInspector = CreateButton(content.transform, "同種類イベントを本家Inspectorで一括編集", 0f, 40f);
            batchInspector.onClick.AddListener(delegate
            {
                Run(delegate { return BatchInspectorOperations.Open(editor, SelectedEventType()); });
            });

            Section(content.transform, "イベントプリセット");
            CreateText(content.transform, "選択範囲内の先頭の指定イベントを保存し、別の範囲へ一定間隔で適用します。", 11f,
                FontStyles.Normal, TextAlignmentOptions.Left);
            presetName = CreateInput(content.transform, false, 40f);
            TMP_Text presetPlaceholder = presetName.placeholder as TMP_Text;
            if (presetPlaceholder != null) presetPlaceholder.text = "プリセット名";
            presetDropdown = CreateDropdown(content.transform, EventPresetStore.Options(), 40f);
            presetInterval = CreateInput(content.transform, false, 40f);
            presetInterval.text = "1";
            GameObject presetButtons = CreateHorizontal(content.transform, "Preset buttons", 5f, 40f);
            Button savePreset = CreateButton(presetButtons.transform, "保存 / 上書き", 0f, 40f);
            savePreset.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    string result = EventPresetStore.SaveFromSelection(editor, SelectedEventType(), presetName.text, decorationsToggle.isOn);
                    RefreshPresetOptions();
                    return result;
                });
            });
            Button applyPreset = CreateButton(presetButtons.transform, "範囲へ適用", 0f, 40f);
            applyPreset.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    return EventPresetStore.ApplyToSelection(editor, presetDropdown.SelectedValue,
                        ParseInt(presetInterval.text, 1, 100000), overwriteSoloToggle.isOn);
                });
            });
            Button deletePreset = CreateButton(content.transform, "選択プリセットを削除", 0f, 40f);
            deletePreset.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    string result = EventPresetStore.Delete(presetDropdown.SelectedValue);
                    RefreshPresetOptions();
                    return result;
                });
            });

            Section(content.transform, "数式による一括編集");
            CreateText(content.transform, "イベント種類を選び、次に変更する数値項目を選択してください。", 13f,
                FontStyles.Normal, TextAlignmentOptions.Left);
            CreateText(content.transform, "イベント種類", 14f, FontStyles.Normal, TextAlignmentOptions.Left);
            formulaEventType = CreateDropdown(content.transform,
                JapaneseLocalization.EventOptions(EditableEventTypeNames()), 40f);
            formulaEventType.SetValue(LevelEventType.SetSpeed.ToString());
            formulaEventType.ValueChanged += delegate { RefreshFormulaProperties(); };
            CreateText(content.transform, "数値項目", 14f, FontStyles.Normal, TextAlignmentOptions.Left);
            formulaProperty = CreateDropdown(content.transform, new[]
            {
                new KeyValuePair<string, string>("No numeric fields", "数値項目がありません")
            }, 40f);
            CreateText(content.transform, "数式", 14f, FontStyles.Normal, TextAlignmentOptions.Left);
            formulaExpression = CreateInput(content.transform, false, 40f);
            formulaExpression.text = "$value * 0.5";
            CreateText(content.transform,
                "$value=元の値 / $i=0始まりの番号 / $t=範囲内の割合(0～1) / $floor=床番号 / $bpm=その地点のBPM",
                11f, FontStyles.Normal, TextAlignmentOptions.Left);
            CreateText(content.transform,
                "例: 半分 → $value*0.5　端へ向かって増加 → $value*(1+$t)　交互 → $value*(1+($i%2)*0.25)",
                11f, FontStyles.Normal, TextAlignmentOptions.Left);
            formulaVariables = CreateInput(content.transform, true, 68f);
            formulaVariables.text = "# 独自変数の例: amp=2";
            Button applyFormula = CreateButton(content.transform, "選択範囲へ数式を適用", 0f, 40f);
            applyFormula.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    return FormulaOperations.ApplyNumericProperty(editor, SelectedFormulaEventType(),
                        formulaProperty.SelectedValue, formulaExpression.text, formulaVariables.text, decorationsToggle.isOn);
                });
            });

            RefreshFormulaProperties();

            Section(content.transform, "イベント値の補間");
            interpolationEventType = CreateDropdown(content.transform,
                JapaneseLocalization.EventOptions(EditableEventTypeNames()), 40f);
            interpolationEventType.SetValue(LevelEventType.MoveTrack.ToString());
            interpolationEventType.ValueChanged += delegate { RefreshInterpolationProperties(); };
            interpolationProperty = CreateDropdown(content.transform, new[]
            {
                new KeyValuePair<string, string>("__none__", "補間可能な項目がありません")
            }, 40f);
            GameObject interpolationValues = CreateHorizontal(content.transform, "Interpolation values", 5f, 40f);
            interpolationStart = CreateInput(interpolationValues.transform, false, 40f);
            interpolationStart.text = "0";
            interpolationEnd = CreateInput(interpolationValues.transform, false, 40f);
            interpolationEnd.text = "1";
            CreateText(content.transform, "数値、x,y、RRGGBB/RRGGBBAAに対応。左が開始値、右が終了値です。", 10f,
                FontStyles.Normal, TextAlignmentOptions.Left);
            Button applyInterpolation = CreateButton(content.transform, "選択範囲のイベントを補間", 0f, 40f);
            applyInterpolation.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    return EventInterpolationOperations.Apply(editor, SelectedInterpolationEventType(),
                        interpolationProperty.SelectedValue, interpolationStart.text, interpolationEnd.text,
                        decorationsToggle.isOn);
                });
            });
            RefreshInterpolationProperties();

            Section(content.transform, "ドロップダウンのお気に入り");
            CreateText(content.transform,
                "本家エディタの任意のドロップダウンで項目を選んでから、ここで★を切り替えます。フィルター以外にも使えます。",
                12f, FontStyles.Normal, TextAlignmentOptions.Left);
            favoriteCurrent = CreateText(content.transform, "", 14f, FontStyles.Normal, TextAlignmentOptions.Left);
            favoriteList = CreateText(content.transform, "", 12f, FontStyles.Normal, TextAlignmentOptions.Left);
            GameObject favoriteButtons = CreateHorizontal(content.transform, "Favorite dropdown buttons", 5f, 38f);
            Button favoriteToggle = CreateButton(favoriteButtons.transform, "★ 追加 / 解除", 0f, 38f);
            favoriteToggle.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    string result = DropdownFavorites.ToggleLast();
                    RefreshFavoriteDisplay();
                    return result;
                });
            });
            Button favoriteClear = CreateButton(favoriteButtons.transform, "一覧を解除", 0f, 38f);
            favoriteClear.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    string result = DropdownFavorites.ClearLastScope();
                    RefreshFavoriteDisplay();
                    return result;
                });
            });
            RefreshFavoriteDisplay();

            BeginCategory(content.transform, "Utility");
            Section(content.transform, "移動・選択");
            GameObject floorRow = CreateHorizontal(content.transform, "Floor jump", 6f, 40f);
            floorNumber = CreateInput(floorRow.transform, false, 40f);
            floorNumber.text = "0";
            Button go = CreateButton(floorRow.transform, "指定床へ移動", 125f, 40f);
            go.onClick.AddListener(delegate
            {
                Run(delegate { return NavigationOperations.GoToFloor(editor, ParseInt(floorNumber.text, 0, int.MaxValue)); });
            });

            GameObject relativeRow = CreateHorizontal(content.transform, "Relative floor jump", 6f, 40f);
            relativeJump = CreateInput(relativeRow.transform, false, 40f);
            relativeJump.text = "100";
            Button goRelative = CreateButton(relativeRow.transform, "現在地から±N", 125f, 40f);
            goRelative.onClick.AddListener(delegate
            {
                Run(delegate { return NavigationOperations.GoRelative(editor, ParseSignedInt(relativeJump.text)); });
            });

            GameObject selectRow = CreateHorizontal(content.transform, "Select count", 6f, 40f);
            selectCount = CreateInput(selectRow.transform, false, 40f);
            selectCount.text = "100";
            Button select = CreateButton(selectRow.transform, "指定数を選択", 125f, 40f);
            select.onClick.AddListener(delegate
            {
                Run(delegate { return NavigationOperations.SelectCount(editor, ParseSignedInt(selectCount.text)); });
            });

            GameObject findRow = CreateHorizontal(content.transform, "Find events", 5f, 40f);
            Button previous = CreateButton(findRow.transform, "前の指定イベント", 0f, 40f);
            previous.onClick.AddListener(delegate
            {
                Run(delegate { return NavigationOperations.FindEvent(editor, SelectedEventType(), false); });
            });
            Button next = CreateButton(findRow.transform, "次の指定イベント", 0f, 40f);
            next.onClick.AddListener(delegate
            {
                Run(delegate { return NavigationOperations.FindEvent(editor, SelectedEventType(), true); });
            });

            Section(content.transform, "イベント検索一覧");
            CreateText(content.transform, "指定した種類の出現位置を100件ずつ一覧表示します。", 11f,
                FontStyles.Normal, TextAlignmentOptions.Left);
            eventSearchType = CreateDropdown(content.transform,
                JapaneseLocalization.EventOptions(EditableEventTypeNames()), 40f);
            eventSearchType.SetValue(LevelEventType.SetSpeed.ToString());
            eventSearchDecorationsToggle = CreateToggle(content.transform, "装飾イベントも含める", true, 205f);
            GameObject eventSearchPageRow = CreateHorizontal(content.transform, "Event search page", 5f, 40f);
            CreateText(eventSearchPageRow.transform, "ページ", 12f, FontStyles.Normal,
                TextAlignmentOptions.MidlineLeft);
            eventSearchPage = CreateInput(eventSearchPageRow.transform, false, 40f);
            eventSearchPage.text = "1";
            SetWidth(eventSearchPage.gameObject, 58f);
            Button previousEventPage = CreateButton(eventSearchPageRow.transform, "前", 54f, 40f);
            previousEventPage.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    eventSearchPage.text = Math.Max(1, ParseInt(eventSearchPage.text, 1, int.MaxValue) - 1).ToString();
                    RefreshEventSearch();
                    return "前のイベント一覧ページを表示しました。";
                });
            });
            Button nextEventPage = CreateButton(eventSearchPageRow.transform, "次", 54f, 40f);
            nextEventPage.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    eventSearchPage.text = (ParseInt(eventSearchPage.text, 1, int.MaxValue) + 1).ToString();
                    RefreshEventSearch();
                    return "次のイベント一覧ページを表示しました。";
                });
            });
            eventSearchDropdown = CreateDropdown(content.transform, new[]
            {
                new KeyValuePair<string, string>("__none__", "該当イベントなし")
            }, 40f);
            eventSearchSummary = CreateText(content.transform, "", 11f, FontStyles.Normal,
                TextAlignmentOptions.Left);
            GameObject eventSearchButtons = CreateHorizontal(content.transform, "Event search buttons", 5f, 40f);
            Button refreshEventSearch = CreateButton(eventSearchButtons.transform, "一覧を更新", 0f, 40f);
            refreshEventSearch.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    RefreshEventSearch();
                    return "イベント一覧を更新しました。";
                });
            });
            Button goToListedEvent = CreateButton(eventSearchButtons.transform, "選択位置へ移動", 0f, 40f);
            goToListedEvent.onClick.AddListener(delegate
            {
                Run(delegate { return EventSearchOperations.GoTo(editor, eventSearchDropdown.SelectedValue); });
            });
            eventSearchType.ValueChanged += RefreshEventSearch;
            eventSearchDecorationsToggle.onValueChanged.AddListener(delegate(bool value) { RefreshEventSearch(); });
            RefreshEventSearch();

            Section(content.transform, "選択範囲プリセット");
            CreateText(content.transform, "現在の譜面ファイルごとに、名前付きの選択範囲を保存します。", 11f,
                FontStyles.Normal, TextAlignmentOptions.Left);
            selectionRangeName = CreateInput(content.transform, false, 40f);
            TMP_Text selectionRangePlaceholder = selectionRangeName.placeholder as TMP_Text;
            if (selectionRangePlaceholder != null) selectionRangePlaceholder.text = "範囲名（例: サビ）";
            selectionRangeDropdown = CreateDropdown(content.transform, SelectionRangeStore.Options(), 40f);
            GameObject selectionRangeButtons = CreateHorizontal(content.transform, "Selection range buttons", 5f, 40f);
            Button saveSelectionRange = CreateButton(selectionRangeButtons.transform, "保存 / 上書き", 0f, 40f);
            saveSelectionRange.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    string result = SelectionRangeStore.SaveSelection(editor, selectionRangeName.text);
                    RefreshSelectionRanges();
                    return result;
                });
            });
            Button goSelectionRange = CreateButton(selectionRangeButtons.transform, "範囲を選択", 0f, 40f);
            goSelectionRange.onClick.AddListener(delegate
            {
                Run(delegate { return SelectionRangeStore.Select(editor, selectionRangeDropdown.SelectedValue); });
            });
            Button deleteSelectionRange = CreateButton(content.transform, "保存範囲を削除", 0f, 40f);
            deleteSelectionRange.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    string result = SelectionRangeStore.Delete(selectionRangeDropdown.SelectedValue);
                    RefreshSelectionRanges();
                    return result;
                });
            });

            Section(content.transform, "Bookmark一覧");
            bookmarkDropdown = CreateDropdown(content.transform, NavigationOperations.BookmarkOptions(editor), 40f);
            GameObject bookmarkButtons = CreateHorizontal(content.transform, "Bookmark buttons", 5f, 40f);
            Button goBookmark = CreateButton(bookmarkButtons.transform, "移動", 0f, 40f);
            goBookmark.onClick.AddListener(delegate
            {
                Run(delegate { return NavigationOperations.GoToBookmark(editor, bookmarkDropdown.SelectedValue); });
            });
            Button addBookmark = CreateButton(bookmarkButtons.transform, "選択先頭へ追加", 0f, 40f);
            addBookmark.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    string result = NavigationOperations.AddBookmark(editor);
                    RefreshBookmarks();
                    return result;
                });
            });
            Button deleteBookmark = CreateButton(bookmarkButtons.transform, "削除", 0f, 40f);
            deleteBookmark.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    string result = NavigationOperations.DeleteBookmark(editor, bookmarkDropdown.SelectedValue);
                    RefreshBookmarks();
                    return result;
                });
            });
            Button refreshBookmarks = CreateButton(content.transform, "Bookmark一覧を更新", 0f, 40f);
            refreshBookmarks.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    RefreshBookmarks();
                    return "Bookmark一覧を更新しました。";
                });
            });

            Section(content.transform, "統計");
            GameObject statsRow = CreateHorizontal(content.transform, "Statistics controls", 5f, 31f);
            statsSelectionToggle = CreateToggle(statsRow.transform, "選択範囲のみ", true, 135f);
            Button stats = CreateButton(statsRow.transform, "集計", 0f, 31f);
            stats.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    statisticsText.text = EventOperations.Statistics(editor, statsSelectionToggle.isOn);
                    return "統計を更新しました。";
                });
            });
            statisticsText = CreateText(content.transform, "", 12f, FontStyles.Normal, TextAlignmentOptions.TopLeft);

            Section(content.transform, "操作履歴");
            CreateText(content.transform, "直近30件を保存し、ここには新しい順で12件表示します。", 11f,
                FontStyles.Normal, TextAlignmentOptions.Left);
            GameObject historyButtons = CreateHorizontal(content.transform, "Operation history buttons", 5f, 40f);
            Button refreshHistory = CreateButton(historyButtons.transform, "履歴を更新", 0f, 40f);
            refreshHistory.onClick.AddListener(delegate
            {
                activeCommand = null;
                RefreshOperationHistory();
                SetStatus("操作履歴を更新しました。");
            });
            Button clearHistory = CreateButton(historyButtons.transform, "履歴を消去", 0f, 40f);
            clearHistory.onClick.AddListener(delegate
            {
                activeCommand = null;
                EditorQoLPreferences.ClearOperationHistory();
                RefreshOperationHistory();
                SetStatus("操作履歴を消去しました。");
            });
            operationHistoryText = CreateText(content.transform, "", 11f, FontStyles.Normal,
                TextAlignmentOptions.TopLeft);
            RefreshOperationHistory();

            Section(content.transform, "診断・安全修復");
            CreateText(content.transform,
                "範囲外floor、solo重複、禁止競合、先頭床・最終床の配置、タイル参照を確認します。",
                11f, FontStyles.Normal, TextAlignmentOptions.Left);
            GameObject diagnosticButtons = CreateHorizontal(content.transform, "Diagnostic buttons", 5f, 40f);
            Button analyze = CreateButton(diagnosticButtons.transform, "診断", 0f, 40f);
            analyze.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    diagnosticsText.text = DiagnosticsOperations.Analyze(editor);
                    return "診断を実行しました。";
                });
            });
            Button repair = CreateButton(diagnosticButtons.transform, "安全修復", 0f, 40f);
            repair.onClick.AddListener(delegate
            {
                Run(delegate
                {
                    string result = DiagnosticsOperations.SafeRepair(editor);
                    diagnosticsText.text = DiagnosticsOperations.Analyze(editor);
                    return result;
                });
            });
            diagnosticsText = CreateText(content.transform, "", 12f, FontStyles.Normal, TextAlignmentOptions.TopLeft);

            FinishCategoryLayout(content.transform);

            status = CreateText(content.transform, "", 12f, FontStyles.Normal, TextAlignmentOptions.Left);
            Button close = CreateButton(content.transform, "左パネルを閉じる", 0f, 31f);
            close.onClick.AddListener(delegate
            {
                HideQoL(false);
                inspector.ShowInspector(false, true);
            });
            RefreshQuickAccess();
        }

        private void ToggleQoL()
        {
            if (IsOpen)
            {
                HideQoL(false);
                inspector.ShowInspector(false, true);
                return;
            }
            ShowQoL();
        }

        private void BeginCategory(Transform parent, string category)
        {
            CaptureBuiltCategoryItems(parent);
            buildingCategory = category;
            buildingSection = null;
            buildingSectionParent = null;
            buildingSectionContent = null;
            buildingSectionGroup = null;
            categoryStartChildIndex = parent == null ? 0 : parent.childCount;
        }

        private void FinishCategoryLayout(Transform parent)
        {
            CaptureBuiltCategoryItems(parent);
            buildingCategory = null;
            buildingSection = null;
            buildingSectionParent = null;
            buildingSectionContent = null;
            buildingSectionGroup = null;
            ApplyCategoryFilter();
        }

        private void CaptureBuiltCategoryItems(Transform parent)
        {
            if (parent == null || string.IsNullOrEmpty(buildingCategory)) return;
            List<GameObject> items;
            if (!categoryItems.TryGetValue(buildingCategory, out items))
            {
                items = new List<GameObject>();
                categoryItems[buildingCategory] = items;
            }

            for (int i = categoryStartChildIndex; i < parent.childCount; i++)
            {
                GameObject item = parent.GetChild(i).gameObject;
                if (item != null && !items.Contains(item)) items.Add(item);
            }
        }

        private void ApplyCategoryFilter()
        {
            string selected = toolCategory == null ? "Tiles" : toolCategory.SelectedValue;
            foreach (KeyValuePair<string, List<GameObject>> pair in categoryItems)
            {
                bool visible = string.Equals(pair.Key, selected, StringComparison.Ordinal);
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    GameObject item = pair.Value[i];
                    if (item != null) item.SetActive(visible);
                }
            }

            foreach (SectionGroup group in sectionGroups.Values)
            {
                if (group.Content != null)
                    group.Content.SetActive(string.Equals(group.Category, selected, StringComparison.Ordinal) &&
                                            !group.Collapsed);
            }

            if (contentScroll != null) contentScroll.verticalNormalizedPosition = 1f;
        }

        private void SetSectionCollapsed(SectionGroup group, bool collapsed, bool save)
        {
            if (group == null) return;
            group.Collapsed = collapsed;
            EditorQoLPreferences.SetSectionCollapsed(group.Id, collapsed, save);
            UpdateSectionVisual(group);
            Canvas.ForceUpdateCanvases();
        }

        private void SetAllSectionsCollapsed(bool collapsed)
        {
            foreach (SectionGroup group in sectionGroups.Values)
            {
                group.Collapsed = collapsed;
                EditorQoLPreferences.SetSectionCollapsed(group.Id, collapsed, false);
                UpdateSectionVisual(group);
            }
            EditorQoLPreferences.Save();
            Canvas.ForceUpdateCanvases();
            if (contentScroll != null) contentScroll.verticalNormalizedPosition = 1f;
            SetStatus(collapsed ? "全セクションを閉じました。" : "全セクションを開きました。");
        }

        private void UpdateSectionVisual(SectionGroup group)
        {
            if (group == null) return;
            if (group.HeaderText != null)
                group.HeaderText.text = (group.Collapsed ? "▶ " : "▼ ") + group.Title;
            string selected = toolCategory == null ? "Tiles" : toolCategory.SelectedValue;
            if (group.Content != null)
                group.Content.SetActive(!group.Collapsed &&
                                        string.Equals(group.Category, selected, StringComparison.Ordinal));
        }

        private void ShowQoL()
        {
            if (contentRoot == null || inspector == null) return;

            foreach (PropertiesPanel panel in inspector.panelsList)
            {
                panel.gameObject.SetActive(false);
                if (panel.tabContainer != null) panel.tabContainer.gameObject.SetActive(false);
            }

            foreach (InspectorTab tab in inspector.tabs.GetComponentsInChildren<InspectorTab>(true))
            {
                if (tab != tabVisual) tab.SetSelected(false);
            }

            inspector.selectedEvent = null;
            inspector.selectedEventType = LevelEventType.None;
            inspector.titleCanvas.SetActive(true);
            inspector.title.text = "エディタ便利機能 v" + ModVersion.Current;
            if (inspector.messageCanvas != null) inspector.messageCanvas.SetActive(false);

            showingQoL = true;
            contentRoot.SetActive(true);
            contentRoot.transform.SetAsLastSibling();
            SetTabSelected(true);
            inspector.ShowInspector(true, true);
            RefreshSelection();
            RefreshFavoriteDisplay();
            RefreshQuickAccess();
            RefreshPresetOptions();
            RefreshBookmarks();
            RefreshSelectionRanges();
            RefreshEventSearch();
            RefreshOperationHistory();
            RefreshInterpolationProperties();
        }

        private void HideQoL(bool hideInspector)
        {
            CancelPendingOperation(false);
            showingQoL = false;
            if (contentRoot != null) contentRoot.SetActive(false);
            SetTabSelected(false);
            if (hideInspector && inspector != null && inspector.showInspector)
                inspector.ShowInspector(false, true);
        }

        private void SetTabSelected(bool selected)
        {
            if (tabVisual != null)
            {
                tabVisual.SetSelected(selected);
                if (tabVisual.cycleButtons != null) tabVisual.cycleButtons.gameObject.SetActive(false);
                return;
            }

            if (tabButton != null)
            {
                ColorBlock colors = tabButton.colors;
                colors.normalColor = Color.white.WithAlpha(selected ? 0.7f : 0.45f);
                tabButton.colors = colors;
            }
        }

        private void Update()
        {
            bool control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (Main.Enabled && control && Input.GetKeyDown(KeyCode.K))
                OpenCommandPalette();

            if (commandSearch != null && commandSearch.isFocused && Input.GetKeyDown(KeyCode.Escape))
            {
                commandSearch.text = string.Empty;
                commandSearch.DeactivateInputField();
            }

            if (reopenAfterUndoFrames > 0)
            {
                reopenAfterUndoFrames--;
                if (reopenAfterUndoFrames == 0) ShowQoL();
            }

            if (!IsOpen) return;

            if (pendingCommandScrollFrames > 0)
            {
                pendingCommandScrollFrames--;
                if (pendingCommandScrollFrames == 0) ScrollToCommandTarget();
            }

            RefreshSelection();
            RefreshFavoriteDisplay();

            // The editor consumes wheel input aggressively. Read it here while the mouse is
            // over the left inspector so scrolling works even above labels and input fields.
            if (contentScroll != null && ShouldCaptureMouse() && !IsAnyDropdownOpen())
            {
                float wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.001f)
                    contentScroll.verticalNormalizedPosition = Mathf.Clamp01(
                        contentScroll.verticalNormalizedPosition + wheel * 0.075f);
            }
        }

        private bool IsAnyDropdownOpen()
        {
            for (int i = 0; i < dropdowns.Count; i++)
                if (dropdowns[i] != null && dropdowns[i].IsOpen) return true;
            return false;
        }

        private void OnDestroy()
        {
            if (tabObject != null) Destroy(tabObject);
            if (contentRoot != null) Destroy(contentRoot);
            if (instance == this) instance = null;
        }

        private bool ShouldCaptureKeyboard()
        {
            if (!IsOpen) return false;
            for (int i = 0; i < inputFields.Count; i++)
            {
                TMP_InputField input = inputFields[i];
                if (input != null && input.isFocused) return true;
            }
            return false;
        }

        private bool ShouldCaptureMouse()
        {
            if (!IsOpen || inspectorRect == null) return false;
            Camera camera = null;
            if (editor.levelEditorCanvas != null && editor.levelEditorCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                camera = editor.levelEditorCanvas.worldCamera;
            return RectTransformUtility.RectangleContainsScreenPoint(inspectorRect, Input.mousePosition, camera);
        }

        private void RefreshSelection()
        {
            if (selection == null || editor == null) return;
            if (editor.selectedFloors == null || editor.selectedFloors.Count == 0)
            {
                selection.text = "選択なし / 全 " + (editor.floors == null ? 0 : editor.floors.Count) + " タイル";
                return;
            }

            try
            {
                FloorRange range = EditorSelection.GetRange(editor, true);
                selection.text = range.Start + "～" + range.End + "（" + range.Count + "タイル）/ 全" + editor.floors.Count + "タイル";
            }
            catch
            {
                selection.text = "選択範囲を取得できません";
            }
        }

        private void Run(Func<string> action)
        {
            CommandEntry command = activeCommand;
            activeCommand = null;
            if (pendingOperation != null) CancelPendingOperation(false);
            if (action == null) return;
            if (operationPreviewToggle != null && operationPreviewToggle.isOn && RequiresPreview(command))
            {
                pendingOperation = action;
                pendingOperationCommand = command;
                pendingOperationScope = CurrentOperationScope();
                if (contentScroll != null)
                {
                    pendingOperationScrollPosition = contentScroll.verticalNormalizedPosition;
                    hasPendingOperationScrollPosition = true;
                }
                if (operationPreviewText != null)
                    operationPreviewText.text = "実行前プレビュー\n" + command.DisplayName + "\n" +
                                                pendingOperationScope +
                                                "\n\n［確定して実行］を押すまで譜面は変更されません。";
                if (operationPreviewBox != null) operationPreviewBox.SetActive(true);
                if (contentScroll != null) contentScroll.verticalNormalizedPosition = 1f;
                SetStatus("操作内容を確認してください。");
                return;
            }
            ExecuteOperation(command, action);
        }

        private void ConfirmPendingOperation()
        {
            if (pendingOperation == null) return;
            string currentScope = CurrentOperationScope();
            if (!string.Equals(currentScope, pendingOperationScope, StringComparison.Ordinal))
            {
                pendingOperationScope = currentScope;
                if (operationPreviewText != null)
                    operationPreviewText.text = "対象が変わったため再確認してください。\n" +
                                                pendingOperationCommand.DisplayName + "\n" + currentScope +
                                                "\n\nもう一度［確定して実行］を押してください。";
                SetStatus("対象範囲が変わりました。もう一度確認してください。");
                return;
            }
            Func<string> action = pendingOperation;
            CommandEntry command = pendingOperationCommand;
            pendingOperation = null;
            pendingOperationCommand = null;
            pendingOperationScope = null;
            if (operationPreviewBox != null) operationPreviewBox.SetActive(false);
            ExecuteOperation(command, action);
            RestorePendingOperationScroll();
        }

        private void CancelPendingOperation(bool showStatus)
        {
            pendingOperation = null;
            pendingOperationCommand = null;
            pendingOperationScope = null;
            if (operationPreviewBox != null) operationPreviewBox.SetActive(false);
            RestorePendingOperationScroll();
            if (showStatus) SetStatus("操作をキャンセルしました。");
        }

        private void RestorePendingOperationScroll()
        {
            if (hasPendingOperationScrollPosition && contentScroll != null)
                contentScroll.verticalNormalizedPosition = pendingOperationScrollPosition;
            hasPendingOperationScrollPosition = false;
        }

        private void ExecuteOperation(CommandEntry command, Func<string> action)
        {
            string commandName = command == null ? "操作" : command.DisplayName;
            string scope = CurrentOperationScope(RequiresPreview(command));
            try
            {
                string result = action();
                SetStatus(result);
                if (command != null) EditorQoLPreferences.RecordRecent(command.Id);
                EditorQoLPreferences.AddOperationHistory(commandName, scope, result, true);
                RefreshSelection();
            }
            catch (Exception ex)
            {
                string result = "エラー: " + ex.Message;
                SetStatus(result);
                EditorQoLPreferences.AddOperationHistory(commandName, scope, result, false);
            }
            RefreshQuickAccess();
            RefreshOperationHistory();
        }

        private bool RequiresPreview(CommandEntry command)
        {
            if (command == null) return false;
            if (command.Category == "Tiles") return true;
            if (command.Category == "Visuals") return command.Label != "一覧を更新";
            if (command.Category == "Events")
            {
                if (command.Section == "イベント一括操作")
                    return command.Label != "同種類イベントを本家Inspectorで一括編集";
                if (command.Section == "イベントプリセット") return command.Label == "範囲へ適用";
                return command.Section == "数式による一括編集" || command.Section == "イベント値の補間";
            }
            return command.Category == "Utility" && command.Label == "安全修復";
        }

        private string CurrentOperationScope(bool includeEventCounts = true)
        {
            if (editor == null) return "対象: 不明";
            try
            {
                FloorRange range = EditorSelection.GetRange(editor, true);
                if (!includeEventCounts)
                    return "対象: " + range.Start + "～" + range.End + "（" + range.Count + "タイル）";
                int events = editor.events.Count(x => x.floor >= range.Start && x.floor <= range.End);
                int decorations = editor.decorations.Count(x => x.floor >= range.Start && x.floor <= range.End);
                return "対象: " + range.Start + "～" + range.End + "（" + range.Count + "タイル）" +
                       " / 範囲内イベント " + events + "件 / 装飾 " + decorations + "件";
            }
            catch
            {
                int decorations = editor.selectedDecorations == null ? 0 : editor.selectedDecorations.Count;
                return decorations > 0 ? "対象: 選択中の装飾 " + decorations + "件" : "対象: 選択なし";
            }
        }

        private void OpenCommandPalette()
        {
            if (commandSearch == null) return;
            if (!IsOpen) ShowQoL();
            commandSearch.Select();
            commandSearch.ActivateInputField();
            commandSearch.MoveTextEnd(false);
            RefreshCommandSearchResults(commandSearch.text);
        }

        private void RefreshCommandSearchResults(string query)
        {
            if (commandSearchResults == null) return;
            List<CommandEntry> matches = FindCommands(query).Take(6).ToList();
            ClearChildren(commandSearchResults.transform);
            if (string.IsNullOrWhiteSpace(query))
            {
                commandSearchResults.SetActive(false);
                return;
            }

            commandSearchResults.SetActive(true);
            if (matches.Count == 0)
            {
                CreateText(commandSearchResults.transform, "一致する機能がありません。", 11f,
                    FontStyles.Normal, TextAlignmentOptions.Left);
                return;
            }

            CreateText(commandSearchResults.transform, "候補をクリック / Enterで先頭を開く", 10f,
                FontStyles.Normal, TextAlignmentOptions.Left);
            foreach (CommandEntry command in matches) CreateCommandShortcut(commandSearchResults.transform, command);
        }

        private IEnumerable<CommandEntry> FindCommands(string query)
        {
            string text = (query ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text)) return Enumerable.Empty<CommandEntry>();

            return commandEntries
                .Where(x => x != null && x.Button != null &&
                    (ContainsIgnoreCase(x.Label, text) || ContainsIgnoreCase(x.Section, text) ||
                     ContainsIgnoreCase(CategoryLabel(x.Category), text)))
                .OrderBy(x => StartsWithIgnoreCase(x.Label, text) ? 0 :
                    (StartsWithIgnoreCase(x.Section, text) ? 1 : 2))
                .ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase);
        }

        private void OpenFirstMatchingCommand(string query)
        {
            CommandEntry command = FindCommands(query).FirstOrDefault();
            if (command == null)
            {
                SetStatus("検索条件に一致する機能がありません。");
                return;
            }

            NavigateToCommand(command);
        }

        private void NavigateToCommand(CommandEntry command)
        {
            if (command == null || command.Button == null) return;
            if (toolCategory != null) toolCategory.SetValue(command.Category);
            if (command.Group != null && command.Group.Collapsed)
                SetSectionCollapsed(command.Group, false, true);
            ApplyCategoryFilter();
            pendingCommandTarget = command.Button.GetComponent<RectTransform>();
            pendingCommandScrollFrames = 2;
            if (commandSearch != null)
            {
                commandSearch.text = string.Empty;
                commandSearch.DeactivateInputField();
            }
            command.Button.Select();
            SetStatus(command.DisplayName + "を開きました。");
        }

        private void CreateCommandShortcut(Transform parent, CommandEntry command)
        {
            if (parent == null || command == null) return;
            GameObject row = CreateHorizontal(parent, "Command shortcut", 4f, 38f);
            Button open = CreatePlainButton(row.transform, command.DisplayName, 0f, 38f);
            open.onClick.AddListener(delegate { NavigateToCommand(command); });
            string favoriteLabel = EditorQoLPreferences.IsFavorite(command.Id) ? "★" : "☆";
            Button favorite = CreatePlainButton(row.transform, favoriteLabel, 46f, 38f);
            favorite.onClick.AddListener(delegate { ToggleCommandFavorite(command); });
        }

        private void ToggleCommandFavorite(CommandEntry command)
        {
            bool added = EditorQoLPreferences.ToggleFavorite(command.Id);
            SetStatus("「" + command.DisplayName + "」をお気に入り" +
                      (added ? "に追加" : "から削除") + "しました。");
            RefreshQuickAccess();
            RefreshCommandSearchResults(commandSearch == null ? string.Empty : commandSearch.text);
        }

        private void RecordCommandUse(CommandEntry command)
        {
            if (command == null) return;
            activeCommand = command;
        }

        private void RefreshQuickAccess()
        {
            RebuildQuickAccess(quickFavorites, EditorQoLPreferences.FavoriteCommandIds(),
                "お気に入りはありません。", 5);
            RebuildQuickAccess(quickRecents, EditorQoLPreferences.RecentCommandIds(),
                "まだ使用履歴がありません。", 3);
        }

        private void RebuildQuickAccess(GameObject container, IList<string> commandIds, string emptyText,
            int maximum)
        {
            if (container == null) return;
            ClearChildren(container.transform);
            List<CommandEntry> commands = commandIds
                .Select(id => commandEntries.FirstOrDefault(x => x.Id == id))
                .Where(x => x != null)
                .Take(maximum)
                .ToList();
            if (commands.Count == 0)
            {
                CreateText(container.transform, emptyText, 10f, FontStyles.Normal, TextAlignmentOptions.Left);
                return;
            }
            foreach (CommandEntry command in commands) CreateCommandShortcut(container.transform, command);
        }

        private static void ClearChildren(Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
        }

        private void ScrollToCommandTarget()
        {
            if (contentScroll == null || contentScroll.content == null ||
                contentScroll.viewport == null || pendingCommandTarget == null) return;

            Canvas.ForceUpdateCanvases();
            RectTransform content = contentScroll.content;
            float scrollableHeight = Mathf.Max(0f, content.rect.height - contentScroll.viewport.rect.height);
            if (scrollableHeight <= 0.01f)
            {
                contentScroll.verticalNormalizedPosition = 1f;
                pendingCommandTarget = null;
                return;
            }

            Vector3 targetCenter = pendingCommandTarget.TransformPoint(pendingCommandTarget.rect.center);
            float distanceFromTop = Mathf.Max(0f, -content.InverseTransformPoint(targetCenter).y);
            float desiredOffset = Mathf.Clamp(distanceFromTop - contentScroll.viewport.rect.height * 0.3f,
                0f, scrollableHeight);
            contentScroll.verticalNormalizedPosition = 1f - desiredOffset / scrollableHeight;
            pendingCommandTarget = null;
        }

        private static bool ContainsIgnoreCase(string source, string query)
        {
            return !string.IsNullOrEmpty(source) &&
                   source.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private static bool StartsWithIgnoreCase(string source, string query)
        {
            return !string.IsNullOrEmpty(source) &&
                   source.StartsWith(query, StringComparison.CurrentCultureIgnoreCase);
        }

        private static string CategoryLabel(string category)
        {
            switch (category)
            {
                case "Tiles": return "タイル・トラック";
                case "Visuals": return "装飾・見た目";
                case "Events": return "イベント編集";
                case "Utility": return "移動・情報";
                default: return category ?? string.Empty;
            }
        }

        private static IEnumerable<string> EditableEventTypeNames()
        {
            return Enum.GetValues(typeof(LevelEventType)).Cast<LevelEventType>()
                .Where(x => x != LevelEventType.None && !x.IsSetting())
                .Select(x => x.ToString());
        }

        private LevelEventType SelectedEventType()
        {
            return EventOperations.ParseEventType(eventType == null ? null : eventType.SelectedValue);
        }

        private LevelEventType SelectedFormulaEventType()
        {
            return EventOperations.ParseEventType(formulaEventType == null ? null : formulaEventType.SelectedValue);
        }

        private LevelEventType SelectedInterpolationEventType()
        {
            return EventOperations.ParseEventType(interpolationEventType == null ? null : interpolationEventType.SelectedValue);
        }

        private void RefreshInterpolationProperties()
        {
            if (interpolationProperty == null || interpolationEventType == null) return;
            try
            {
                List<string> properties = EventInterpolationOperations.GetPropertyNames(SelectedInterpolationEventType());
                if (properties.Count == 0)
                    interpolationProperty.SetOptions(new[]
                    {
                        new KeyValuePair<string, string>("__none__", "補間可能な項目がありません")
                    });
                else
                    interpolationProperty.SetOptions(JapaneseLocalization.PropertyOptions(properties));
            }
            catch (Exception ex)
            {
                interpolationProperty.SetOptions(new[]
                {
                    new KeyValuePair<string, string>("__none__", "取得できません")
                });
                SetStatus("エラー: " + ex.Message);
            }
        }

        private void RefreshPresetOptions()
        {
            if (presetDropdown != null) presetDropdown.SetOptions(EventPresetStore.Options());
        }

        private void RefreshBookmarks()
        {
            if (bookmarkDropdown != null && editor != null)
                bookmarkDropdown.SetOptions(NavigationOperations.BookmarkOptions(editor));
        }

        private void RefreshSelectionRanges()
        {
            if (selectionRangeDropdown != null) selectionRangeDropdown.SetOptions(SelectionRangeStore.Options());
        }

        private void RefreshEventSearch()
        {
            if (editor == null || eventSearchType == null || eventSearchDropdown == null ||
                eventSearchPage == null || eventSearchDecorationsToggle == null) return;
            try
            {
                int page;
                if (!int.TryParse(eventSearchPage.text, out page) || page < 1) page = 1;
                EventOccurrencePage result = EventSearchOperations.GetPage(editor,
                    EventOperations.ParseEventType(eventSearchType.SelectedValue),
                    eventSearchDecorationsToggle.isOn, page);
                eventSearchPage.text = result.Page.ToString();
                eventSearchDropdown.SetOptions(result.Options);
                if (eventSearchSummary != null)
                    eventSearchSummary.text = "全" + result.Total + "件 / " + result.Page + " / " +
                                              result.PageCount + "ページ";
            }
            catch (Exception ex)
            {
                if (eventSearchSummary != null) eventSearchSummary.text = "取得エラー: " + ex.Message;
            }
        }

        private void RefreshOperationHistory()
        {
            if (operationHistoryText != null)
                operationHistoryText.text = EditorQoLPreferences.OperationHistoryText(12);
        }

        private void RefreshFormulaProperties()
        {
            if (formulaProperty == null || formulaEventType == null) return;
            try
            {
                List<string> properties = FormulaOperations.GetNumericPropertyNames(SelectedFormulaEventType());
                if (properties.Count == 0)
                    formulaProperty.SetOptions(new[] { new KeyValuePair<string, string>("No numeric fields", "数値項目がありません") });
                else
                    formulaProperty.SetOptions(JapaneseLocalization.PropertyOptions(properties));
            }
            catch (Exception ex)
            {
                formulaProperty.SetOptions(new[] { new KeyValuePair<string, string>("Unavailable", "取得できません") });
                SetStatus("エラー: " + ex.Message);
            }
        }

        private void RefreshSystemFonts()
        {
            systemFontNames = CustomFontOperations.GetInstalledFontNames();
            FilterSystemFonts(systemFontSearch == null ? string.Empty : systemFontSearch.text);
        }

        private void FilterSystemFonts(string query)
        {
            if (systemFont == null) return;
            string text = (query ?? string.Empty).Trim();
            string[] filtered = string.IsNullOrEmpty(text)
                ? systemFontNames
                : systemFontNames.Where(x => x.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0)
                    .ToArray();

            if (filtered.Length == 0)
            {
                systemFont.SetOptions(new[]
                {
                    new KeyValuePair<string, string>("__no_font_match__", "一致するフォントがありません")
                });
                return;
            }

            systemFont.SetOptions(filtered);
        }

        private string SelectedSystemFont()
        {
            if (systemFont == null) throw new InvalidOperationException("フォント一覧を取得できませんでした。");
            string selected = systemFont.SelectedValue;
            if (selected == "__no_font_match__")
                throw new InvalidOperationException("検索条件に一致するフォントがありません。");
            return selected;
        }

        private void RefreshFavoriteDisplay()
        {
            if (favoriteCurrent != null) favoriteCurrent.text = DropdownFavorites.CurrentSelectionText();
            if (favoriteList != null) favoriteList.text = DropdownFavorites.CurrentFavoritesText();
        }

        private void SetStatus(string text)
        {
            if (status != null) status.text = text;
        }

        private static float ParseFloat(string text, float min, float max)
        {
            float value;
            if (!float.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value) || value < min || value > max)
                throw new FormatException(min + " 以上 " + max + " 以下の数値を入力してください。");
            return value;
        }

        private static double ParseDouble(string text)
        {
            return ParseDouble(text, -1000000d, 1000000d);
        }

        private static double ParseDouble(string text, double min, double max)
        {
            double value;
            if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value) || value < min || value > max)
                throw new FormatException(min + " 以上 " + max + " 以下の数値を入力してください。");
            return value;
        }

        private static T DropdownEnum<T>(NativeDropdown dropdown) where T : struct
        {
            string text = dropdown.SelectedValue;
            T result;
            if (!Enum.TryParse(text, out result)) throw new FormatException("不明な選択肢です: " + text);
            return result;
        }

        private static int ParseInt(string text, int min, int max)
        {
            int value;
            if (!int.TryParse(text, out value) || value < min || value > max)
                throw new FormatException(min + " 以上 " + max + " 以下の整数を入力してください。");
            return value;
        }

        private static int ParseSignedInt(string text)
        {
            int value;
            if (!int.TryParse(text, out value) || value == 0)
                throw new FormatException("0以外の整数を入力してください。");
            return value;
        }

        private GameObject CreateObject(string name, Transform parent)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(ResolveBuildParent(parent), false);
            return obj;
        }

        private Transform ResolveBuildParent(Transform parent)
        {
            return buildingSectionContent != null && buildingSectionParent != null &&
                   parent == buildingSectionParent
                ? buildingSectionContent
                : parent;
        }

        private GameObject CreateVertical(Transform parent, string name, float spacing, int left, int right, int top, int bottom)
        {
            GameObject obj = CreateObject(name, parent);
            VerticalLayoutGroup group = obj.AddComponent<VerticalLayoutGroup>();
            group.spacing = spacing;
            group.padding = new RectOffset(left, right, top, bottom);
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = false;
            group.childForceExpandWidth = true;
            return obj;
        }

        private GameObject CreateHorizontal(Transform parent, string name, float spacing, float height)
        {
            GameObject obj = CreateObject(name, parent);
            HorizontalLayoutGroup group = obj.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.childAlignment = TextAnchor.MiddleLeft;
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = false;
            group.childForceExpandWidth = true;
            LayoutElement layout = obj.AddComponent<LayoutElement>();
            layout.preferredHeight = Mathf.Max(height, 38f);
            return obj;
        }

        private TMP_Text CreateText(Transform parent, string value, float size, FontStyles style, TextAlignmentOptions alignment)
        {
            GameObject obj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            obj.transform.SetParent(ResolveBuildParent(parent), false);
            TMP_Text text = obj.GetComponent<TMP_Text>();
            text.text = value;
            text.fontSize = Mathf.Max(size, 16f);
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = Color.white;
            text.enableWordWrapping = true;
            TMP_Text template = editor.notificationOkButton.GetComponentInChildren<TMP_Text>(true);
            if (template != null) text.font = template.font;
            LayoutElement layout = obj.AddComponent<LayoutElement>();
            layout.minHeight = Math.Max(24f, text.fontSize + 7f);
            return text;
        }

        private void Section(Transform parent, string title)
        {
            buildingSectionParent = null;
            buildingSectionContent = null;
            buildingSectionGroup = null;
            buildingSection = title;
            string id = (buildingCategory ?? string.Empty) + "|" + title;
            Button header = CreatePlainButton(parent, title, 0f, 38f);
            TMP_Text headerText = header.GetComponentInChildren<TMP_Text>(true);
            GameObject body = CreateVertical(parent, title + " Content", 7f, 0, 0, 0, 5);
            SectionGroup group = new SectionGroup
            {
                Id = id,
                Category = buildingCategory,
                Title = title,
                Header = header,
                HeaderText = headerText,
                Content = body,
                Collapsed = EditorQoLPreferences.GetSectionCollapsed(id, DefaultSectionCollapsed(title))
            };
            sectionGroups[id] = group;
            header.onClick.AddListener(delegate { SetSectionCollapsed(group, !group.Collapsed, true); });
            buildingSectionParent = parent;
            buildingSectionContent = body.transform;
            buildingSectionGroup = group;
            UpdateSectionVisual(group);
        }

        private static bool DefaultSectionCollapsed(string title)
        {
            return title == "イベント検索一覧" || title == "選択範囲プリセット" || title == "操作履歴";
        }

        private Button CreateButton(Transform parent, string label, float width, float height)
        {
            Button button = CreatePlainButton(parent, label, width, height);
            if (!string.IsNullOrEmpty(buildingCategory))
            {
                CommandEntry entry = new CommandEntry
                {
                    Label = label,
                    Category = buildingCategory,
                    Section = buildingSection,
                    Button = button,
                    Group = buildingSectionGroup
                };
                commandEntries.Add(entry);
                button.onClick.AddListener(delegate { RecordCommandUse(entry); });
            }
            return button;
        }

        private Button CreatePlainButton(Transform parent, string label, float width, float height)
        {
            GameObject obj = Instantiate(editor.notificationOkButton.gameObject, ResolveBuildParent(parent), false);
            obj.name = label + " Button";
            obj.SetActive(true);
            Button button = obj.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = label;
                text.fontSize = 16f;
                text.enableAutoSizing = true;
                text.fontSizeMin = 12f;
                text.fontSizeMax = 16f;
            }
            LayoutElement layout = obj.GetComponent<LayoutElement>();
            if (layout == null) layout = obj.AddComponent<LayoutElement>();
            layout.preferredHeight = Mathf.Max(height, 40f);
            layout.minHeight = Mathf.Max(height, 40f);
            layout.flexibleWidth = width <= 0f ? 1f : 0f;
            if (width > 0f) layout.preferredWidth = width;

            return button;
        }

        private TMP_InputField CreateInput(Transform parent, bool multiline, float height)
        {
            // Use the level editor's own input-field prefab instead of constructing a look-alike.
            // This preserves the standard background, caret, selection colors, padding, font,
            // focus handling, and keyboard behavior used elsewhere in the editor.
            TMP_InputField template = Resources.Load<TMP_InputField>("LevelEditor/SettingsControls/TextControl");
            TMP_InputField input;
            parent = ResolveBuildParent(parent);

            if (template != null)
            {
                input = Instantiate(template, parent, false);
                input.gameObject.name = multiline ? "Editor QoL Multiline Input" : "Editor QoL Input";
                input.gameObject.SetActive(true);
            }
            else
            {
                // Defensive fallback for game versions where the resource path changes.
                GameObject obj = new GameObject("Editor QoL Input", typeof(RectTransform), typeof(CanvasRenderer),
                    typeof(Image), typeof(TMP_InputField));
                obj.transform.SetParent(parent, false);
                Image image = obj.GetComponent<Image>();
                image.color = new Color(0.13f, 0.13f, 0.16f, 1f);

                GameObject viewport = CreateObject("Text Area", obj.transform);
                RectTransform viewportRect = viewport.GetComponent<RectTransform>();
                Stretch(viewportRect, 6f, 6f, 4f, 4f);
                viewport.AddComponent<RectMask2D>();

                TMP_Text text = CreateText(viewport.transform, "", 12f, FontStyles.Normal,
                    multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft);
                Stretch(text.rectTransform, 0f, 0f, 0f, 0f);
                TMP_Text placeholder = CreateText(viewport.transform, "", 12f, FontStyles.Italic,
                    multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft);
                placeholder.color = new Color(1f, 1f, 1f, 0.35f);
                Stretch(placeholder.rectTransform, 0f, 0f, 0f, 0f);

                input = obj.GetComponent<TMP_InputField>();
                input.textViewport = viewportRect;
                input.textComponent = text;
                input.placeholder = placeholder;
            }

            input.lineType = multiline
                ? TMP_InputField.LineType.MultiLineNewline
                : TMP_InputField.LineType.SingleLine;

            if (input.textComponent != null)
            {
                input.textComponent.fontSize = Mathf.Max(input.textComponent.fontSize, 16f);
                input.textComponent.alignment = multiline
                    ? TextAlignmentOptions.TopLeft
                    : TextAlignmentOptions.MidlineLeft;
                input.textComponent.enableWordWrapping = multiline;
            }

            TMP_Text placeholderText = input.placeholder as TMP_Text;
            if (placeholderText != null)
            {
                placeholderText.fontSize = Mathf.Max(placeholderText.fontSize, 16f);
                placeholderText.alignment = multiline
                    ? TextAlignmentOptions.TopLeft
                    : TextAlignmentOptions.MidlineLeft;
            }

            LayoutElement layout = input.GetComponent<LayoutElement>();
            if (layout == null) layout = input.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = Mathf.Max(height, 40f);
            layout.preferredHeight = Mathf.Max(height, 40f);
            layout.flexibleWidth = 1f;

            inputFields.Add(input);
            return input;
        }

        private NativeDropdown CreateEnumDropdown<T>(Transform parent, float height) where T : struct
        {
            return CreateDropdown(parent, JapaneseLocalization.EnumOptions(typeof(T), Enum.GetNames(typeof(T))), height);
        }

        private NativeDropdown CreateDropdown(Transform parent, IEnumerable<string> options, float height)
        {
            return CreateDropdown(parent,
                options == null ? null : options.Select(x => new KeyValuePair<string, string>(x, x)), height);
        }

        private NativeDropdown CreateDropdown(Transform parent,
            IEnumerable<KeyValuePair<string, string>> options, float height)
        {
            parent = ResolveBuildParent(parent);
            TweakableDropdown template = inspector.GetComponentsInChildren<TweakableDropdown>(true)
                .Where(x => x != null && x.dropdownItemPrefab != null)
                .OrderBy(x => x.enumTypeString == "HitSound" ? 2 : (x.enumTypeString == "Ease" ? 1 : 0))
                .FirstOrDefault();
            if (template == null)
                template = Resources.FindObjectsOfTypeAll<TweakableDropdown>()
                    .Where(x => x != null && x.dropdownItemPrefab != null && x.gameObject.scene.IsValid())
                    .OrderBy(x => x.enumTypeString == "HitSound" ? 2 : (x.enumTypeString == "Ease" ? 1 : 0))
                    .FirstOrDefault();
            if (template == null)
                throw new InvalidOperationException("エディタ標準のドロップダウンを取得できませんでした。");

            TweakableDropdown dropdown = Instantiate(template, parent, false);
            dropdown.gameObject.name = "Editor QoL Standard Dropdown";
            dropdown.gameObject.SetActive(true);
            RectTransform rect = dropdown.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.localScale = Vector3.one;
                rect.anchoredPosition = Vector2.zero;
            }

            LayoutElement layout = dropdown.GetComponent<LayoutElement>();
            if (layout == null) layout = dropdown.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = Mathf.Max(height, 40f);
            layout.preferredHeight = Mathf.Max(height, 40f);
            layout.flexibleWidth = 1f;

            NativeDropdown result = new NativeDropdown(dropdown);
            dropdowns.Add(result);
            result.SetOptions(options);
            return result;
        }

        private Toggle CreateToggle(Transform parent, string label, bool value, float width)
        {
            GameObject root = CreateHorizontal(parent, label + " Toggle", 4f, 34f);
            LayoutElement rootLayout = root.GetComponent<LayoutElement>();
            rootLayout.preferredWidth = width;
            rootLayout.flexibleWidth = 0f;

            GameObject box = new GameObject("Box", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Toggle));
            box.transform.SetParent(root.transform, false);
            LayoutElement boxLayout = box.AddComponent<LayoutElement>();
            boxLayout.preferredWidth = 24f;
            boxLayout.preferredHeight = 24f;
            Image background = box.GetComponent<Image>();
            background.color = new Color(0.2f, 0.2f, 0.24f, 1f);

            GameObject check = new GameObject("Check", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            check.transform.SetParent(box.transform, false);
            Stretch(check.GetComponent<RectTransform>(), 4f, 4f, 4f, 4f);
            Image checkImage = check.GetComponent<Image>();
            checkImage.color = Color.white;

            Toggle toggle = box.GetComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.graphic = checkImage;
            toggle.isOn = value;
            CreateText(root.transform, label, 16f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            return toggle;
        }

        private static void SetWidth(GameObject obj, float width)
        {
            LayoutElement layout = obj.GetComponent<LayoutElement>();
            if (layout == null) layout = obj.AddComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.flexibleWidth = 0f;
        }

        private static void Stretch(RectTransform rect, float left, float right, float top, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }
    }
}
