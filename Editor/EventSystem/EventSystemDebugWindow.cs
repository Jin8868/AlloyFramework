#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AlloyFramework.Editor
{

/// <summary>用于检查当前事件与监听器的编辑器窗口。</summary>
public sealed class EventSystemDebugWindow : EditorWindow
{
    private readonly List<EventSystem.EventDebugInfo> _eventInfos = new List<EventSystem.EventDebugInfo>(32);
    private readonly List<EventSystem.EventListenerDebugInfo> _listenerInfos = new List<EventSystem.EventListenerDebugInfo>(16);
    private readonly List<EventSystem.DispatchDebugInfo> _dispatchInfos = new List<EventSystem.DispatchDebugInfo>(200);
    private readonly List<EventSystem.ListenerExceptionDebugInfo> _exceptionInfos = new List<EventSystem.ListenerExceptionDebugInfo>(100);
    private readonly List<EventSystem.CleanupDebugInfo> _cleanupInfos = new List<EventSystem.CleanupDebugInfo>(100);
    private Vector2 _eventScrollPosition;
    private Vector2 _listenerScrollPosition;
    private Vector2 _dispatchScrollPosition;
    private double _lastRepaintTime;
    private string _searchText = string.Empty;
    private string _selectedEventName;
    private bool _showOnlyProblemListeners = true;

    [MenuItem("★AlloyFramework★/事件系统调试器", false, 20)]
    private static void Open()
    {
        GetWindow<EventSystemDebugWindow>("Event System");
    }

    private void OnEnable()
    {
        var eventSystem = EventSystem.Instance;
        eventSystem.CaptureRegistrationStackTrace = true;
        eventSystem.CaptureDispatchStackTrace = true;
        eventSystem.WarnOnDestroyedListener = true;
    }

    private void OnInspectorUpdate()
    {
        // 仅在运行时按较低频率重绘，避免无意义地占用 Editor CPU。
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup - _lastRepaintTime < 0.2d) return;
        _lastRepaintTime = EditorApplication.timeSinceStartup;
        Repaint();
    }

    private void OnGUI()
    {
        if (!EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox("进入 Play Mode 后可查看当前存活事件。", MessageType.Info);
            return;
        }

        var eventSystem = EventSystem.Instance;
        eventSystem.DiagnosticsEnabled = true;
        eventSystem.GetEventDebugInfo(_eventInfos);
        eventSystem.GetDispatchDebugInfo(_dispatchInfos);
        eventSystem.GetListenerExceptionDebugInfo(_exceptionInfos);
        eventSystem.GetCleanupDebugInfo(_cleanupInfos);
        if (!string.IsNullOrEmpty(_selectedEventName)) eventSystem.GetListenerDebugInfo(_selectedEventName, _listenerInfos);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"事件数：{_eventInfos.Count}", EditorStyles.boldLabel);
            if (GUILayout.Button("清理已销毁监听器", GUILayout.Width(140))) eventSystem.PurgeDestroyedListeners();
            if (GUILayout.Button("清空诊断记录", GUILayout.Width(110))) eventSystem.ClearDiagnosticHistory();
            if (GUILayout.Button("导出快照", GUILayout.Width(80))) ExportSnapshot();
            if (GUILayout.Button("刷新", GUILayout.Width(60))) Repaint();
        }
        eventSystem.CaptureRegistrationStackTrace = EditorGUILayout.ToggleLeft("记录后续订阅的调用栈（仅排查时开启）", eventSystem.CaptureRegistrationStackTrace);
        eventSystem.CaptureDispatchStackTrace = EditorGUILayout.ToggleLeft("记录派发来源调用栈（仅排查时开启）", eventSystem.CaptureDispatchStackTrace);
        eventSystem.WarnOnDestroyedListener = EditorGUILayout.ToggleLeft("派发到未取消的已销毁对象时输出一次 Warning", eventSystem.WarnOnDestroyedListener);
        _showOnlyProblemListeners = EditorGUILayout.ToggleLeft("只显示泄漏风险监听器", _showOnlyProblemListeners);
        _searchText = EditorGUILayout.TextField("筛选", _searchText);

        EditorGUILayout.Space(6);
        EditorGUILayout.BeginHorizontal();
        DrawEventList();
        DrawListenerList();
        DrawDispatchHistory();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawEventList()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(position.width * 0.30f)))
        {
            EditorGUILayout.LabelField("事件", EditorStyles.boldLabel);
            _eventScrollPosition = EditorGUILayout.BeginScrollView(_eventScrollPosition, EditorStyles.helpBox);
            for (var i = 0; i < _eventInfos.Count; i++)
            {
                var info = _eventInfos[i];
                if (!Contains(info.Name)) continue;
                var label = $"{info.Name}\n监听 {info.ListenerCount} | 派发 {info.DispatchCount} | 本次 {info.LastDispatchMilliseconds:F2} ms | 最大 {info.MaxDispatchMilliseconds:F2} ms | 异常 {info.ExceptionCount}";
                if (info.InvalidListenerCount > 0 || info.UnownedManagedTargetCount > 0)
                {
                    label += $" | 无效 {info.InvalidListenerCount} | 无 owner 普通对象 {info.UnownedManagedTargetCount}";
                }
                var previousColor = GUI.contentColor;
                if (info.InvalidListenerCount > 0) GUI.contentColor = Color.red;
                else if (info.UnownedManagedTargetCount > 0) GUI.contentColor = new Color(1f, 0.65f, 0f);
                if (GUILayout.Toggle(_selectedEventName == info.Name, label, "Button")) _selectedEventName = info.Name;
                GUI.contentColor = previousColor;
            }
            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawListenerList()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(position.width * 0.35f)))
        {
            EditorGUILayout.LabelField(string.IsNullOrEmpty(_selectedEventName) ? "监听器（请选择事件）" : $"监听器：{_selectedEventName}", EditorStyles.boldLabel);
            _listenerScrollPosition = EditorGUILayout.BeginScrollView(_listenerScrollPosition, EditorStyles.helpBox);
            for (var i = 0; i < _listenerInfos.Count; i++)
            {
                var info = _listenerInfos[i];
                if (_showOnlyProblemListeners && !info.HasIssue) continue;
                if (!Contains(info.MethodName) && !Contains(info.TargetName) && !Contains(info.DeclaringType)) continue;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var previousColor = GUI.contentColor;
                    if (info.IsDestroyed || info.IsOwnerDestroyed || info.IsOwnerCollected) GUI.contentColor = Color.red;
                    else if (info.IsUnownedManagedTarget) GUI.contentColor = new Color(1f, 0.65f, 0f);
                    else if (info.IsInactiveBehaviour) GUI.contentColor = new Color(1f, 0.65f, 0f);
                    EditorGUILayout.LabelField($"{info.DeclaringType}.{info.MethodName}", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(info.TargetName);
                    if (info.IsOwnerDestroyed || info.IsOwnerCollected) EditorGUILayout.LabelField($"所有者：{info.OwnerName}（疑似未取消订阅）");
                    else if (info.IsUnownedManagedTarget) EditorGUILayout.LabelField("未关联 owner 的普通对象（高风险：无法自动判断是否泄漏）");
                    else EditorGUILayout.LabelField($"所有者：{info.OwnerName}");
                    GUI.contentColor = previousColor;
                    if (info.HasIssue)
                    {
                        EditorGUILayout.HelpBox($"{info.IssueDescription}\n建议：{info.SuggestedFix}", GetIssueMessageType(info));
                    }
                    EditorGUILayout.LabelField($"订阅：Frame {info.RegisteredFrame}  |  {new System.DateTime(info.RegisteredUtcTicks, System.DateTimeKind.Utc).ToLocalTime():HH:mm:ss}");
                    if (info.Target != null && GUILayout.Button("定位监听目标"))
                    {
                        Selection.activeObject = info.Target;
                        EditorGUIUtility.PingObject(info.Target);
                    }
                    if (!string.IsNullOrEmpty(info.RegistrationStackTrace))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField("订阅调用栈", EditorStyles.boldLabel);
                            if (GUILayout.Button("复制调用栈", GUILayout.Width(90))) EditorGUIUtility.systemCopyBuffer = info.RegistrationStackTrace;
                        }
                        EditorGUILayout.TextArea(info.RegistrationStackTrace, EditorStyles.miniLabel);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawDispatchHistory()
    {
        using (new EditorGUILayout.VerticalScope())
        {
            EditorGUILayout.LabelField("最近派发（最新在前）", EditorStyles.boldLabel);
            _dispatchScrollPosition = EditorGUILayout.BeginScrollView(_dispatchScrollPosition, EditorStyles.helpBox);
            for (var i = 0; i < _dispatchInfos.Count; i++)
            {
                var info = _dispatchInfos[i];
                if (!Contains(info.EventName)) continue;
                EditorGUILayout.LabelField($"{new System.DateTime(info.UtcTicks, System.DateTimeKind.Utc).ToLocalTime():HH:mm:ss.fff}  {info.EventName}");
                EditorGUILayout.LabelField($"{info.Signature}  |  {info.ParameterSummary}  |  监听 {info.ListenerCount}  |  {info.DurationMilliseconds:F3} ms", EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(info.SourceStackTrace)) EditorGUILayout.TextArea(info.SourceStackTrace, EditorStyles.miniLabel);
            }
            if (_exceptionInfos.Count > 0)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("监听器异常", EditorStyles.boldLabel);
                for (var i = 0; i < _exceptionInfos.Count; i++)
                {
                    var info = _exceptionInfos[i];
                    EditorGUILayout.LabelField($"{info.EventName} -> {info.Listener}\n{info.ExceptionType}: {info.Message}", EditorStyles.helpBox);
                }
            }
            if (_cleanupInfos.Count > 0)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("自动清理历史", EditorStyles.boldLabel);
                for (var i = 0; i < _cleanupInfos.Count; i++)
                {
                    var info = _cleanupInfos[i];
                    EditorGUILayout.LabelField($"{new System.DateTime(info.UtcTicks, System.DateTimeKind.Utc).ToLocalTime():HH:mm:ss}  {info.EventName} -> {info.Listener}\n{info.Reason}", EditorStyles.helpBox);
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }

    private void ExportSnapshot()
    {
        var path = EditorUtility.SaveFilePanel("Export Event System diagnostics", Application.dataPath, "EventSystemDiagnostics", "txt");
        if (string.IsNullOrEmpty(path)) return;
        var text = new StringBuilder(4096);
        text.AppendLine("Event System Diagnostics Snapshot");
        text.AppendLine(System.DateTime.Now.ToString("O"));
        text.AppendLine("\nEvents:");
        for (var i = 0; i < _eventInfos.Count; i++) text.AppendLine($"{_eventInfos[i].Name} | listeners={_eventInfos[i].ListenerCount} | dispatches={_eventInfos[i].DispatchCount} | totalMs={_eventInfos[i].TotalDispatchMilliseconds:F3} | maxMs={_eventInfos[i].MaxDispatchMilliseconds:F3} | exceptions={_eventInfos[i].ExceptionCount}");
        text.AppendLine("\nListener Risks:");
        for (var i = 0; i < _eventInfos.Count; i++)
        {
            var eventInfo = _eventInfos[i];
            if (eventInfo.InvalidListenerCount == 0 && eventInfo.UnownedManagedTargetCount == 0) continue;
            text.AppendLine($"{eventInfo.Name} | invalid={eventInfo.InvalidListenerCount} | unownedManaged={eventInfo.UnownedManagedTargetCount}");
        }
        text.AppendLine("\nDispatches:");
        for (var i = 0; i < _dispatchInfos.Count; i++) text.AppendLine($"{_dispatchInfos[i].EventName} | {_dispatchInfos[i].ParameterSummary} | {_dispatchInfos[i].DurationMilliseconds:F3} ms");
        text.AppendLine("\nExceptions:");
        for (var i = 0; i < _exceptionInfos.Count; i++) text.AppendLine($"{_exceptionInfos[i].EventName} | {_exceptionInfos[i].Listener} | {_exceptionInfos[i].ExceptionType}: {_exceptionInfos[i].Message}");
        text.AppendLine("\nCleanup:");
        for (var i = 0; i < _cleanupInfos.Count; i++) text.AppendLine($"{_cleanupInfos[i].EventName} | {_cleanupInfos[i].Listener} | {_cleanupInfos[i].Reason}");
        File.WriteAllText(path, text.ToString(), Encoding.UTF8);
        EditorUtility.RevealInFinder(path);
    }

    private bool Contains(string value)
    {
        return string.IsNullOrEmpty(_searchText) || value.IndexOf(_searchText, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static MessageType GetIssueMessageType(EventSystem.EventListenerDebugInfo info)
    {
        return info.IsDestroyed || info.IsOwnerDestroyed || info.IsOwnerCollected ? MessageType.Error : MessageType.Warning;
    }
}
}
#endif

