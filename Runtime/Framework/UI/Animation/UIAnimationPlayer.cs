using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlloyFramework.UI
{
    [DisallowMultipleComponent]
    public sealed class UIAnimationPlayer : MonoBehaviour, IUpdateable
    {
        private static readonly Action<object> m_cancellationCallback = HandleCancellation; // 无闭包的取消回调。

        [SerializeField]
        private List<UIAnimationDefinition> m_animations = new List<UIAnimationDefinition>(); // 全部动效配置。

        [SerializeField]
        private List<UIAnimationEventDefinition> m_animationEvents =
            new List<UIAnimationEventDefinition>(); // 全部动效帧事件配置。

        private readonly Dictionary<string, UIAnimationDefinition> m_animationMap =
            new Dictionary<string, UIAnimationDefinition>(StringComparer.Ordinal); // 全部动效索引。
        private readonly Dictionary<string, List<UIAnimationEventDefinition>> m_animationEventMap =
            new Dictionary<string, List<UIAnimationEventDefinition>>(StringComparer.Ordinal); // 按动效 Key 建立的帧事件索引。
        private UIAnimationDefinition m_currentDefinition; // 当前播放的动效定义。
        private UniTaskCompletionSource m_completionSource; // 当前播放的完成源。
        private CancellationTokenRegistration m_cancellationRegistration; // 当前播放的取消注册。
        private CancellationToken m_cancellationToken; // 当前播放关联的取消令牌。
        private EUIAnimationPlaybackState m_playbackState; // 当前播放状态。
        private float m_currentTime; // 当前播放时间。
        private bool m_registered; // 是否已注册到框架更新循环。
        private IReadOnlyList<UIAnimationEventDefinition> m_currentEvents; // 当前动效需要派发的帧事件。
        private string m_currentAnimationKey; // 当前动效用于业务识别的 Key。
        private int m_playbackVersion; // 用于识别事件回调引发的重入播放。
        private bool m_animationMapBuilt; // 动效和帧事件索引是否已经构建。
#if UNITY_EDITOR
        private UIAnimationDefinition m_debugDefinition; // Inspector 正在调试的动效定义。
        private UIAnimationDefinition m_debugSavedDefinition; // 进入调试前播放的动效定义。
        private EUIAnimationPlaybackState m_debugSavedState; // 进入调试前的播放状态。
        private float m_debugSavedTime; // 进入调试前的播放时间。
        private bool m_debugPlaying; // Inspector 调试预览是否正在推进。
        private string m_debugSavedAnimationKey; // 进入调试前的业务动效 Key。
#endif

        public IReadOnlyList<UIAnimationDefinition> Animations => m_animations;
        public IReadOnlyList<UIAnimationEventDefinition> AnimationEvents => m_animationEvents;
        public EUIAnimationPlaybackState PlaybackState => m_playbackState;
        public float CurrentTime => m_currentTime;
        public float Duration => m_currentDefinition?.Clip == null ? 0f : m_currentDefinition.Clip.length;
#if UNITY_EDITOR
        public bool CanRestoreInspectorPose => m_debugSavedDefinition != null;
#endif

        /// <summary>
        /// 当前动效越过 Player 配置的帧事件时间时触发。
        /// </summary>
        public event Action<UIAnimationEventContext> AnimationEventReceived;

        /// <summary>
        /// 播放 UI 打开动效；未配置时立即完成。
        /// </summary>
        /// <param name="cancellationToken">用于取消播放的令牌。</param>
        /// <returns>动效播放完成任务。</returns>
        public UniTask PlayOpenAsync(CancellationToken cancellationToken = default)
        {
            return PlayByKeyAsync(UIAnimationKeys.OPEN, false, cancellationToken);
        }

        /// <summary>
        /// 播放 UI 关闭动效；未配置时立即完成。
        /// </summary>
        /// <param name="cancellationToken">用于取消播放的令牌。</param>
        /// <returns>动效播放完成任务。</returns>
        public UniTask PlayCloseAsync(CancellationToken cancellationToken = default)
        {
            return PlayByKeyAsync(UIAnimationKeys.CLOSE, false, cancellationToken);
        }

        /// <summary>
        /// 根据 Key 播放自定义动效并通过回调报告完成或错误。
        /// </summary>
        /// <param name="animationKey">Prefab 中定义的自定义动效 Key。</param>
        /// <param name="onCompleted">播放结束后的错误回调，成功时参数为空。</param>
        /// <param name="cancellationToken">用于取消播放的令牌。</param>
        public void Play(
            string animationKey,
            Action<Exception> onCompleted = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                UICallbacks.Run(PlayCustomAsync(animationKey, cancellationToken), onCompleted);
            }
            catch (Exception exception)
            {
                UICallbacks.Run(UniTask.FromException(exception), onCompleted);
            }
        }

        /// <summary>
        /// 暂停当前动效。
        /// </summary>
        public void Pause()
        {
            if (m_playbackState == EUIAnimationPlaybackState.Playing)
            {
                m_playbackState = EUIAnimationPlaybackState.Paused;
            }
        }

        /// <summary>
        /// 继续播放当前已暂停的动效。
        /// </summary>
        public void Resume()
        {
            if (m_playbackState == EUIAnimationPlaybackState.Paused)
            {
                m_playbackState = EUIAnimationPlaybackState.Playing;
            }
        }

        /// <summary>
        /// 按指定方式停止当前动效并正常完成等待任务。
        /// </summary>
        /// <param name="stopMode">停止后保留或采样的姿态。</param>
        public void Stop(EUIAnimationStopMode stopMode)
        {
#if UNITY_EDITOR
            if (m_playbackState == EUIAnimationPlaybackState.Debugging)
            {
                EndInspectorDebug();
            }
#endif
            if (m_currentDefinition == null)
            {
                return;
            }

            if (stopMode == EUIAnimationStopMode.SampleStart)
            {
                Sample(0f);
            }
            else if (stopMode == EUIAnimationStopMode.SampleEnd)
            {
                Sample(Duration);
            }

            CompletePlayback();
        }

        /// <summary>
        /// 将当前动效跳转并采样到指定时间。
        /// </summary>
        /// <param name="time">目标时间，单位为秒。</param>
        public void Seek(float time)
        {
            if (m_currentDefinition == null)
            {
                return;
            }

            Sample(Mathf.Clamp(time, 0f, Duration));
        }

        /// <summary>
        /// 将当前动效向前采样一帧。
        /// </summary>
        public void StepPreviousFrame()
        {
            StepFrame(-1);
        }

        /// <summary>
        /// 将当前动效向后采样一帧。
        /// </summary>
        public void StepNextFrame()
        {
            StepFrame(1);
        }

        /// <summary>
        /// 由框架更新循环推进当前动效。
        /// </summary>
        /// <param name="deltaTime">受时间缩放影响的帧间隔。</param>
        /// <param name="unscaledDeltaTime">不受时间缩放影响的帧间隔。</param>
        public void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
#if UNITY_EDITOR
            if (m_playbackState == EUIAnimationPlaybackState.Debugging)
            {
                UpdateDebugPreview(unscaledDeltaTime);
                return;
            }
#endif
            if (m_playbackState != EUIAnimationPlaybackState.Playing || m_currentDefinition == null)
            {
                return;
            }

            var frameDelta = m_currentDefinition.TimeMode == EUIAnimationTimeMode.Unscaled
                ? unscaledDeltaTime
                : deltaTime;
            AdvancePlayback(frameDelta, m_playbackVersion);
        }

        internal bool HasOpenAnimation
        {
            get
            {
                EnsureAnimationMap();
                return m_animationMap.ContainsKey(UIAnimationKeys.OPEN);
            }
        }

        internal void PrepareOpenAnimation()
        {
            if (HasOpenAnimation)
            {
                SampleDefinition(m_animationMap[UIAnimationKeys.OPEN], 0f);
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// 进入 Inspector 独占调试并采样所选动效首帧。
        /// </summary>
        /// <param name="definition">需要调试的动效定义。</param>
        public void BeginInspectorDebug(UIAnimationDefinition definition)
        {
            ValidateDefinition(definition, true, definition?.Key ?? "Inspector");
            if (m_playbackState != EUIAnimationPlaybackState.Debugging)
            {
                m_debugSavedDefinition = m_currentDefinition;
                m_debugSavedState = m_playbackState;
                m_debugSavedTime = m_currentTime;
                m_debugSavedAnimationKey = m_currentAnimationKey;
            }

            m_debugDefinition = definition;
            m_debugPlaying = false;
            m_currentDefinition = definition;
            m_currentAnimationKey = definition.Key;
            m_currentTime = 0f;
            m_playbackState = EUIAnimationPlaybackState.Debugging;
            SampleDefinition(definition, 0f);
        }

        /// <summary>
        /// 播放 Inspector 当前选择的调试动效。
        /// </summary>
        public void PlayInspectorDebug()
        {
            if (m_playbackState == EUIAnimationPlaybackState.Debugging)
            {
                m_debugPlaying = true;
            }
        }

        /// <summary>
        /// 暂停 Inspector 当前选择的调试动效。
        /// </summary>
        public void PauseInspectorDebug()
        {
            m_debugPlaying = false;
        }

        /// <summary>
        /// 退出 Inspector 调试并恢复进入前的播放姿态和状态。
        /// </summary>
        public void EndInspectorDebug()
        {
            if (m_playbackState != EUIAnimationPlaybackState.Debugging)
            {
                return;
            }

            m_debugPlaying = false;
            m_currentDefinition = m_debugSavedDefinition;
            m_currentTime = m_debugSavedTime;
            m_currentAnimationKey = m_debugSavedAnimationKey;
            m_playbackState = m_debugSavedState;
            if (m_currentDefinition != null)
            {
                SampleDefinition(m_currentDefinition, m_currentTime);
            }

            m_debugDefinition = null;
            m_debugSavedDefinition = null;
            m_debugSavedState = EUIAnimationPlaybackState.Idle;
            m_debugSavedTime = 0f;
            m_debugSavedAnimationKey = null;
        }
#endif

        private void Awake()
        {
            RebuildAnimationMap();
        }

        private void OnEnable()
        {
            EnsureAnimationMap();
            m_registered = GameLoop.Register(this);
        }

        private void OnDisable()
        {
            UnregisterFromGameLoop();
            CancelPlayback(new CancellationToken(true));
        }

        private void OnDestroy()
        {
            UnregisterFromGameLoop();
            CancelPlayback(new CancellationToken(true));
        }

        private UniTask PlayByKeyAsync(
            string animationKey,
            bool required,
            CancellationToken cancellationToken)
        {
            EnsureAnimationMap();
            if (!m_animationMap.TryGetValue(animationKey, out var definition))
            {
                if (required)
                {
                    throw new KeyNotFoundException(
                        $"UI 动效 Key 不存在：Key={animationKey}，对象={name}，路径={GetHierarchyPath()}。");
                }

                return UniTask.CompletedTask;
            }

            return PlayDefinitionAsync(definition, required, animationKey, cancellationToken);
        }

        private UniTask PlayDefinitionAsync(
            UIAnimationDefinition definition,
            bool required,
            string definitionName,
            CancellationToken cancellationToken)
        {
            if (!IsDefinitionConfigured(definition))
            {
                if (required)
                {
                    throw new InvalidOperationException(
                        $"UI 动效配置缺失：Key={definitionName}，对象={name}，路径={GetHierarchyPath()}。");
                }

                return UniTask.CompletedTask;
            }

            cancellationToken.ThrowIfCancellationRequested();
            ValidateDefinition(definition, required, definitionName);
            InterruptPlayback();

            m_currentDefinition = definition;
            m_currentAnimationKey = definitionName;
            m_animationEventMap.TryGetValue(definition.Key, out var animationEvents);
            m_currentEvents = animationEvents;
            m_currentTime = 0f;
            m_playbackState = EUIAnimationPlaybackState.Playing;
            m_playbackVersion++;
            var completionSource = new UniTaskCompletionSource();
            m_completionSource = completionSource;
            m_cancellationToken = cancellationToken;
            Sample(0f);
            if (cancellationToken.CanBeCanceled)
            {
                var cancellationContext = new UIAnimationCancellationContext(
                    this,
                    m_playbackVersion,
                    cancellationToken);
                m_cancellationRegistration = cancellationToken.Register(
                    m_cancellationCallback,
                    cancellationContext);
            }

            DispatchEvents(0f, 0f, true);

            return completionSource.Task;
        }

        private UniTask PlayCustomAsync(string animationKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(animationKey))
            {
                throw new ArgumentException("UI 动效 Key 不能为空。", nameof(animationKey));
            }

            return PlayByKeyAsync(animationKey, true, cancellationToken);
        }

        private void AdvancePlayback(float frameDelta, int playbackVersion)
        {
            var previousTime = m_currentTime;
            var nextTime = previousTime + frameDelta;
            var duration = Duration;
            if (!m_currentDefinition.Loop)
            {
                var sampledTime = Mathf.Min(nextTime, duration);
                Sample(sampledTime);
                DispatchEvents(previousTime, sampledTime, false);
                if (playbackVersion != m_playbackVersion)
                {
                    return;
                }

                if (nextTime >= duration)
                {
                    CompletePlayback();
                }

                return;
            }

            // 跨越循环边界时分别派发尾段和新一轮头段，确保零秒事件每轮只触发一次。
            while (nextTime >= duration)
            {
                Sample(duration);
                DispatchEvents(previousTime, duration, false);
                if (playbackVersion != m_playbackVersion)
                {
                    return;
                }

                nextTime -= duration;
                previousTime = 0f;
                DispatchEvents(0f, 0f, true);
                if (playbackVersion != m_playbackVersion)
                {
                    return;
                }
            }

            Sample(nextTime);
            DispatchEvents(previousTime, nextTime, false);
        }

        private void DispatchEvents(float previousTime, float currentTime, bool includeStart)
        {
            if (m_currentEvents == null || AnimationEventReceived == null)
            {
                return;
            }

            for (var index = 0; index < m_currentEvents.Count; index++)
            {
                var animationEvent = m_currentEvents[index];
                var eventTime = animationEvent.Time;
                var crossedEvent = includeStart
                    ? Mathf.Approximately(eventTime, 0f)
                    : eventTime > previousTime && eventTime <= currentTime;
                if (!crossedEvent)
                {
                    continue;
                }

                var context = new UIAnimationEventContext(
                    this,
                    m_currentAnimationKey,
                    animationEvent.EventKey,
                    eventTime);
                try
                {
                    AnimationEventReceived.Invoke(context);
                }
                catch (Exception exception)
                {
                    FailPlayback(exception);
                    return;
                }
            }
        }

        private void Sample(float time)
        {
            SampleDefinition(m_currentDefinition, time);
            m_currentTime = Mathf.Clamp(time, 0f, Duration);
        }

        private void SampleDefinition(UIAnimationDefinition definition, float time)
        {
            if (!IsDefinitionConfigured(definition))
            {
                return;
            }

            var sampledTime = Mathf.Clamp(time, 0f, definition.Clip.length);
            definition.Clip.SampleAnimation(gameObject, sampledTime);
        }

        private void StepFrame(int direction)
        {
            if (m_currentDefinition?.Clip == null || m_currentDefinition.Clip.frameRate <= 0f)
            {
                return;
            }

            var frameDuration = 1f / m_currentDefinition.Clip.frameRate;
            Seek(m_currentTime + frameDuration * direction);
        }

        private void CompletePlayback()
        {
            var completionSource = m_completionSource;
            ClearCompletionState();
            m_playbackState = EUIAnimationPlaybackState.Idle;
            m_playbackVersion++;
            completionSource?.TrySetResult();
        }

        private void FailPlayback(Exception exception)
        {
            var completionSource = m_completionSource;
            ClearCompletionState();
            m_playbackState = EUIAnimationPlaybackState.Idle;
            m_playbackVersion++;
            completionSource?.TrySetException(exception);
        }

        private void InterruptPlayback()
        {
#if UNITY_EDITOR
            if (m_playbackState == EUIAnimationPlaybackState.Debugging)
            {
                EndInspectorDebug();
            }
#endif
            if (m_completionSource == null)
            {
                return;
            }

            var completionSource = m_completionSource;
            var cancellationToken = m_cancellationToken;
            ClearCompletionState();
            m_playbackState = EUIAnimationPlaybackState.Idle;
            m_playbackVersion++;
            completionSource.TrySetCanceled(cancellationToken);
        }

        private void CancelPlayback(CancellationToken cancellationToken)
        {
#if UNITY_EDITOR
            if (m_playbackState == EUIAnimationPlaybackState.Debugging)
            {
                EndInspectorDebug();
            }
#endif
            if (m_completionSource == null)
            {
                return;
            }

            var completionSource = m_completionSource;
            ClearCompletionState();
            m_playbackState = EUIAnimationPlaybackState.Idle;
            m_playbackVersion++;
            completionSource.TrySetCanceled(cancellationToken);
        }

        private void ClearCompletionState()
        {
            m_cancellationRegistration.Dispose();
            m_cancellationRegistration = default;
            m_cancellationToken = default;
            m_completionSource = null;
        }

        private void EnsureAnimationMap()
        {
            if (!m_animationMapBuilt)
            {
                RebuildAnimationMap();
            }
        }

        private void RebuildAnimationMap()
        {
            m_animationMap.Clear();
            m_animationEventMap.Clear();
            for (var index = 0; index < m_animations.Count; index++)
            {
                var definition = m_animations[index];
                ValidateAnimationDefinition(definition, index);
                if (m_animationMap.ContainsKey(definition.Key))
                {
                    throw new InvalidOperationException(
                        $"UI 动效 Key 重复：Key={definition.Key}，对象={name}，路径={GetHierarchyPath()}。");
                }

                m_animationMap.Add(definition.Key, definition);
            }

            for (var index = 0; index < m_animationEvents.Count; index++)
            {
                var eventDefinition = m_animationEvents[index];
                ValidateAnimationEventDefinition(eventDefinition, index);
                if (!m_animationEventMap.TryGetValue(eventDefinition.AnimationKey, out var eventDefinitions))
                {
                    eventDefinitions = new List<UIAnimationEventDefinition>();
                    m_animationEventMap.Add(eventDefinition.AnimationKey, eventDefinitions);
                }

                eventDefinitions.Add(eventDefinition);
            }

            m_animationMapBuilt = true;
        }

        private void ValidateAnimationDefinition(UIAnimationDefinition definition, int index)
        {
            if (definition == null)
            {
                throw new InvalidOperationException(
                    $"UI 动效配置为空：索引={index}，对象={name}，路径={GetHierarchyPath()}。");
            }

            if (definition.Key == UIAnimationKeys.OPEN || definition.Key == UIAnimationKeys.CLOSE)
            {
                if (definition.Loop)
                {
                    throw new InvalidOperationException(
                        $"UI {definition.Key} 动效不允许循环：对象={name}，路径={GetHierarchyPath()}。");
                }
            }
            else
            {
                UIAnimationValidationRules.ValidateKey(definition.Key);
            }

            ValidateDefinition(definition, true, definition.Key);
        }

        private void ValidateAnimationEventDefinition(UIAnimationEventDefinition definition, int index)
        {
            if (definition == null)
            {
                throw new InvalidOperationException(
                    $"UI 动效帧事件配置为空：索引={index}，对象={name}，路径={GetHierarchyPath()}。");
            }

            if (!m_animationMap.TryGetValue(definition.AnimationKey, out var animationDefinition))
            {
                throw new InvalidOperationException(
                    $"UI 动效帧事件引用了未配置的 Key：Key={definition.AnimationKey}，索引={index}。");
            }

            UIAnimationValidationRules.ValidateEventKey(definition.EventKey);
            if (definition.Time < 0f || definition.Time > animationDefinition.Clip.length)
            {
                throw new InvalidOperationException(
                    $"UI 动效帧事件时间超出 Clip 范围：Key={definition.AnimationKey}，Time={definition.Time}。");
            }
        }

        private void ValidateDefinition(
            UIAnimationDefinition definition,
            bool required,
            string definitionName)
        {
            if (definition == null || definition.Clip == null)
            {
                if (!required && !IsDefinitionConfigured(definition))
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"UI 动效引用不完整：动效={definitionName}，对象={name}，路径={GetHierarchyPath()}。");
            }

            if (!definition.Clip.legacy)
            {
                throw new InvalidOperationException(
                    $"UI 动效 Clip 必须启用 Legacy：动效={definitionName}，Clip={definition.Clip.name}。");
            }

            if (definition.Clip.length <= 0f || definition.Clip.frameRate <= 0f)
            {
                throw new InvalidOperationException(
                    $"UI 动效 Clip 的时长和帧率必须大于 0：动效={definitionName}，Clip={definition.Clip.name}。");
            }

        }

        private string GetHierarchyPath()
        {
            var current = transform;
            var path = current.name;
            while (current.parent != null)
            {
                current = current.parent;
                path = current.name + "/" + path;
            }

            return path;
        }

        private void UnregisterFromGameLoop()
        {
            if (!m_registered)
            {
                return;
            }

            GameLoop.Unregister(this);
            m_registered = false;
        }

        private static bool IsDefinitionConfigured(UIAnimationDefinition definition)
        {
            return definition != null && definition.IsConfigured;
        }

        private static void HandleCancellation(object state)
        {
            var context = (UIAnimationCancellationContext)state;
            if (context.PlaybackVersion == context.Player.m_playbackVersion)
            {
                context.Player.CancelPlayback(context.CancellationToken);
            }
        }

#if UNITY_EDITOR
        private void UpdateDebugPreview(float unscaledDeltaTime)
        {
            if (!m_debugPlaying || m_debugDefinition?.Clip == null)
            {
                return;
            }

            var duration = m_debugDefinition.Clip.length;
            var nextTime = m_currentTime + unscaledDeltaTime;
            if (nextTime >= duration)
            {
                nextTime = m_debugDefinition.Loop && duration > 0f ? nextTime % duration : duration;
                m_debugPlaying = m_debugDefinition.Loop;
            }

            SampleDefinition(m_debugDefinition, nextTime);
            m_currentDefinition = m_debugDefinition;
            m_currentTime = nextTime;
        }
#endif

        private sealed class UIAnimationCancellationContext
        {
            internal readonly UIAnimationPlayer Player; // 需要取消的播放器。
            internal readonly int PlaybackVersion; // 注册取消时的播放版本。
            internal readonly CancellationToken CancellationToken; // 需要传递给等待者的取消令牌。

            internal UIAnimationCancellationContext(
                UIAnimationPlayer player,
                int playbackVersion,
                CancellationToken cancellationToken)
            {
                Player = player;
                PlaybackVersion = playbackVersion;
                CancellationToken = cancellationToken;
            }
        }
    }
}
