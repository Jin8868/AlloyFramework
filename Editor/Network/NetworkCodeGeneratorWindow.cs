using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AlloyFramework.Net.Editor
{
    internal sealed class NetworkCodeGeneratorWindow : EditorWindow
    {
        private NetworkGenerationSettings m_settings; // 项目生成配置。
        private readonly HashSet<string> m_selected = new HashSet<string>(StringComparer.Ordinal); // 实际勾选集合。
        private string[] m_sources = Array.Empty<string>(); // 搜索前的完整协议列表。
        private string m_search = ""; // 文件名及相对路径搜索词。
        private Vector2 m_scroll; // 列表滚动位置。
        private NetworkGenerationPlan m_plan; // 最近一次依赖分析结果。
        private string m_status = "请勾选协议，分析依赖后生成。"; // 操作反馈。
        private MessageType m_statusType = MessageType.Info; // 反馈级别。

        [MenuItem("★AlloyFramework★/网络/协议代码生成")]
        private static void Open() => GetWindow<NetworkCodeGeneratorWindow>("协议代码生成");

        private void OnEnable()
        {
            try
            {
                m_settings = NetworkGenerationSettings.Load();
                m_selected.UnionWith(m_settings.SelectedFiles ?? Array.Empty<string>());
                RefreshSources();
            }
            catch (Exception exception) { ShowFailure(exception); }
        }

        private void OnGUI()
        {
            if (m_settings == null) { EditorGUILayout.HelpBox(m_status, m_statusType); return; }
            EditorGUI.BeginChangeCheck();
            m_settings.ProtoRoot = EditorGUILayout.TextField("Proto 根目录", m_settings.ProtoRoot);
            m_settings.ProtocPath = EditorGUILayout.TextField("protoc 34.1", m_settings.ProtocPath);
            m_settings.StandardInclude = EditorGUILayout.TextField("标准 include", m_settings.StandardInclude);
            m_settings.OptionsRoot = EditorGUILayout.TextField("框架 option 根目录", m_settings.OptionsRoot);
            m_settings.OutputRoot = EditorGUILayout.TextField("生成目录", m_settings.OutputRoot);
            m_settings.GeneratedNamespace = EditorGUILayout.TextField("协议注册命名空间", m_settings.GeneratedNamespace);
            m_settings.ServiceNamespace = EditorGUILayout.TextField("Service 命名空间", m_settings.ServiceNamespace);
            m_settings.ServiceAssembly = EditorGUILayout.TextField("Service 所属程序集", m_settings.ServiceAssembly);
            m_settings.BusinessServiceRoot =
                EditorGUILayout.TextField("业务处理目录", m_settings.BusinessServiceRoot);
            m_settings.ProtocolVersion = EditorGUILayout.IntField("协议版本", m_settings.ProtocolVersion);
            if (EditorGUI.EndChangeCheck()) m_plan = null;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("刷新文件")) RunAction(RefreshSources);
            if (GUILayout.Button("保存配置")) RunAction(SaveSettings);
            EditorGUILayout.EndHorizontal();

            m_search = EditorGUILayout.TextField("搜索文件名或路径", m_search);
            EditorGUILayout.LabelField($"已勾选 {m_selected.Count} / {m_sources.Length}；搜索不改变勾选。");
            m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
            foreach (string source in m_sources)
            {
                if (source.IndexOf(m_search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                bool previous = m_selected.Contains(source);
                bool current = EditorGUILayout.ToggleLeft(source, previous);
                if (current == previous) continue;
                if (current) m_selected.Add(source); else m_selected.Remove(source);
                m_plan = null;
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("分析依赖与受影响协议")) RunAction(Analyze);
            if (GUILayout.Button("生成勾选项")) RunAction(Generate);
            EditorGUILayout.EndHorizontal();
            if (m_plan != null && m_plan.RequiredSelection.Count > 0)
            {
                EditorGUILayout.HelpBox("需要补选：\n" + string.Join("\n", m_plan.RequiredSelection), MessageType.Warning);
                if (GUILayout.Button("勾选所需依赖和受影响协议"))
                {
                    m_selected.UnionWith(m_plan.RequiredSelection);
                    m_plan = null;
                    RunAction(Analyze);
                }
            }
            EditorGUILayout.HelpBox(m_status, m_statusType);
        }

        private void RefreshSources()
        {
            m_sources = NetworkCodeGenerator.ListSources(m_settings);
            m_selected.IntersectWith(m_sources);
            m_plan = null;
        }

        private void SaveSettings()
        {
            m_settings.SelectedFiles = m_selected.OrderBy(path => path, StringComparer.Ordinal).ToArray();
            m_settings.Save();
        }

        private void Analyze()
        {
            SaveSettings();
            m_plan = NetworkCodeGenerator.Analyze(m_settings);
            m_status = m_plan.RequiredSelection.Count == 0
                ? "依赖有效；只重写勾选协议，注册表保留其他已生成协议。"
                : "存在缺失或过期依赖、受影响协议；补选后才能提交一致的输出。";
            m_statusType = m_plan.RequiredSelection.Count == 0 ? MessageType.Info : MessageType.Warning;
        }

        private void Generate()
        {
            // 空选择不运行生成器，避免只更新注册表却被理解为生成了协议。
            if (m_selected.Count == 0)
            {
                m_status = "请先勾选至少一个 .proto 文件。";
                m_statusType = MessageType.Warning;
                return;
            }
            // 生成前重新读取源和清单，避免使用上次分析后已变化的文件。
            Analyze();
            if (m_plan.RequiredSelection.Count > 0) return;
            NetworkCodeGenerator.Generate(m_settings, m_plan);
            AssetDatabase.Refresh();
            m_status = $"生成成功（{DateTime.Now:HH:mm:ss}）：{m_selected.Count} 个协议及总注册表。" +
                "输出内容相同时保留原文件，不改变修改时间。";
            m_statusType = MessageType.Info;
            m_plan = null;
            Debug.Log(m_status + "\n输出目录：" + m_settings.OutputRoot);
            Repaint();
        }

        private void RunAction(Action action)
        {
            try { action(); }
            catch (Exception exception) { ShowFailure(exception); }
        }

        private void ShowFailure(Exception exception)
        {
            m_status = exception.Message;
            m_statusType = MessageType.Error;
            Debug.LogException(exception);
        }
    }
}
