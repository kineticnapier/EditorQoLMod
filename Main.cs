using HarmonyLib;
using Kiner.ADOFAIEditorQoL.Core;
using Kiner.ADOFAIEditorQoL.Runtime;
using UnityModManagerNet;

namespace Kiner.ADOFAIEditorQoL
{
    internal static class Main
    {
        internal static UnityModManager.ModEntry.ModLogger Logger;
        internal static bool Enabled;
        internal static string ModPath;
        private static Harmony harmony;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Logger = modEntry.Logger;
            ModPath = modEntry.Path;
            Enabled = true;
            DropdownFavorites.Initialize(ModPath);
            EventPresetStore.Initialize(ModPath);
            harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll();
            modEntry.OnToggle = OnToggle;
            modEntry.OnUnload = OnUnload;
            Logger.Log("ADOFAI Editor QoL v" + ModVersion.Current + " loaded.");
            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;
            UI.EditorQoLPanel.SetModEnabled(value);
            if (value)
            {
                RuntimeEffects.ReactivateCurrentScene();
            }
            else
            {
                RuntimeEffects.Cleanup();
            }
            return true;
        }

        private static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            Enabled = false;
            DropdownFavorites.Save();
            EventPresetStore.Save();
            RuntimeEffects.Cleanup();
            Pacl2UndoBridge.Cleanup();
            UI.EditorQoLPanel.DestroyCurrent();
            if (harmony != null) harmony.UnpatchAll(modEntry.Info.Id);
            return true;
        }
    }
}
