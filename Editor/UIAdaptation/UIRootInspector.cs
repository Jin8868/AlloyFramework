using AlloyFramework.UI;
using UnityEditor;

namespace AlloyFramework.Editor
{
    [CustomEditor(typeof(UIRoot))]
    internal sealed class UIRootInspector : UnityEditor.Editor
    {
        private bool m_isConfigurationExpanded = true; // 屏幕适配配置区是否展开。

        /// <summary>
        /// 绘制 UIRoot 的屏幕适配配置。
        /// </summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(
                serializedObject,
                "m_Script",
                "m_orientationMode",
                "m_landscapeReferenceResolution",
                "m_landscapeMatchWidthOrHeight",
                "m_portraitReferenceResolution",
                "m_portraitMatchWidthOrHeight");
            EditorGUILayout.Space();
            m_isConfigurationExpanded = EditorGUILayout.Foldout(
                m_isConfigurationExpanded,
                "屏幕适配配置",
                true);
            if (m_isConfigurationExpanded)
            {
                DrawOrientationConfiguration();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawOrientationConfiguration()
        {
            var orientationMode = serializedObject.FindProperty("m_orientationMode");
            EditorGUILayout.PropertyField(orientationMode);
            var mode = (EUIScreenOrientationMode)orientationMode.enumValueIndex;
            if (mode != EUIScreenOrientationMode.FixedPortrait)
            {
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("m_landscapeReferenceResolution"));
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("m_landscapeMatchWidthOrHeight"));
            }

            if (mode != EUIScreenOrientationMode.FixedLandscape)
            {
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("m_portraitReferenceResolution"));
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("m_portraitMatchWidthOrHeight"));
            }
        }

    }
}
