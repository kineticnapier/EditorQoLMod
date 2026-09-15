using System;
using System.Collections.Generic;
using Kiner.ADOFAIEditorQoL.Runtime;
using KineticNapier.ADOFAIWorkbench;

namespace Kiner.ADOFAIEditorQoL.UI
{
    internal static class AssetProbeWorkbenchIntegration
    {
        private static readonly AssetProbeWorkbenchPaneProvider Provider = new AssetProbeWorkbenchPaneProvider();
        private static bool registered;

        internal static void Initialize()
        {
            if (registered) return;
            Workbench.RegisterPaneProvider(Provider);
            registered = true;
        }

        internal static void Shutdown()
        {
            if (!registered) return;
            Workbench.UnregisterPaneProvider(Provider);
            registered = false;
        }
    }

    internal sealed class AssetProbeWorkbenchPaneProvider : IDockablePaneProvider
    {
        private readonly AssetProbeWorkbenchPane pane = new AssetProbeWorkbenchPane();
        public IEnumerable<IDockablePane> CreatePanes() { yield return pane; }
    }

    internal sealed class AssetProbeWorkbenchPane : IDockablePane
    {
        private string targetName = "floorMeshLong";
        private string status = "ロード済みUnityオブジェクトを調査します。";
        private string summary = "未計測";
        private string reportPath = string.Empty;
        private string exportDirectory = string.Empty;

        public string Id { get { return "editor-qol.asset-probe"; } }
        public string Title { get { return "Editor QoL: Asset Probe"; } }
        public bool CanClose { get { return true; } }

        public WorkbenchPaneView BuildView()
        {
            WorkbenchPaneView view = new WorkbenchPaneView()
                .Text("Asset Probe", 16f, true)
                .Text("Mesh/Material/Textureと、エディタが実際に使うイベント・床アイコン対応を調査します。", 9f, false)
                .Spacer(4)
                .Button("イベント/床アイコン辞書を一括調査", "probe-icon-catalog", string.Empty, false)
                .Text("LevelEventType / category / RDConstants特殊床アイコンを列挙し、Sprite領域をPNGへ切り出します。", 8f, false)
                .Spacer(4)
                .Button("純正 meshFloor prefab を調査", "probe-mesh-floor", string.Empty, false)
                .Button("純正 spriteFloor prefab を調査", "probe-sprite-floor", string.Empty, false)
                .Button("現在のエディタ先頭床を調査", "probe-floor", string.Empty, false)
                .Spacer(4)
                .Text("その他のロード済みGameObject名", 10f, false)
                .BeginRow()
                    .Input(targetName, "set-target")
                    .Button("名前で調査", "probe-name", string.Empty, false)
                .EndRow()
                .Spacer(6)
                .Text("結果: " + summary, 9f, true)
                .Text(status, 9f, false);

            if (!string.IsNullOrEmpty(reportPath)) view.Text("Report: " + reportPath, 8f, false);
            if (!string.IsNullOrEmpty(exportDirectory)) view.Text("Assets: " + exportDirectory, 8f, false);
            return view;
        }

        public void HandleAction(string actionId, string argument)
        {
            switch (actionId)
            {
                case "set-target": targetName = argument ?? string.Empty; Publish(); return;
                case "probe-icon-catalog": Run(IconCatalogProbe.Probe); return;
                case "probe-mesh-floor": Run(AssetProbe.ProbeMeshFloorPrefab); return;
                case "probe-sprite-floor": Run(AssetProbe.ProbeSpriteFloorPrefab); return;
                case "probe-floor": Run(AssetProbe.ProbeFirstEditorFloor); return;
                case "probe-name": Run(delegate { return AssetProbe.ProbeLoadedObject(targetName); }); return;
            }
        }

        private void Run(Func<AssetProbeResult> probe)
        {
            try
            {
                AssetProbeResult result = probe();
                summary = result.RootSummary + " / " + result.GameObjectCount + " objects / " + result.ComponentCount +
                    " components / " + result.ExportedTextureCount + " PNGs";
                reportPath = result.ReportPath;
                exportDirectory = result.ExportDirectory;
                status = "調査・書き出し完了 v" + ModVersion.Current;
            }
            catch (Exception ex)
            {
                summary = "失敗";
                status = "エラー: " + ex.Message;
                if (Main.Logger != null) Main.Logger.Error("Asset Probe failed: " + ex);
            }
            Publish();
        }

        private void Publish() { Workbench.PublishPane(Id); }
    }
}
