using System;
using AlloyFramework.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AlloyFramework.Editor
{
    [CustomEditor(typeof(UIView), true)]
    internal sealed class UIViewInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("AlloyFramework UI", EditorStyles.boldLabel);

            var view = (UIView)target;
            var prefabPath = GetEditablePrefabPath(view);
            if (string.IsNullOrEmpty(prefabPath))
            {
                EditorGUILayout.HelpBox(
                    "请打开 UI 预制体后刷新绑定，场景中的预制体实例不支持直接生成。",
                    MessageType.Info);
                using (new EditorGUI.DisabledScope(true))
                    GUILayout.Button("重新生成 View 绑定");
                return;
            }

            EditorGUILayout.LabelField("预制体", prefabPath);
            var prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);
            var settings = UIAuthoringSettings.TryLoad(prefabGuid);
            if (settings == null || !settings.HasGenerated)
            {
                EditorGUILayout.HelpBox(
                    "没有找到该界面的生成配置，请先通过 UI 生成器完成首次生成。",
                    MessageType.Warning);
                using (new EditorGUI.DisabledScope(true))
                    GUILayout.Button("重新生成 View 绑定");
                return;
            }

            if (!GUILayout.Button("重新生成 View 绑定", GUILayout.Height(28f))) return;

            try
            {
                SavePrefabStageIfNeeded(view, prefabPath);
                UIAuthoringGenerator.Generate(settings);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static string GetEditablePrefabPath(UIView view)
        {
            if (EditorUtility.IsPersistent(view.gameObject))
                return AssetDatabase.GetAssetPath(view.gameObject);

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && view.transform.root.gameObject == stage.prefabContentsRoot)
                return stage.assetPath;

            return string.Empty;
        }

        private static void SavePrefabStageIfNeeded(UIView view, string prefabPath)
        {
            if (EditorUtility.IsPersistent(view.gameObject)) return;

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && view.transform.root.gameObject == stage.prefabContentsRoot)
                PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, prefabPath);
        }
    }
}