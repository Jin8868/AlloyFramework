using System.Collections.Generic;
using AlloyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace AlloyFramework.Editor
{
    [CustomPropertyDrawer(typeof(UIAnimationDefinition))]
    internal sealed class UIAnimationDefinitionDrawer : PropertyDrawer
    {
        private const int LINECOUNT = 5;
        private readonly List<string> m_keys = new List<string>(); // 框架定义的全部动效 Key 下拉选项。
        private readonly List<string> m_keyLabels = new List<string>(); // Key 下拉框显示名称。

        /// <summary>
        /// 根据折叠状态返回动效定义需要的 Inspector 高度。
        /// </summary>
        /// <param name="property">当前动效定义的序列化属性。</param>
        /// <param name="label">Inspector 显示标签。</param>
        /// <returns>绘制当前属性所需的像素高度。</returns>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded)
            {
                return EditorGUIUtility.singleLineHeight;
            }

            return EditorGUIUtility.singleLineHeight * LINECOUNT +
                   EditorGUIUtility.standardVerticalSpacing * (LINECOUNT - 1);
        }

        /// <summary>
        /// 绘制 Key、AnimationClip 和播放参数配置。
        /// </summary>
        /// <param name="position">当前属性的绘制区域。</param>
        /// <param name="property">当前动效定义的序列化属性。</param>
        /// <param name="label">Inspector 显示标签。</param>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var lineHeight = EditorGUIUtility.singleLineHeight;
            var spacing = EditorGUIUtility.standardVerticalSpacing;
            var lineRect = new Rect(position.x, position.y, position.width, lineHeight);
            property.isExpanded = EditorGUI.Foldout(lineRect, property.isExpanded, label, true);
            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                lineRect.y += lineHeight + spacing;
                DrawKeyPopup(
                    lineRect,
                    property.FindPropertyRelative("m_key"));
                lineRect.y += lineHeight + spacing;
                EditorGUI.PropertyField(
                    lineRect,
                    property.FindPropertyRelative("m_clip"),
                    new GUIContent("AnimationClip"));
                lineRect.y += lineHeight + spacing;
                EditorGUI.PropertyField(lineRect, property.FindPropertyRelative("m_timeMode"));
                lineRect.y += lineHeight + spacing;
                EditorGUI.PropertyField(lineRect, property.FindPropertyRelative("m_loop"));
            }

            EditorGUI.EndProperty();
        }

        private void DrawKeyPopup(
            Rect position,
            SerializedProperty keyProperty)
        {
            RebuildKeyOptions(keyProperty.stringValue);
            var selectedIndex = Mathf.Max(0, m_keys.IndexOf(keyProperty.stringValue));
            var nextIndex = EditorGUI.Popup(position, "Key", selectedIndex, m_keyLabels.ToArray());
            keyProperty.stringValue = m_keys[nextIndex];
        }

        private void RebuildKeyOptions(string currentKey)
        {
            m_keys.Clear();
            m_keyLabels.Clear();
            m_keys.Add(string.Empty);
            m_keyLabels.Add("未选择");
            var configuredKeys = UIAnimationKeys.All;
            for (var index = 0; index < configuredKeys.Count; index++)
            {
                var key = configuredKeys[index];
                m_keys.Add(key);
                m_keyLabels.Add(key);
            }

            if (!string.IsNullOrEmpty(currentKey) && !m_keys.Contains(currentKey))
            {
                m_keys.Add(currentKey);
                m_keyLabels.Add(currentKey + "（框架未定义）");
            }
        }

    }
}
