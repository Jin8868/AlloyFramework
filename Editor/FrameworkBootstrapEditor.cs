using System;
using UnityEditor;
using UnityEngine;

namespace AlloyFramework.Editor
{
    /// <summary>绘制框架固定入口及其主包配置，业务无需编写入口编辑器。</summary>
    [CustomEditor(typeof(FrameworkBootstrap))]
    public sealed class FrameworkBootstrapEditor : UnityEditor.Editor
    {
        private UnityEditor.Editor m_settingsEditor; // 主包配置资产的缓存编辑器。

        /// <summary>绘制场景引用、共用配置及配置校验结果。</summary>
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var bootstrap = (FrameworkBootstrap)target;
            if (bootstrap.Settings == null)
            {
                EditorGUILayout.HelpBox("请选择主包启动配置资产。", MessageType.Error);
                return;
            }

            // 只检查配置值，不调用业务配置工厂，避免编辑 Inspector 时安装运行时服务。
            try
            {
                bootstrap.Settings.Validate();
            }
            catch (InvalidOperationException exception)
            {
                EditorGUILayout.HelpBox(exception.Message, MessageType.Error);
            }

            // 运行与构建必须使用同一份资产，业务只声明构建侧的固定配置地址。
            string configurationPath = bootstrap.Settings.BuildConfigurationResourcePath;
            if (bootstrap.Settings != Resources.Load<FrameworkBootSettings>(configurationPath))
            {
                EditorGUILayout.HelpBox("场景配置与构建工具使用的 Resources 配置不一致。", MessageType.Error);
            }

            EditorGUILayout.HelpBox("框架固定执行启动流程；此处仅配置运行参数与展示资源。", MessageType.Info);
            CreateCachedEditor(bootstrap.Settings, null, ref m_settingsEditor);
            m_settingsEditor.OnInspectorGUI();
        }

        private void OnDisable()
        {
            if (m_settingsEditor != null) DestroyImmediate(m_settingsEditor);
        }
    }
}
