using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace AlloyFramework.Net.Editor
{
    internal sealed class NetworkCodeGeneratorWindow : EditorWindow
    {
        private NetworkGenerationSettings m_settings; // 当前窗口的生成参数，不读写配置文件。
        private readonly HashSet<string> m_selected = new HashSet<string>(StringComparer.Ordinal); // 实际勾选集合。
        private string[] m_sources = Array.Empty<string>(); // 搜索前的完整协议列表。
        private string m_search = ""; // 文件名及相对路径搜索词。
        private Vector2 m_scroll; // 列表滚动位置。
        private NetworkGenerationPlan m_plan; // 最近一次依赖分析结果。
        private string m_status = "请勾选协议，分析依赖后生成。"; // 操作反馈。
        private MessageType m_statusType = MessageType.Info; // 反馈级别。
        private bool m_showDirectories; // 框架固定目录默认折叠。
        private bool m_showParameters; // 生成参数默认折叠。
        private SearchField m_searchField; // 复用 Unity 原生搜索控件。
        private GUIStyle m_titleStyle; // 窗口标题样式。
        private GUIStyle m_fileStyle; // 协议文件名样式。
        private GUIStyle m_pathStyle; // 协议相对路径样式。
        private GUIStyle m_directoryStyle; // 支持复制的只读目录样式。

        [MenuItem("★AlloyFramework★/网络/协议代码生成")]
        private static void Open() => GetWindow<NetworkCodeGeneratorWindow>("协议代码生成");

        private void OnEnable()
        {
            minSize = new Vector2(520, 440);
            wantsMouseMove = true;
            m_searchField = new SearchField();
            try
            {
                m_settings = new NetworkGenerationSettings();
                RefreshSources();
            }
            catch (Exception exception) { ShowFailure(exception); }
        }

        private void OnGUI()
        {
            if (m_settings == null) { EditorGUILayout.HelpBox(m_status, m_statusType); return; }
            InitializeStyles();

            // 目录与生成参数收纳到折叠区，让协议选择成为窗口的主要内容。
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("协议代码生成", m_titleStyle);
            EditorGUILayout.LabelField("选择 .proto 文件，生成消息、分部服务与协议注册表。", EditorStyles.miniLabel);
            EditorGUILayout.Space(6);
            DrawDirectories();
            DrawParameters();
            EditorGUILayout.Space(6);
            DrawSearchToolbar();
            DrawSelectionToolbar();
            DrawSources();
            DrawActions();
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(m_status, m_statusType);
        }

        private void InitializeStyles()
        {
            if (m_titleStyle != null) return;
            m_titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 17 };
            m_fileStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft };
            m_pathStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
            m_directoryStyle = new GUIStyle(EditorStyles.textField) { fontSize = 11 };
            m_searchField = m_searchField ?? new SearchField();
        }

        private void DrawDirectories()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            m_showDirectories = EditorGUILayout.Foldout(m_showDirectories, "框架固定目录 · 只读", true);
            if (m_showDirectories)
            {
                // 路径由框架统一定义，只读文本仍允许选择复制。
                DrawDirectory("协议源目录", m_settings.ProtoRoot);
                DrawDirectory("生成输出目录", m_settings.OutputRoot);
                DrawDirectory("业务回调目录", m_settings.BusinessServiceRoot);
                DrawDirectory("protoc 34.1", m_settings.ProtocPath);
                DrawDirectory("标准协议目录", m_settings.StandardInclude);
                DrawDirectory("框架选项目录", m_settings.OptionsRoot);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawDirectory(string label, string path)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(100));
            EditorGUILayout.SelectableLabel(path, m_directoryStyle, GUILayout.Height(20));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawParameters()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            m_showParameters = EditorGUILayout.Foldout(m_showParameters, "生成参数", true);
            if (m_showParameters)
            {
                EditorGUI.BeginChangeCheck();
                m_settings.GeneratedNamespace =
                    EditorGUILayout.TextField("协议注册命名空间", m_settings.GeneratedNamespace);
                m_settings.ServiceNamespace = EditorGUILayout.TextField("Service 命名空间", m_settings.ServiceNamespace);
                m_settings.ServiceAssembly = EditorGUILayout.TextField("Service 所属程序集", m_settings.ServiceAssembly);
                m_settings.ProtocolVersion = EditorGUILayout.IntField("协议版本", m_settings.ProtocolVersion);
                if (EditorGUI.EndChangeCheck()) m_plan = null;
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawSearchToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            var searchRect = GUILayoutUtility.GetRect(100, 22, GUILayout.ExpandWidth(true));
            searchRect.y += 2;
            searchRect.height = 18;
            EditorGUI.BeginChangeCheck();
            m_search = m_searchField.OnGUI(searchRect, m_search);
            if (EditorGUI.EndChangeCheck()) m_scroll = Vector2.zero;
            if (GUILayout.Button("刷新文件", EditorStyles.toolbarButton, GUILayout.Width(76)))
                RunAction(RefreshSources);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSelectionToolbar()
        {
            int visibleCount = 0;
            foreach (string source in m_sources)
                if (MatchesSearch(source)) visibleCount++;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"已选 {m_selected.Count} / {m_sources.Length}    搜索结果 {visibleCount}",
                EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(visibleCount == 0))
            {
                if (GUILayout.Button("全选结果", EditorStyles.miniButtonLeft, GUILayout.Width(70)))
                    SelectVisible(true);
                if (GUILayout.Button("取消结果", EditorStyles.miniButtonRight, GUILayout.Width(70)))
                    SelectVisible(false);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSources()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
            int visibleCount = 0;
            foreach (string source in m_sources)
            {
                if (!MatchesSearch(source)) continue;
                DrawSource(source, visibleCount);
                visibleCount++;
            }
            if (visibleCount == 0)
            {
                GUILayout.Space(30);
                EditorGUILayout.LabelField(m_sources.Length == 0 ? "还没有协议文件" : "没有匹配的协议",
                    EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.LabelField(m_sources.Length == 0
                    ? "将 .proto 放入 Protocol/Proto 后刷新文件。"
                    : "尝试其他文件名或相对路径；已勾选的协议会保留。", EditorStyles.centeredGreyMiniLabel);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawSource(string source, int index)
        {
            var row = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
            bool previous = m_selected.Contains(source);
            bool hovered = row.Contains(Event.current.mousePosition);
            var background = EditorGUIUtility.isProSkin
                ? new Color(1, 1, 1, index % 2 == 0 ? 0.035f : 0.065f)
                : new Color(0, 0, 0, index % 2 == 0 ? 0.025f : 0.055f);
            if (hovered) background = new Color(0.35f, 0.55f, 0.75f, 0.12f);
            if (previous) background = new Color(0.20f, 0.48f, 0.80f, 0.22f);
            EditorGUI.DrawRect(row, background);

            // 文件名突出显示，路径作为辅助信息；整行点击与复选框使用同一勾选集合。
            var toggleRect = new Rect(row.x + 8, row.y + 16, 18, 18);
            bool current = EditorGUI.Toggle(toggleRect, previous);
            GUI.Label(new Rect(row.x + 34, row.y + 12, 24, 24), EditorGUIUtility.IconContent("TextAsset Icon"));
            GUI.Label(new Rect(row.x + 66, row.y + 5, row.width - 74, 22),
                new GUIContent(Path.GetFileName(source), source), m_fileStyle);
            GUI.Label(new Rect(row.x + 66, row.y + 27, row.width - 74, 18),
                new GUIContent(source, source), m_pathStyle);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && hovered &&
                !toggleRect.Contains(Event.current.mousePosition))
            {
                current = !previous;
                Event.current.Use();
            }
            if (current != previous)
            {
                if (current) m_selected.Add(source); else m_selected.Remove(source);
                m_plan = null;
                Repaint();
            }
            if (Event.current.type == EventType.MouseMove) Repaint();
        }

        private bool MatchesSearch(string source)
        {
            return source.IndexOf(m_search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SelectVisible(bool selected)
        {
            // 批量操作仅作用于搜索结果，不清除被搜索隐藏的其他勾选项。
            foreach (string source in m_sources)
            {
                if (!MatchesSearch(source)) continue;
                if (selected) m_selected.Add(source); else m_selected.Remove(source);
            }
            m_plan = null;
        }

        private void DrawActions()
        {
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(m_selected.Count == 0))
            {
                if (GUILayout.Button("分析依赖", GUILayout.Height(30))) RunAction(Analyze);
                if (GUILayout.Button($"生成所选协议 ({m_selected.Count})", GUILayout.Height(30))) RunAction(Generate);
            }
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
        }

        private void RefreshSources()
        {
            m_sources = NetworkCodeGenerator.ListSources(m_settings);
            m_selected.IntersectWith(m_sources);
            m_plan = null;
        }

        private void Analyze()
        {
            m_settings.SelectedFiles = m_selected.OrderBy(path => path, StringComparer.Ordinal).ToArray();
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
