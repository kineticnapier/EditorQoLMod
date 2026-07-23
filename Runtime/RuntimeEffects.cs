using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Runtime
{
    internal static class RuntimeEffects
    {
        internal static void Cleanup()
        {
            DestroyRuntimeComponents<TextMaskRuntime>();
            DestroyRuntimeComponents<TutorialBackgroundRuntime>();
            DestroyRuntimeComponents<MultiTilePlanetRuntime>();
            DestroyRuntimeComponents<MultiTileDecorationRuntime>();
            DestroyRuntimeComponents<CustomTextFontRuntime>();
            CustomTextFontRuntime.ClearCachedFonts();
            SpriteAlphaMaskUtils.doRefreshMaskCache = true;
        }

        internal static void CleanupPlaybackEffects()
        {
            DestroyRuntimeComponents<TutorialBackgroundRuntime>();
            DestroyRuntimeComponents<MultiTilePlanetRuntime>();
        }

        internal static void ReactivateCurrentScene()
        {
            scnEditor editor = scnEditor.instance;
            if (editor != null)
            {
                UI.EditorQoLPanel.Attach(editor);
                try
                {
                    editor.UpdateDecorationObjects();
                }
                catch
                {
                    // Some editor fields are not initialized during early scene startup.
                }
            }

            scnGame game = scnGame.instance;
            if (game != null)
            {
                TutorialBackgroundRuntime runtime = game.GetComponent<TutorialBackgroundRuntime>();
                if (runtime == null) runtime = game.gameObject.AddComponent<TutorialBackgroundRuntime>();
                runtime.Configure(game);

                MultiTilePlanetRuntime planetRuntime = game.GetComponent<MultiTilePlanetRuntime>();
                if (planetRuntime == null)
                    planetRuntime = game.gameObject.AddComponent<MultiTilePlanetRuntime>();
                planetRuntime.Configure(game);
            }

            MultiTileDecorationRuntime.AttachAllInCurrentScenes();
        }

        private static void DestroyRuntimeComponents<T>() where T : MonoBehaviour
        {
            T[] components = Resources.FindObjectsOfTypeAll<T>();
            for (int i = 0; i < components.Length; i++)
            {
                T component = components[i];
                if (component == null || component.gameObject == null ||
                    !component.gameObject.scene.IsValid())
                    continue;

                IRuntimeEffect runtimeEffect = component as IRuntimeEffect;
                if (runtimeEffect != null) runtimeEffect.StopAndRestore();
                Object.Destroy(component);
            }
        }
    }

    internal interface IRuntimeEffect
    {
        void StopAndRestore();
    }
}
