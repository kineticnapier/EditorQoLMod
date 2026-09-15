using System;
using System.Reflection;
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
            EditorQoLPreferences.Initialize(ModPath);
            SelectionRangeStore.Initialize(ModPath);
            UI.EditorQoLWorkbenchIntegration.Initialize();

            harmony = new Harmony(modEntry.Info.Id);
            PatchAllResilient(harmony);
            Patches.LargeLevelHarmonyDiagnostics.EnsureExplicitBridge(harmony);

            modEntry.OnToggle = OnToggle;
            modEntry.OnUnload = OnUnload;
            Logger.Log("ADOFAI Editor QoL v" + ModVersion.Current + " loaded.");
            return true;
        }

        private static void PatchAllResilient(Harmony harmonyInstance)
        {
            Assembly assembly = typeof(Main).Assembly;
            Type incompatibleApplyCoreProfiler = typeof(Patches.LargeLevelProfileApplyCoreEventsPatch);

            foreach (Type type in assembly.GetTypes())
            {
                if (!HasHarmonyPatch(type)) continue;

                if (type == incompatibleApplyCoreProfiler)
                {
                    Logger.Warning("Skipped incompatible optional profiler patch: " + type.FullName +
                                   " (ApplyCoreEventsToFloors signature differs on this game build).");
                    continue;
                }

                try
                {
                    harmonyInstance.CreateClassProcessor(type).Patch();
                }
                catch (Exception ex)
                {
                    // A single stale optional patch must not make the entire QoL mod unload.
                    Logger.Error("Harmony patch skipped for " + type.FullName + ": " + ex);
                }
            }
        }

        private static bool HasHarmonyPatch(Type type)
        {
            if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length > 0)
            {
                return true;
            }

            MethodInfo[] methods = type.GetMethods(BindingFlags.Static | BindingFlags.Instance |
                                                   BindingFlags.Public | BindingFlags.NonPublic |
                                                   BindingFlags.DeclaredOnly);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].GetCustomAttributes(typeof(HarmonyPatch), true).Length > 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;
            UI.EditorQoLPanel.SetModEnabled(value);
            if (value)
            {
                UI.EditorQoLWorkbenchIntegration.Initialize();
                RuntimeEffects.ReactivateCurrentScene();
            }
            else
            {
                UI.EditorQoLWorkbenchIntegration.Shutdown();
                RuntimeEffects.Cleanup();
            }
            return true;
        }

        private static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            Enabled = false;
            DropdownFavorites.Save();
            EventPresetStore.Save();
            EditorQoLPreferences.Save();
            SelectionRangeStore.Save();
            RuntimeEffects.Cleanup();
            Pacl2UndoBridge.Cleanup();
            UI.EditorQoLWorkbenchIntegration.Shutdown();
            UI.EditorQoLPanel.DestroyCurrent();
            if (harmony != null) harmony.UnpatchAll(modEntry.Info.Id);
            return true;
        }
    }
}
