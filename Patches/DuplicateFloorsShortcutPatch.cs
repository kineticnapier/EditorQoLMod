using ADOFAI.Editor.Actions;
using HarmonyLib;
using Kiner.ADOFAIEditorQoL.Core;

namespace Kiner.ADOFAIEditorQoL.Patches
{
    [HarmonyPatch(typeof(DuplicateDecorationsEditorAction), "Execute")]
    internal static class DuplicateFloorsShortcutPatch
    {
        private static bool Prefix(scnEditor editor)
        {
            if (!Main.Enabled || editor == null) return true;
            if (!editor.SelectionDecorationIsEmpty()) return true;
            if (editor.selectedFloors == null || editor.selectedFloors.Count == 0) return true;

            try
            {
                TileTransformOperations.RepeatSelection(editor, 1, FloorDuplicateMode.TilesEventsDecorations);
            }
            catch (System.Exception ex)
            {
                if (Main.Logger != null) Main.Logger.Warning("Ctrl+Dによる床複製に失敗しました: " + ex.Message);
            }
            return false;
        }
    }
}
