using AlloyFramework.Audio;
using UnityEditor;
using UnityEngine;

namespace AlloyFramework.Editor
{
    internal sealed class AudioDiagnosticsWindow : EditorWindow
    {
        private string m_eventKey = string.Empty; // 手动播放的事件名称。
        private string m_parameterKey = string.Empty; // 项目制作侧定义的全局参数。
        private float m_parameterValue; // 参数值，使用项目约定单位。
        private Vector2 m_scroll; // 状态列表滚动位置。

        [MenuItem("★AlloyFramework★/音频/音频调试面板")]
        private static void OpenWindow() { GetWindow<AudioDiagnosticsWindow>("框架音频"); }

        private void OnInspectorUpdate() { Repaint(); }

        private void OnGUI()
        {
            AudioManager manager = AudioManager.Instance;
            EditorGUILayout.LabelField("后端", manager.BackendName ?? "尚未安装");
            EditorGUILayout.LabelField("就绪", manager.IsReady.ToString());
            AudioCacheInfo cache = manager.GetCacheInfo();
            EditorGUILayout.LabelField("闲置缓存组", cache.IdleGroupCount.ToString());
            EditorGUILayout.LabelField("缓存文件估算 / 预算（MiB）",
                $"{cache.EstimatedRetainedFileBytes / 1048576.0:F2} / {cache.BudgetBytes / 1048576.0:F2}");
            EditorGUILayout.LabelField("闲置保留秒数", cache.KeepAliveSeconds.ToString());
            m_eventKey = EditorGUILayout.TextField("Event", m_eventKey);
            using (new EditorGUI.DisabledScope(!Application.isPlaying || !manager.IsReady))
            {
                DrawChannelVolume(manager, EAudioVolumeChannel.Master, "总音量");
                DrawChannelVolume(manager, EAudioVolumeChannel.BGM, "BGM 音量");
                DrawChannelVolume(manager, EAudioVolumeChannel.SFX, "音效音量");
                if (GUILayout.Button("清理闲置音频缓存")) { manager.ClearIdleCache(); }
                if (GUILayout.Button("播放事件") && !string.IsNullOrWhiteSpace(m_eventKey))
                { manager.PlayAudio(m_eventKey); }
                m_parameterKey = EditorGUILayout.TextField("全局参数", m_parameterKey);
                m_parameterValue = EditorGUILayout.FloatField("数值", m_parameterValue);
                if (GUILayout.Button("设置参数"))
                {
                    AudioOperationResult result = manager.SetParameter(m_parameterKey, m_parameterValue);
                    if (!result.IsSuccess) { Debug.LogError(result.Message); }
                }
            }
            m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
            foreach (string description in manager.GetGroupDescriptions())
            { EditorGUILayout.LabelField(description); }
            foreach (AudioPlaybackInfo playback in manager.GetPlaybackSnapshots())
            {
                EditorGUILayout.LabelField($"{playback.PlayID} | {playback.Key} | Scope={playback.ScopeID}");
                EditorGUILayout.LabelField($"{playback.State} | {playback.EndReason} | {playback.Result.Error}");
                EditorGUILayout.LabelField(manager.GetBackendPlaybackDescription(playback.PlayID));
                if (!playback.Result.IsSuccess) { EditorGUILayout.HelpBox(playback.Result.Message, MessageType.Error); }
                if (manager.TryGetVolume(playback.PlayID, out float volume))
                {
                    float selectedVolume = EditorGUILayout.Slider("单次音量", volume, 0, 1);
                    if (selectedVolume != volume) { manager.SetVolume(playback.PlayID, selectedVolume); }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("暂停")) { manager.PauseAudio(playback.PlayID); }
                    if (GUILayout.Button("恢复")) { manager.ResumeAudio(playback.PlayID); }
                    if (GUILayout.Button("停止")) { manager.StopAudio(playback.PlayID); }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private static void DrawChannelVolume(AudioManager manager, EAudioVolumeChannel channel, string label)
        {
            float volume = manager.GetVolume(channel);
            float selectedVolume = EditorGUILayout.Slider(label, volume, 0, 1);
            if (selectedVolume != volume) { manager.SetVolume(channel, selectedVolume); }
        }
    }
}
