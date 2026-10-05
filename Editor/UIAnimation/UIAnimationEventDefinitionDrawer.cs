using System.Collections.Generic;
using AlloyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace AlloyFramework.Editor
{
    [CustomPropertyDrawer(typeof(UIAnimationEventDefinition))]
    internal sealed class UIAnimationEventDefinitionDrawer : PropertyDrawer
    {
        private const int LINECOUNT = 5;
        private readonly List<string> m_animationKeys = new List<string>(); // 当前 Player 已配置的动效 Key。
        private readonly List<string> m_eventKeys = new List<string>(); // 框架定义的帧事件 Key。

        /// <summary>
        /// 根据折叠状态返回帧事件定义需要的 Inspector 高度。
        /// </summary>
        /// <param name="property">当前帧事件定义的序列化属性。</param>
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
        /// 绘制目标动效、事件 Key 和触发时间配置。
        /// </summary>
        /// <param name="position">当前属性的绘制区域。</param>
        /// <param name="property">当前帧事件定义的序列化属性。</param>
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
                DrawAnimationKey(lineRect, property.FindPropertyRelative("m_animationKey"), property);
                lineRect.y += lineHeight + spacing;
                DrawAnimationClip(
                    lineRect,
                    property.serializedObject.targetObject as UIAnimationPlayer,
                    property.FindPropertyRelative("m_animationKey").stringValue);
                lineRect.y += lineHeight + spacing;
                DrawEventKey(lineRect, property.FindPropertyRelative("m_eventKey"));
                lineRect.y += lineHeight + spacing;
                DrawTime(
                    lineRect,
                    property.FindPropertyRelative("m_time"),
                    property.serializedObject.targetObject as UIAnimationPlayer,
                    property.FindPropertyRelative("m_animationKey").stringValue);
            }

            EditorGUI.EndProperty();
        }

        private void DrawAnimationKey(
            Rect position,
            SerializedProperty keyProperty,
            SerializedProperty eventProperty)
        {
            m_animationKeys.Clear();
            m_animationKeys.Add("未选择");
            var player = eventProperty.serializedObject.targetObject as UIAnimationPlayer;
            if (player != null)
            {
                var animations = player.Animations;
                for (var index = 0; index < animations.Count; index++)
                {
                    var definition = animations[index];
                    if (definition != null && !string.IsNullOrEmpty(definition.Key))
                    {
                        m_animationKeys.Add(definition.Key);
                    }
                }
            }

            var selectedIndex = Mathf.Max(0, m_animationKeys.IndexOf(keyProperty.stringValue));
            var nextIndex = EditorGUI.Popup(position, "Animation Key", selectedIndex, m_animationKeys.ToArray());
            keyProperty.stringValue = nextIndex == 0 ? string.Empty : m_animationKeys[nextIndex];
        }

        private void DrawEventKey(Rect position, SerializedProperty keyProperty)
        {
            m_eventKeys.Clear();
            m_eventKeys.Add("未选择");
            var eventKeys = UIAnimationEventKeys.All;
            for (var index = 0; index < eventKeys.Count; index++)
            {
                m_eventKeys.Add(eventKeys[index]);
            }

            var selectedIndex = Mathf.Max(0, m_eventKeys.IndexOf(keyProperty.stringValue));
            var nextIndex = EditorGUI.Popup(position, "Event Key", selectedIndex, m_eventKeys.ToArray());
            keyProperty.stringValue = nextIndex == 0 ? string.Empty : m_eventKeys[nextIndex];
        }

        private static void DrawAnimationClip(
            Rect position,
            UIAnimationPlayer player,
            string animationKey)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.ObjectField(
                    position,
                    "AnimationClip",
                    GetAnimationClip(player, animationKey),
                    typeof(AnimationClip),
                    false);
            }
        }

        private static void DrawTime(
            Rect position,
            SerializedProperty timeProperty,
            UIAnimationPlayer player,
            string animationKey)
        {
            var duration = GetAnimationDuration(player, animationKey);
            if (duration <= 0f)
            {
                EditorGUI.PropertyField(position, timeProperty, new GUIContent("Time"));
                return;
            }

            timeProperty.floatValue = EditorGUI.Slider(
                position,
                "Time",
                timeProperty.floatValue,
                0f,
                duration);
        }

        private static float GetAnimationDuration(UIAnimationPlayer player, string animationKey)
        {
            var clip = GetAnimationClip(player, animationKey);
            return clip == null ? 0f : clip.length;
        }

        private static AnimationClip GetAnimationClip(UIAnimationPlayer player, string animationKey)
        {
            if (player == null || string.IsNullOrEmpty(animationKey))
            {
                return null;
            }

            var animations = player.Animations;
            for (var index = 0; index < animations.Count; index++)
            {
                var definition = animations[index];
                if (definition != null && definition.Key == animationKey && definition.Clip != null)
                {
                    return definition.Clip;
                }
            }

            return null;
        }
    }
}
