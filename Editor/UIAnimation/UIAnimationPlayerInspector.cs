using System.Collections.Generic;
using AlloyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace AlloyFramework.Editor
{
    [CustomEditor(typeof(UIAnimationPlayer))]
    internal sealed class UIAnimationPlayerInspector : UnityEditor.Editor
    {
        private SerializedProperty m_animationsProperty; // 全部动效序列化配置。
        private SerializedProperty m_animationEventsProperty; // 全部帧事件序列化配置。
        private readonly List<UIAnimationDefinition> m_debugDefinitions =
            new List<UIAnimationDefinition>(); // Debug 可选动效定义。
        private readonly List<string> m_debugLabels = new List<string>(); // Debug 下拉显示名称。
        private bool m_debugFoldout; // 是否展开 Debug 区域。
        private bool m_previewActive; // 当前 Inspector 是否持有预览。
        private bool m_previewPlaying; // Edit Mode 预览是否正在推进。
        private bool m_ownsAnimationMode; // 是否由当前 Inspector 开启 AnimationMode。
        private int m_selectedDefinitionIndex; // 当前 Debug 动效索引。
        private float m_previewTime; // Edit Mode 当前预览时间。
        private double m_lastUpdateTime; // Edit Mode 上一次更新时刻。

        private UIAnimationPlayer Player => (UIAnimationPlayer)target;

        private void OnEnable()
        {
            m_animationsProperty = serializedObject.FindProperty("m_animations");
            m_animationEventsProperty = serializedObject.FindProperty("m_animationEvents");
            EditorApplication.update += UpdatePreview;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += EndPreview;
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdatePreview;
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= EndPreview;
            EndPreview();
        }

        /// <summary>
        /// 绘制 UI 动效配置、校验结果和只读调试预览。
        /// </summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDebug();
            EditorGUILayout.Space(8f);
            EditorGUILayout.PropertyField(m_animationsProperty, new GUIContent("动效配置"), true);
            EditorGUILayout.Space(8f);
            EditorGUILayout.PropertyField(m_animationEventsProperty, new GUIContent("帧事件"), true);
            serializedObject.ApplyModifiedProperties();

            DrawValidation();
        }

        private void DrawValidation()
        {
            var errors = UIAnimationValidation.GetErrors(Player);
            for (var index = 0; index < errors.Count; index++)
            {
                EditorGUILayout.HelpBox(errors[index], MessageType.Error);
            }

            if (GUILayout.Button("校验配置"))
            {
                if (errors.Count == 0)
                {
                    Debug.Log($"UI 动效配置校验通过：{Player.name}", Player);
                }
                else
                {
                    Debug.LogError(string.Join("\n", errors), Player);
                }
            }
        }

        private void DrawDebug()
        {
            var wasExpanded = m_debugFoldout;
            m_debugFoldout = EditorGUILayout.Foldout(m_debugFoldout, "Debug", true);
            if (!m_debugFoldout)
            {
                if (wasExpanded)
                {
                    EndPreview();
                }

                return;
            }

            if (UIAnimationValidation.GetErrors(Player).Count > 0)
            {
                EditorGUILayout.HelpBox("请先修复配置错误，再使用 Debug 预览。", MessageType.Warning);
                return;
            }

            RebuildDebugDefinitions();
            EditorGUILayout.HelpBox(
                "编辑模式和运行模式均可预览。帧事件在下方数组配置，Debug 预览不会派发业务事件。",
                MessageType.Info);
            if (m_debugDefinitions.Count == 0)
            {
                EditorGUILayout.HelpBox("当前 Player 没有可预览的完整动效配置。", MessageType.Info);
                return;
            }

            m_selectedDefinitionIndex = Mathf.Clamp(
                m_selectedDefinitionIndex,
                0,
                m_debugDefinitions.Count - 1);
            EditorGUI.BeginChangeCheck();
            var selectedIndex = EditorGUILayout.Popup(
                "Animation Key",
                m_selectedDefinitionIndex,
                m_debugLabels.ToArray());
            if (EditorGUI.EndChangeCheck() || !m_previewActive)
            {
                SelectDefinition(selectedIndex);
            }

            var definition = m_debugDefinitions[m_selectedDefinitionIndex];
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Clip", definition.Clip, typeof(AnimationClip), false);
            }

            if (!Application.isPlaying && EditorUtility.IsPersistent(Player.gameObject))
            {
                EditorGUILayout.HelpBox(
                    "编辑模式预览需要在 Prefab Mode 或场景实例上执行。",
                    MessageType.Info);
                if (GUILayout.Button("打开 Prefab Mode 预览"))
                {
                    AssetDatabase.OpenAsset(Player.gameObject);
                }

                return;
            }

            if (Application.isPlaying && m_previewActive && !Player.CanRestoreInspectorPose)
            {
                EditorGUILayout.HelpBox(
                    "该运行时实例在进入 Debug 前没有采样记录，退出后无法恢复原姿态。",
                    MessageType.Warning);
            }

            DrawPreviewControls(definition);
        }

        private void DrawPreviewControls(UIAnimationDefinition definition)
        {
            var duration = definition.Clip.length;
            var currentTime = Application.isPlaying ? Player.CurrentTime : m_previewTime;
            EditorGUI.BeginChangeCheck();
            var selectedTime = EditorGUILayout.Slider("Time", currentTime, 0f, duration);
            if (EditorGUI.EndChangeCheck())
            {
                Seek(selectedTime);
            }

            var frameRate = definition.Clip.frameRate;
            var currentFrame = frameRate <= 0f ? 0 : Mathf.RoundToInt(currentTime * frameRate);
            var totalFrames = frameRate <= 0f ? 0 : Mathf.RoundToInt(duration * frameRate);
            EditorGUILayout.LabelField(
                $"{currentTime:F3}s / {duration:F3}s    Frame {currentFrame} / {totalFrames}    {frameRate:F2} fps");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("|<"))
                {
                    Seek(0f);
                }

                if (GUILayout.Button("<"))
                {
                    StepFrame(-1, definition);
                }

                var isPlaying = Application.isPlaying
                    ? Player.PlaybackState == EUIAnimationPlaybackState.Debugging && m_previewPlaying
                    : m_previewPlaying;
                if (GUILayout.Button(isPlaying ? "暂停" : "播放"))
                {
                    TogglePlay();
                }

                if (GUILayout.Button(">"))
                {
                    StepFrame(1, definition);
                }

                if (GUILayout.Button(">|"))
                {
                    Seek(duration);
                }
            }

            if (GUILayout.Button("退出 Debug"))
            {
                EndPreview();
                m_debugFoldout = false;
            }
        }

        private void RebuildDebugDefinitions()
        {
            m_debugDefinitions.Clear();
            m_debugLabels.Clear();
            var animations = Player.Animations;
            for (var index = 0; index < animations.Count; index++)
            {
                var definition = animations[index];
                AddDebugDefinition(definition, definition?.Key ?? $"Animation[{index}]");
            }
        }

        private void AddDebugDefinition(UIAnimationDefinition definition, string label)
        {
            if (definition?.Clip == null)
            {
                return;
            }

            m_debugDefinitions.Add(definition);
            m_debugLabels.Add(label);
        }

        private void SelectDefinition(int selectedIndex)
        {
            EndPreview();
            m_selectedDefinitionIndex = Mathf.Clamp(selectedIndex, 0, m_debugDefinitions.Count - 1);
            m_previewTime = 0f;
            var definition = m_debugDefinitions[m_selectedDefinitionIndex];
            if (!Application.isPlaying && EditorUtility.IsPersistent(Player.gameObject))
            {
                m_previewActive = false;
                return;
            }

            m_previewActive = true;
            if (Application.isPlaying)
            {
                Player.BeginInspectorDebug(definition);
                return;
            }

            StartAnimationMode();
            SampleEditMode(0f);
        }

        private void TogglePlay()
        {
            m_previewPlaying = !m_previewPlaying;
            m_lastUpdateTime = EditorApplication.timeSinceStartup;
            if (!Application.isPlaying)
            {
                return;
            }

            if (m_previewPlaying)
            {
                Player.PlayInspectorDebug();
            }
            else
            {
                Player.PauseInspectorDebug();
            }
        }

        private void Seek(float time)
        {
            m_previewPlaying = false;
            m_previewTime = time;
            if (Application.isPlaying)
            {
                Player.PauseInspectorDebug();
                Player.Seek(time);
            }
            else
            {
                SampleEditMode(time);
            }

            Repaint();
        }

        private void StepFrame(int direction, UIAnimationDefinition definition)
        {
            var frameDuration = definition.Clip.frameRate <= 0f ? 0f : 1f / definition.Clip.frameRate;
            var currentTime = Application.isPlaying ? Player.CurrentTime : m_previewTime;
            Seek(Mathf.Clamp(currentTime + frameDuration * direction, 0f, definition.Clip.length));
        }

        private void UpdatePreview()
        {
            if (!m_previewActive)
            {
                return;
            }

            if (Application.isPlaying)
            {
                var runtimeDefinition = m_debugDefinitions[m_selectedDefinitionIndex];
                if (!GameLoop.IsInitialized && m_previewPlaying)
                {
                    var runtimeUpdateTime = EditorApplication.timeSinceStartup;
                    var runtimeDeltaTime = (float)(runtimeUpdateTime - m_lastUpdateTime);
                    m_lastUpdateTime = runtimeUpdateTime;
                    Player.OnUpdate(runtimeDeltaTime, runtimeDeltaTime);
                }

                if (!runtimeDefinition.Loop && Player.CurrentTime >= runtimeDefinition.Clip.length)
                {
                    m_previewPlaying = false;
                }

                Repaint();
                return;
            }

            if (!m_previewPlaying || m_debugDefinitions.Count == 0)
            {
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            var deltaTime = (float)(now - m_lastUpdateTime);
            m_lastUpdateTime = now;
            var definition = m_debugDefinitions[m_selectedDefinitionIndex];
            var nextTime = m_previewTime + deltaTime;
            if (nextTime >= definition.Clip.length)
            {
                nextTime = definition.Loop ? nextTime % definition.Clip.length : definition.Clip.length;
                m_previewPlaying = definition.Loop;
            }

            SampleEditMode(nextTime);
            Repaint();
        }

        private void StartAnimationMode()
        {
            if (AnimationMode.InAnimationMode())
            {
                return;
            }

            AnimationMode.StartAnimationMode();
            m_ownsAnimationMode = true;
            m_lastUpdateTime = EditorApplication.timeSinceStartup;
        }

        private void SampleEditMode(float time)
        {
            if (!m_previewActive || m_debugDefinitions.Count == 0)
            {
                return;
            }

            var definition = m_debugDefinitions[m_selectedDefinitionIndex];
            StartAnimationMode();
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(Player.gameObject, definition.Clip, time);
            AnimationMode.EndSampling();
            m_previewTime = time;
            SceneView.RepaintAll();
        }

        private void EndPreview()
        {
            m_previewPlaying = false;
            if (m_previewActive && Application.isPlaying && Player != null)
            {
                Player.EndInspectorDebug();
            }

            if (m_ownsAnimationMode && AnimationMode.InAnimationMode())
            {
                AnimationMode.StopAnimationMode();
            }

            m_previewActive = false;
            m_ownsAnimationMode = false;
            m_previewTime = 0f;
            SceneView.RepaintAll();
        }

        private void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode)
            {
                EndPreview();
            }
        }

    }
}
