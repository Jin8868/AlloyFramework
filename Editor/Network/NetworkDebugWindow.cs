using UnityEditor;
using UnityEngine;

namespace AlloyFramework.Net.Editor
{
    internal sealed class NetworkDebugWindow : EditorWindow
    {
        private Vector2 m_scroll; // 会话列表滚动位置。

        [MenuItem("★AlloyFramework★/网络/网络调试面板")]
        private static void Open() => GetWindow<NetworkDebugWindow>("网络调试");
        private void OnInspectorUpdate() => Repaint();

        private void OnGUI()
        {
            var manager = NetworkManager.Instance;
            if (manager == null)
            {
                EditorGUILayout.HelpBox("框架网络管理器尚未初始化。", MessageType.Info);
                return;
            }
            m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
            foreach (var session in manager.Sessions)
            {
                EditorGUILayout.LabelField(session.Name, session.State.ToString(), EditorStyles.boldLabel);
                EditorGUILayout.LabelField("待请求 / 接收积压 / 发送积压",
                    $"{session.PendingCount} / {session.ReceiveQueueCount} / {session.SendQueueCount}");
                EditorGUILayout.LabelField("发送 / 接收字节", $"{session.SentBytes} / {session.ReceivedBytes}");
                EditorGUILayout.LabelField("超时 / 最近往返毫秒",
                    $"{session.TimeoutCount} / {session.LastRoundTripMilliseconds:F1}");
                if (session.LastError != null) EditorGUILayout.HelpBox(session.LastError.Message, MessageType.Warning);
                EditorGUILayout.Space();
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
