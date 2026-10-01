using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace AlloyFramework.Editor
{
    public static class FrameworkMenu
    {
        private const string MenuRoot = "★AlloyFramework★/";
        private const string BootScenePath = "Assets/Res/Scenes/Boot.unity";

        [MenuItem(MenuRoot + "启动框架 _F5", false, 0)]
        private static void StartFramework()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            var bootScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(BootScenePath);
            if (bootScene == null)
            {
                AlloyDebug.Error($"Boot scene was not found: {BootScenePath}");
                return;
            }

            var activeScene = SceneManager.GetActiveScene();
            if (!string.Equals(activeScene.path, BootScenePath, StringComparison.Ordinal) &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EditorSceneManager.playModeStartScene = bootScene;
            EditorApplication.isPlaying = true;
        }

        [MenuItem(MenuRoot + "配置表", false, 1)]
        private static void OpenConfigTable()
        {
        }
    }
}
