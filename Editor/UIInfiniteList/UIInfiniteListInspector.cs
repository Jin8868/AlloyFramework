using AlloyFramework.UI;
using UnityEditor;
using UnityEditor.UI;
using UnityEngine;

namespace AlloyFramework.Editor
{
    /// <summary>在 ScrollRect 原有设置下展示虚拟列表布局参数。</summary>
    [CustomEditor(typeof(UIInfiniteList), true)]
    [CanEditMultipleObjects]
    public sealed class UIInfiniteListInspector : ScrollRectEditor
    {
        /// <summary>绘制滚动设置和虚拟化配置。</summary>
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            serializedObject.Update();

            // 原生 ScrollRect Inspector 不会自动绘制派生类字段。
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("无限列表", EditorStyles.boldLabel);
            // 只允许预制体资源，避免把场景实例当成长期默认模板。
            var prefabProperty = serializedObject.FindProperty("m_defaultPrefab");
            EditorGUI.showMixedValue = prefabProperty.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            var prefab = (UIListItem)EditorGUILayout.ObjectField(
                new GUIContent("默认 Item 预制体", "拖入根节点挂有 UIListItem 子类的预制体资源。"),
                prefabProperty.objectReferenceValue,
                typeof(UIListItem),
                false);
            if (EditorGUI.EndChangeCheck()) prefabProperty.objectReferenceValue = prefab;
            EditorGUI.showMixedValue = false;
            EditorGUILayout.HelpBox(
                "固定模板：配置默认预制体后调用 BindData(items)。动态模板：调用 Bind(prefab, items) 显式指定。",
                MessageType.Info);
            if (target is UIIrregularList)
            {
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("m_layoutBehaviour"), new GUIContent("布局行为"));
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("m_templates"), new GUIContent("额外模板"), true);
                EditorGUILayout.HelpBox(
                    "布局行为挂在列表同一节点。编号 0 使用默认模板，其他正编号对应额外模板。",
                    MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_itemSize"), new GUIContent("Item 尺寸"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_spacing"), new GUIContent("滚动方向间距"));
            if (!(target is UIIrregularList))
            {
                // 交叉轴固定项数，主轴可选按视口均分或保留 Item 尺寸。
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_crossAxisCount"),
                    new GUIContent("每行列数 / 每列行数", "纵向滚动配置列数，横向滚动配置行数。"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_crossAxisSpacing"),
                    new GUIContent("交叉方向间距"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_fitCrossAxis"),
                    new GUIContent("交叉方向均分视口"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_visibleLineCount"),
                    new GUIContent("可见行数 / 列数", "按视口分配滚动方向的项尺寸，0 表示使用 Item 尺寸。"));
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_padding"), new GUIContent("内容边距"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_buffer"), new GUIContent("缓冲距离"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("m_maxInactiveItems"), new GUIContent("空闲缓存上限"));
            EditorGUILayout.HelpBox(
                "只启用一个滚动轴；Content 必须直接位于 Viewport 下，且不能启用布局组或尺寸适配器。",
                MessageType.Info);
            serializedObject.ApplyModifiedProperties();
        }
    }
}