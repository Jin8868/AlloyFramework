using AlloyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace AlloyFramework.Editor
{
    [CustomEditor(typeof(UIAtlasImage))]
    [CanEditMultipleObjects]
    internal sealed class UIAtlasImageInspector : UnityEditor.Editor
    {
        /// <summary>
        /// 绘制组件配置，并在编辑模式和运行模式下显示当前图片的只读来源。
        /// </summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();

            // 直接在原组件下展示当前图片来源，不增加运行时组件或独立窗口。
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Debug（当前图片）", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                foreach (Object inspectedTarget in targets)
                {
                    var atlasImage = inspectedTarget as UIAtlasImage;
                    if (atlasImage == null)
                    {
                        continue;
                    }

                    if (targets.Length > 1)
                    {
                        EditorGUILayout.ObjectField("目标组件", atlasImage, typeof(UIAtlasImage), true);
                    }
                    EditorGUILayout.TextField("资源包", atlasImage.DebugPackageName);
                    EditorGUILayout.TextField("图集路径", atlasImage.DebugAtlasLocation);
                    EditorGUILayout.TextField("图片名称", atlasImage.DebugSpriteName);
                }
            }
        }

        /// <summary>
        /// 在运行期间刷新面板，使异步加载完成后可查看最新图片来源。
        /// </summary>
        /// <returns>编辑器运行期间返回真，否则返回假。</returns>
        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying;
        }
    }
}
