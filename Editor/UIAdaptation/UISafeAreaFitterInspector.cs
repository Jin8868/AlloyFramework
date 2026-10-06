using AlloyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace AlloyFramework.Editor
{
    [CustomEditor(typeof(UISafeAreaFitter))]
    internal sealed class UISafeAreaFitterInspector : UnityEditor.Editor
    {
        private SerializedProperty m_currentAnchorMax; // 当前最终右上锚点属性。
        private SerializedProperty m_currentAnchorMin; // 当前最终左下锚点属性。
        private SerializedProperty m_currentSafeArea; // 当前安全区域属性。

        private void OnEnable()
        {
            m_currentSafeArea = serializedObject.FindProperty("m_currentSafeArea");
            m_currentAnchorMin = serializedObject.FindProperty("m_currentAnchorMin");
            m_currentAnchorMax = serializedObject.FindProperty("m_currentAnchorMax");
        }

        /// <summary>
        /// 绘制安全区适配组件的配置和只读运行时结果。
        /// </summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // 配置项由预制体作者维护，运行时计算结果始终保持只读。
            DrawPropertiesExcluding(
                serializedObject,
                "m_Script",
                "m_currentSafeArea",
                "m_currentAnchorMin",
                "m_currentAnchorMax");
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("当前适配结果", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(m_currentSafeArea);
                EditorGUILayout.PropertyField(m_currentAnchorMin);
                EditorGUILayout.PropertyField(m_currentAnchorMax);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
