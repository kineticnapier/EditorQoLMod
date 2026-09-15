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

        public IEnumerable<IDockablePane> CreatePanes()
        {
            yield return pane;
        }
    }

    internal sealed class AssetProbeWorkbenchPane : IDockablePane
    {
        private string targetName = "floorMeshLong";
        private string status = "ロード済みUnityオブジェクトを調査します。";
        private string summary = "未計測";
        private string reportPath = string.Empty;

        public string Id { get { return "editor-qol.asset-probe"; } }
        public string Title { get { return "Editor QoL: Asset Probe"; } }
        public bool CanClose { get { return true; } }

        public WorkbenchPaneView BuildView()
        {
            WorkbenchPaneView view = new WorkbenchPaneView()
                .Text("Asset Probe", 16f, true)
                .Text("ADOFAIが現在ロードしているGameObjectと、そのMesh / Material / Shader / Texture / Sprite / Unity参照を調査します。", 9f, false)
                .Spacer(4)
                .Text("ロード済みGameObject名", 10f, false)
                .BeginRow()
                    .Input(targetName, "set-target")
                    .Button("名前で調査", "probe-name", string.Empty, false)
                .EndRow()
                .Button("エディタ先頭床を調査", "probe-floor", string.Empty, false)
                .Spacer(6)
                .Text("結果: " + summary, 9f, true)
                .Text(status, 9f, false);

            if (!string.IsNullOrEmpty(reportPath))
                view.Text("Report: " + reportPath, 8f, false);

            return view;
        }

        public void HandleAction(string actionId, string argument)
        {
            switch (actionId)
            {
                case "set-target":
                    targetName = argument ?? string.Empty;
                    Publish();
                    return;
                case "probe-name":
                    Run(delegate { return AssetProbe.ProbeLoadedObject(targetName); });
                    return;
                case "probe-floor":
                    Run(AssetProbe.ProbeFirstEditorFloor);
                    return;
            }
        }

        private void Run(Func<AssetProbeResult> probe)
        {
            try
            {
                AssetProbeResult result = probe();
                summary = result.RootSummary + " / " + result.GameObjectCount + " objects / " +
                    result.ComponentCount + " components";
                reportPath = result.ReportPath;
                status = "調査完了 v" + ModVersion.Current;
            }
            catch (Exception ex)
            {
                summary = "失敗";
                status = "エラー: " + ex.Message;
                if (Main.Logger != null) Main.Logger.Error("Asset Probe failed: " + ex);
            }
            Publish();
        }

        private void Publish()
        {
            Workbench.PublishPane(Id);
        }
    }
}
