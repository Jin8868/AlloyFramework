using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AlloyFramework.Editor
{
    internal sealed class ConfigTableWindow : EditorWindow
    {
        private const string CONFIGTABLESDIRECTORY = "DesignData/Luban/Datas"; // Excel 配置表相对目录。
        private const string LUBANGENERATESCRIPT = "Tools/Luban/Generate.ps1"; // Luban 生成脚本相对路径。
        private const float HEADERHEIGHT = 86f; // 窗口头部区域高度。
        private const float ROWHEIGHT = 52f; // 单个配置表条目的高度。
        private const float LOGHEIGHT = 180f; // 配置生成日志区域高度。

        private readonly List<string> m_excelPaths = new List<string>(); // 当前发现的 Excel 源表路径。
        private readonly Queue<string> m_pendingLogLines = new Queue<string>(); // 等待主线程显示的生成日志。
        private readonly StringBuilder m_generationLog = new StringBuilder(); // 本次配置生成的完整日志。
        private readonly object m_logLock = new object(); // 保护后台进程写入的日志队列。

        private Vector2 m_scrollPosition; // 配置表列表的滚动位置。
        private Vector2 m_logScrollPosition; // 生成日志区域的滚动位置。
        private string m_searchText = string.Empty; // 当前文件名筛选条件。
        private string m_statusMessage = string.Empty; // 最近一次操作结果。
        private MessageType m_statusType = MessageType.Info; // 最近一次操作结果的提示类型。
        private Process m_generationProcess; // 当前运行的配置生成进程。
        private bool m_isGenerating; // 当前是否正在生成配置。
        private bool m_showGenerationLog; // 当前是否显示配置生成日志。
        private bool m_generationFailed; // 最近一次配置生成是否失败。
        private GUIStyle m_titleStyle; // 顶部标题的显示样式。
        private GUIStyle m_subtitleStyle; // 顶部说明的显示样式。
        private GUIStyle m_actionButtonStyle; // 顶部操作按钮的显示样式。
        private GUIStyle m_fileNameStyle; // 配置表名称的显示样式。
        private GUIStyle m_directoryStyle; // 配置表相对目录的显示样式。
        private GUIStyle m_badgeStyle; // 文件格式标识的显示样式。
        private GUIStyle m_hintStyle; // 列表操作提示的显示样式。
        private GUIStyle m_emptyStyle; // 空列表说明的显示样式。
        private GUIStyle m_logStyle; // 生成日志文本的显示样式。

        internal static void OpenWindow()
        {
            var window = GetWindow<ConfigTableWindow>("配置表");
            window.minSize = new Vector2(680f, 440f);
            window.Show();
        }

        private void OnEnable()
        {
            ConfigureStyles();
            RefreshExcelPaths();
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdateGenerationProcess;
            StopGenerationProcess();
        }

        private void OnGUI()
        {
            ConfigureStyles();
            DrawHeader();
            DrawSearchField();
            DrawListHeader();
            DrawExcelList();
            DrawGenerationLog();
            DrawStatusMessage();
        }

        private void ConfigureStyles()
        {
            m_titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 18,
                normal = { textColor = Color.white }
            };
            m_subtitleStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 11,
                normal = { textColor = new Color(0.74f, 0.81f, 0.9f) }
            };
            m_actionButtonStyle = new GUIStyle(EditorStyles.miniButton)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                fixedHeight = 27f
            };
            m_fileNameStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 12
            };
            m_directoryStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.52f, 0.57f, 0.64f) }
            };
            m_badgeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 9,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.3f, 0.78f, 0.46f) }
            };
            m_hintStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.5f, 0.54f, 0.6f) }
            };
            m_emptyStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12
            };
            m_logStyle = new GUIStyle(EditorStyles.textArea)
            {
                font = EditorStyles.standardFont,
                fontSize = 11,
                wordWrap = false
            };
        }

        private void DrawHeader()
        {
            var headerRect = GUILayoutUtility.GetRect(0f, HEADERHEIGHT, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(headerRect, new Color(0.11f, 0.17f, 0.25f));

            var titleRect = new Rect(headerRect.x + 20f, headerRect.y + 13f, 270f, 27f);
            var subtitleRect = new Rect(headerRect.x + 20f, headerRect.y + 41f, 340f, 20f);
            GUI.Label(titleRect, "Luban 配置表", m_titleStyle);
            GUI.Label(subtitleRect, $"已发现 {m_excelPaths.Count} 个 Excel 配置表", m_subtitleStyle);

            var buttonRight = headerRect.xMax - 16f;
            var generateButtonText = m_isGenerating ? "正在生成..." : "重新生成配置";
            using (new EditorGUI.DisabledScope(m_isGenerating))
            {
                buttonRight = DrawHeaderButton(
                    buttonRight,
                    headerRect.y + 29f,
                    generateButtonText,
                    RegenerateConfig);
            }
            buttonRight -= 8f;
            buttonRight = DrawHeaderButton(buttonRight, headerRect.y + 29f, "打开目录", OpenConfigTablesDirectory);
            buttonRight -= 8f;
            DrawHeaderButton(buttonRight, headerRect.y + 29f, "刷新", RefreshExcelPaths);
        }

        private float DrawHeaderButton(float right, float top, string text, Action action)
        {
            var isGenerateButton = text == "重新生成配置" || text == "正在生成...";
            var width = isGenerateButton ? 112f : 70f;
            var buttonRect = new Rect(right - width, top, width, 27f);
            if (GUI.Button(buttonRect, text, m_actionButtonStyle))
            {
                action();
            }

            return buttonRect.x;
        }

        private void DrawSearchField()
        {
            var searchRect = GUILayoutUtility.GetRect(0f, 46f, GUILayout.ExpandWidth(true));
            var backgroundRect = new Rect(searchRect.x + 12f, searchRect.y + 8f, searchRect.width - 24f, 30f);
            EditorGUI.DrawRect(backgroundRect, new Color(0.18f, 0.18f, 0.18f));

            var labelRect = new Rect(backgroundRect.x + 10f, backgroundRect.y, 44f, backgroundRect.height);
            var inputRect = new Rect(backgroundRect.x + 54f, backgroundRect.y + 4f, backgroundRect.width - 88f, 22f);
            var clearRect = new Rect(backgroundRect.xMax - 28f, backgroundRect.y + 4f, 24f, 22f);
            GUI.Label(labelRect, "搜索", EditorStyles.miniLabel);

            var searchText = GUI.TextField(inputRect, m_searchText, EditorStyles.toolbarSearchField);
            if (searchText != m_searchText)
            {
                m_searchText = searchText;
            }

            if (GUI.Button(clearRect, "×", EditorStyles.miniButton) && !string.IsNullOrEmpty(m_searchText))
            {
                m_searchText = string.Empty;
                GUI.FocusControl(null);
            }
        }

        private void DrawListHeader()
        {
            var headerRect = GUILayoutUtility.GetRect(0f, 25f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(headerRect, new Color(0.15f, 0.15f, 0.15f));
            GUI.Label(
                new Rect(headerRect.x + 18f, headerRect.y, 220f, headerRect.height),
                "配置表文件",
                EditorStyles.miniBoldLabel);
            GUI.Label(
                new Rect(headerRect.xMax - 120f, headerRect.y, 100f, headerRect.height),
                "双击打开",
                m_hintStyle);
        }

        private void DrawExcelList()
        {
            if (GetVisibleExcelCount() == 0)
            {
                DrawEmptyState();
                return;
            }

            m_scrollPosition = EditorGUILayout.BeginScrollView(m_scrollPosition);
            for (var index = 0; index < m_excelPaths.Count; index++)
            {
                var excelPath = m_excelPaths[index];
                if (MatchesSearch(excelPath))
                {
                    DrawExcelEntry(excelPath, index);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawEmptyState()
        {
            var emptyRect = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
            var message = m_excelPaths.Count == 0
                ? "配置表目录中还没有 Excel 文件"
                : "没有匹配的配置表";
            GUI.Label(emptyRect, message, m_emptyStyle);
        }

        private void DrawExcelEntry(string excelPath, int index)
        {
            var rowRect = GUILayoutUtility.GetRect(0f, ROWHEIGHT, GUILayout.ExpandWidth(true));
            var isHovered = rowRect.Contains(Event.current.mousePosition);
            var isAlternateRow = index % 2 == 0;
            var backgroundColor = isHovered
                ? new Color(0.22f, 0.34f, 0.48f)
                : isAlternateRow
                    ? new Color(0.17f, 0.17f, 0.17f)
                    : new Color(0.14f, 0.14f, 0.14f);
            EditorGUI.DrawRect(rowRect, backgroundColor);

            var relativePath = GetRelativeConfigPath(excelPath);
            var fileName = Path.GetFileName(relativePath);
            var directoryName = Path.GetDirectoryName(relativePath);
            var fileNameRect = new Rect(rowRect.x + 18f, rowRect.y + 7f, rowRect.width - 190f, 20f);
            var directoryRect = new Rect(rowRect.x + 18f, rowRect.y + 28f, rowRect.width - 190f, 18f);
            var badgeRect = new Rect(rowRect.xMax - 118f, rowRect.y + 15f, 42f, 22f);
            var hintRect = new Rect(rowRect.xMax - 72f, rowRect.y, 58f, ROWHEIGHT);
            GUI.Label(fileNameRect, fileName, m_fileNameStyle);
            GUI.Label(directoryRect, string.IsNullOrEmpty(directoryName) ? "根目录" : directoryName, m_directoryStyle);
            GUI.Label(badgeRect, "XLSX", m_badgeStyle);
            GUI.Label(hintRect, "打开 ›", m_hintStyle);

            EditorGUIUtility.AddCursorRect(rowRect, MouseCursor.Link);
            var currentEvent = Event.current;
            if (currentEvent.type == EventType.MouseDown &&
                currentEvent.clickCount == 2 &&
                rowRect.Contains(currentEvent.mousePosition))
            {
                OpenExcelFile(excelPath);
                currentEvent.Use();
            }
        }

        private void DrawStatusMessage()
        {
            if (string.IsNullOrEmpty(m_statusMessage))
            {
                return;
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(m_statusMessage, m_statusType);
        }

        private void DrawGenerationLog()
        {
            if (!m_showGenerationLog)
            {
                return;
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                m_isGenerating ? "生成日志 · 执行中" : "生成日志 · 执行失败",
                EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (m_generationFailed && GUILayout.Button("复制失败日志", GUILayout.Width(100f)))
            {
                CopyGenerationLog();
            }

            EditorGUILayout.EndHorizontal();

            var logText = m_generationLog.Length == 0 ? "等待生成工具输出..." : m_generationLog.ToString();
            m_logScrollPosition = EditorGUILayout.BeginScrollView(
                m_logScrollPosition,
                GUILayout.Height(LOGHEIGHT));
            var logHeight = Mathf.Max(
                LOGHEIGHT - 8f,
                m_logStyle.CalcHeight(new GUIContent(logText), position.width - 42f));
            var logRect = GUILayoutUtility.GetRect(
                0f,
                logHeight,
                GUILayout.ExpandWidth(true));
            EditorGUI.SelectableLabel(logRect, logText, m_logStyle);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void RefreshExcelPaths()
        {
            m_excelPaths.Clear();

            var configTablesDirectory = GetConfigTablesDirectory();
            if (!Directory.Exists(configTablesDirectory))
            {
                SetStatus($"未找到配置表目录：{configTablesDirectory}", MessageType.Error);
                return;
            }

            var excelPaths = Directory.GetFiles(
                configTablesDirectory,
                "*.xlsx",
                SearchOption.AllDirectories);
            Array.Sort(excelPaths, StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < excelPaths.Length; index++)
            {
                var excelPath = excelPaths[index];
                if (!Path.GetFileName(excelPath).StartsWith("~$", StringComparison.Ordinal))
                {
                    m_excelPaths.Add(excelPath);
                }
            }

            SetStatus($"已发现 {m_excelPaths.Count} 个配置表。", MessageType.Info);
        }

        private void OpenConfigTablesDirectory()
        {
            var configTablesDirectory = GetConfigTablesDirectory();
            if (!Directory.Exists(configTablesDirectory))
            {
                SetStatus($"未找到配置表目录：{configTablesDirectory}", MessageType.Error);
                return;
            }

            EditorUtility.RevealInFinder(configTablesDirectory);
        }

        private void OpenExcelFile(string excelPath)
        {
            try
            {
                var startInfo = new ProcessStartInfo(excelPath)
                {
                    UseShellExecute = true
                };

                Process.Start(startInfo);
                SetStatus($"已打开配置表：{GetRelativeConfigPath(excelPath)}", MessageType.Info);
            }
            catch (Exception exception)
            {
                SetStatus($"打开配置表失败：{exception.Message}", MessageType.Error);
            }
        }

        private void RegenerateConfig()
        {
            if (m_isGenerating)
            {
                return;
            }

            var generateScriptPath = Path.Combine(GetRepositoryRoot(), LUBANGENERATESCRIPT);
            if (!File.Exists(generateScriptPath))
            {
                SetStatus($"未找到 Luban 生成脚本：{generateScriptPath}", MessageType.Error);
                return;
            }

            try
            {
                ResetGenerationLog();
                var startInfo = new ProcessStartInfo("powershell.exe")
                {
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{generateScriptPath}\"",
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    StandardErrorEncoding = Encoding.UTF8,
                    StandardOutputEncoding = Encoding.UTF8,
                    UseShellExecute = false,
                    WorkingDirectory = GetRepositoryRoot()
                };
                var process = new Process
                {
                    EnableRaisingEvents = false,
                    StartInfo = startInfo
                };
                process.OutputDataReceived += OnGenerationOutputReceived;
                process.ErrorDataReceived += OnGenerationErrorReceived;
                if (!process.Start())
                {
                    process.Dispose();
                    CompleteGenerationFailure("无法启动 PowerShell 生成进程。");
                    return;
                }

                m_generationProcess = process;
                m_isGenerating = true;
                m_showGenerationLog = true;
                m_generationFailed = false;
                SetStatus("正在生成配置代码和二进制数据...", MessageType.Info);
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                EditorApplication.update -= UpdateGenerationProcess;
                EditorApplication.update += UpdateGenerationProcess;
            }
            catch (Exception exception)
            {
                EditorApplication.update -= UpdateGenerationProcess;
                DrainPendingLogLines();
                StopGenerationProcess();
                CompleteGenerationFailure($"启动 Luban 失败：{exception}");
            }
        }

        private void UpdateGenerationProcess()
        {
            try
            {
                DrainPendingLogLines();
                if (m_generationProcess == null || !m_generationProcess.HasExited)
                {
                    Repaint();
                    return;
                }

                // WaitForExit 确保异步输出事件已经全部完成，再根据退出码确定最终结果。
                m_generationProcess.WaitForExit();
                DrainPendingLogLines();
                var exitCode = m_generationProcess.ExitCode;
                DisposeGenerationProcess();
                EditorApplication.update -= UpdateGenerationProcess;

                if (exitCode == 0)
                {
                    CompleteGenerationSuccess();
                    return;
                }

                CompleteGenerationFailure($"配置生成失败，进程退出码：{exitCode}。");
            }
            catch (Exception exception)
            {
                EditorApplication.update -= UpdateGenerationProcess;
                StopGenerationProcess();
                CompleteGenerationFailure($"读取配置生成结果失败：{exception}");
            }
        }

        private void OnGenerationOutputReceived(object sender, DataReceivedEventArgs eventArguments)
        {
            EnqueueGenerationLog(eventArguments.Data);
        }

        private void OnGenerationErrorReceived(object sender, DataReceivedEventArgs eventArguments)
        {
            if (eventArguments.Data != null)
            {
                EnqueueGenerationLog($"[错误] {eventArguments.Data}");
            }
        }

        private void EnqueueGenerationLog(string logLine)
        {
            if (logLine == null)
            {
                return;
            }

            lock (m_logLock)
            {
                m_pendingLogLines.Enqueue(logLine);
            }
        }

        private void DrainPendingLogLines()
        {
            var appendedLine = false;
            lock (m_logLock)
            {
                while (m_pendingLogLines.Count > 0)
                {
                    m_generationLog.AppendLine(m_pendingLogLines.Dequeue());
                    appendedLine = true;
                }
            }

            if (appendedLine)
            {
                m_logScrollPosition.y = float.MaxValue;
            }
        }

        private void CompleteGenerationSuccess()
        {
            m_isGenerating = false;
            m_showGenerationLog = false;
            m_generationFailed = false;
            AssetDatabase.Refresh();
            SetStatus("配置代码和二进制配置生成完成。", MessageType.Info);
            EditorUtility.DisplayDialog(
                "配置生成完成",
                "配置代码和二进制配置已经成功生成。",
                "确定");
            Repaint();
        }

        private void CompleteGenerationFailure(string failureMessage)
        {
            m_isGenerating = false;
            m_showGenerationLog = true;
            m_generationFailed = true;
            if (!string.IsNullOrWhiteSpace(failureMessage))
            {
                m_generationLog.AppendLine(failureMessage);
            }

            SetStatus("配置生成失败，请查看并复制下方日志。", MessageType.Error);
            Repaint();
        }

        private void CopyGenerationLog()
        {
            EditorGUIUtility.systemCopyBuffer = m_generationLog.ToString();
            ShowNotification(new GUIContent("失败日志已复制"));
        }

        private void ResetGenerationLog()
        {
            lock (m_logLock)
            {
                m_pendingLogLines.Clear();
            }

            m_generationLog.Clear();
            m_logScrollPosition = Vector2.zero;
        }

        private void StopGenerationProcess()
        {
            if (m_generationProcess == null)
            {
                return;
            }

            if (!m_generationProcess.HasExited)
            {
                m_generationProcess.Kill();
            }

            DisposeGenerationProcess();
            m_isGenerating = false;
        }

        private void DisposeGenerationProcess()
        {
            if (m_generationProcess == null)
            {
                return;
            }

            m_generationProcess.OutputDataReceived -= OnGenerationOutputReceived;
            m_generationProcess.ErrorDataReceived -= OnGenerationErrorReceived;
            m_generationProcess.Dispose();
            m_generationProcess = null;
        }

        private void SetStatus(string message, MessageType messageType)
        {
            m_statusMessage = message;
            m_statusType = messageType;
        }

        private int GetVisibleExcelCount()
        {
            var count = 0;
            for (var index = 0; index < m_excelPaths.Count; index++)
            {
                if (MatchesSearch(m_excelPaths[index]))
                {
                    count++;
                }
            }

            return count;
        }

        private bool MatchesSearch(string excelPath)
        {
            return string.IsNullOrEmpty(m_searchText) ||
                   Path.GetFileName(excelPath).IndexOf(
                       m_searchText,
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string GetRelativeConfigPath(string excelPath)
        {
            var configTablesDirectory = GetConfigTablesDirectory();
            return excelPath.Substring(configTablesDirectory.Length).TrimStart(Path.DirectorySeparatorChar);
        }

        private static string GetConfigTablesDirectory()
        {
            return Path.Combine(GetRepositoryRoot(), CONFIGTABLESDIRECTORY);
        }

        private static string GetRepositoryRoot()
        {
            return Directory.GetParent(Application.dataPath).Parent.FullName;
        }
    }
}
