using AlloyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace AlloyFramework.Editor
{
    [CustomPropertyDrawer(typeof(UIBindingReferenceAttribute))]
    internal sealed class UIBindingReferenceDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.name.StartsWith("m_", System.StringComparison.Ordinal))
                label = new GUIContent(
                    ObjectNames.NicifyVariableName(property.name.Substring(2)), label.tooltip);
            using (new EditorGUI.DisabledScope(true))
                EditorGUI.PropertyField(position, property, label, true);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            EditorGUI.GetPropertyHeight(property, label, true);
    }
}
