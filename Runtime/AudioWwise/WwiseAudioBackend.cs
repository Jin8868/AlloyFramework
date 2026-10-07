using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlloyFramework.Audio.Wwise
{
    public sealed class WwiseAudioBackend : IAudioBackend, IAudioBackendDiagnostics
    {
        private readonly Dictionary<long, NativePlayback> m_playbacks =
            new Dictionary<long, NativePlayback>(); // 框架标识到原生播放。
        private readonly Dictionary<AudioEmitter, GameObject> m_emitters =
            new Dictionary<AudioEmitter, GameObject>(); // 显式空间发声对象。
        private readonly Dictionary<AudioContentLease, NativeGroup> m_groups =
            new Dictionary<AudioContentLease, NativeGroup>(); // 原生组加载所有权。
        private readonly ConcurrentQueue<NativePlayback> m_endedQueue =
            new ConcurrentQueue<NativePlayback>(); // 回调仅交付最小上下文。
        private readonly AkCallbackManager.EventCallback m_callback = OnEventCallback; // 缓存静态回调。
        private readonly Dictionary<string, int> m_fileUsers = new Dictionary<string, int>(); // 原生目录文件引用。
        private readonly List<AudioEmitter> m_emitterSnapshot = new List<AudioEmitter>(); // 跟随与注销复用缓冲。
        private GameObject m_root; // 框架唯一持久化对象。
        private GameObject m_listener; // 默认监听器，不依赖业务相机。
        private GameObject m_defaultEmitter; // 所有二维事件共用对象。
        private Transform m_listenerTarget; // 显式选择的空间监听目标。
        private Action<long, long> m_ended; // 框架结束通知。
        private long m_session; // 迟到回调隔离。
        private string m_initDirectory; // 原生读取的会话独占目录，避免累积搜索路径。
        private bool m_ownsEngine; // 仅销毁自己初始化的引擎。
        private bool m_focused = true; // 系统焦点状态。
        private bool m_systemPaused; // 系统挂起状态。
        private readonly IAssetHandle<ScriptableObject> m_settingsLease; // Player 中通过框架加载的 SDK 配置。

        /// <summary>创建可选后端，项目设置由框架资源系统加载。</summary>
        /// <param name="settingsLease">覆盖引擎生命周期的初始化设置资源租约。</param>
        public WwiseAudioBackend(IAssetHandle<ScriptableObject> settingsLease = null)
        { m_settingsLease = settingsLease; }

        public bool IsReady { get; private set; }
        public EAudioCapabilities Capabilities => EAudioCapabilities.Spatial | EAudioCapabilities.Pause |
            EAudioCapabilities.InstanceParameter | EAudioCapabilities.GlobalParameter |
            EAudioCapabilities.State | EAudioCapabilities.Switch;

        /// <summary>初始化框架拥有的引擎、Init、默认监听和发声对象。</summary>
        /// <param name="settings">平台与语言设置。</param>
        /// <param name="initialContent">初始化文件租约。</param>
        /// <param name="session">会话标识。</param>
        /// <param name="ended">真实结束通知。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>初始化任务。</returns>
        /// <exception cref="InvalidOperationException">已有其他引擎或原生初始化失败。</exception>
        public UniTask InitializeAsync(AudioSettings settings, AudioContentLease initialContent, long session,
            Action<long, long> ended, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (AkUnitySoundEngine.IsInitialized())
            { throw new InvalidOperationException("Wwise 已由其他入口初始化，请移除 WwiseGlobal 并关闭编辑器自动初始化。"); }
            if (!(m_settingsLease?.Asset is AkWwiseInitializationSettings))
            { throw new InvalidOperationException("缺少框架加载的 Wwise 初始化设置资源。"); }
            if (initialContent.BackendVersion != AkUnitySoundEngine.WwiseVersion)
            { throw new InvalidOperationException("SoundBank 制作版本与当前 Wwise SDK 不匹配，请重新生成。"); }
            m_session = session;
            m_ended = ended;
            m_initDirectory = Path.Combine(Application.persistentDataPath, "AlloyWwise", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_initDirectory);
            try
            {
                // 复用 SDK 设置数据，初始化和回调泵所有权由框架独占。
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                { AkUnitySoundEngine.SetAndroidActivity(activity.GetRawObject()); }
#endif
                var platform = AkWwiseInitializationSettings.ActivePlatformSettings;
                var initialization = platform.AkInitializationSettings;
                GCHandle pin = GCHandle.Alloc(initialization);
                m_ownsEngine = true;
                try { Check(AkUnitySoundEngine.Init(initialization), "初始化引擎"); }
                finally { pin.Free(); }
                var spatial = platform.AkSpatialAudioInitSettings;
                pin = GCHandle.Alloc(spatial);
                try { Check(AkUnitySoundEngine.InitSpatialAudio(spatial), "初始化空间音频"); }
                finally { pin.Free(); }
                var communication = platform.AkCommunicationSettings;
                pin = GCHandle.Alloc(communication);
                try { AkUnitySoundEngine.InitCommunication(communication); }
                finally { pin.Free(); }
                Check(AkUnitySoundEngine.SetBasePath(m_initDirectory), "设置初始化目录");
                // 当前自动 Bank 输出使用 Event、Bus、Media 目录，兼容项目关闭原生子目录开关的设置。
                AddSearchDirectory("Event");
                AddSearchDirectory("Bus");
                AddSearchDirectory("Media");
                Check(AkUnitySoundEngine.SetCurrentLanguage(settings.Language), "设置语言");
                AkCallbackManager.Init(platform.CallbackManagerInitializationSettings);
                LoadNativeGroup(initialContent);

                // 对象直接注册到原生引擎，避免 AkInitializer 的第二套生命周期。
                m_root = new GameObject("[AlloyFramework] WwiseAudio");
                UnityEngine.Object.DontDestroyOnLoad(m_root);
                m_listener = CreateObject("DefaultListener");
                m_defaultEmitter = CreateObject("Default2DEmitter");
                Check(AkUnitySoundEngine.SetDefaultListeners(
                    new[] { AkUnitySoundEngine.GetAkGameObjectID(m_listener) }, 1), "登记默认监听器");
                WwiseAudioHost host = m_root.AddComponent<WwiseAudioHost>();
                host.Backend = this;
                IsReady = true;
                AlloyDebug.Log("[Audio/Wwise] 默认监听器已注册，框架持久化对象已创建。");
                return UniTask.CompletedTask;
            }
            catch { ShutdownImmediately(); throw; }
        }

        /// <summary>终止引擎后完成关闭。</summary>
        /// <returns>关闭任务。</returns>
        public UniTask ShutdownAsync() { ShutdownImmediately(); return UniTask.CompletedTask; }

        /// <summary>幂等销毁本后端持有的原生引擎和对象。</summary>
        public void ShutdownImmediately()
        {
            IsReady = false;
            if (m_ownsEngine)
            {
                foreach (NativePlayback playback in m_playbacks.Values)
                { AkCallbackManager.RemoveEventCallbackCookie(playback); }
                if (AkUnitySoundEngine.IsInitialized()) { AkUnitySoundEngine.UnregisterAllGameObjects(); }
                // Term 等待原生线程退出，之后内容提供器才能删除文件。
                AkUnitySoundEngine.Term();
                AkCallbackManager.Term();
                m_ownsEngine = false;
            }
            m_playbacks.Clear();
            m_groups.Clear();
            m_fileUsers.Clear();
            m_emitters.Clear();
            m_emitterSnapshot.Clear();
            while (m_endedQueue.TryDequeue(out _)) { }
            m_ended = null;
            if (m_root)
            {
                WwiseAudioHost host = m_root.GetComponent<WwiseAudioHost>();
                if (host) { host.Backend = null; }
                UnityEngine.Object.Destroy(m_root);
            }
            m_root = null;
            m_listener = null;
            m_listenerTarget = null;
            m_defaultEmitter = null;
            if (!string.IsNullOrEmpty(m_initDirectory) && Directory.Exists(m_initDirectory))
            { Directory.Delete(m_initDirectory, true); }
            m_initDirectory = null;
            m_settingsLease?.Dispose();
        }

        /// <summary>加载 Bank 并准备自动事件的独立媒体。</summary>
        /// <param name="content">保持有效的组文件。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>加载任务。</returns>
        public UniTask LoadGroupAsync(AudioContentLease content, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadNativeGroup(content);
            return UniTask.CompletedTask;
        }

        /// <summary>同步卸载确认后重建搜索目录。</summary>
        /// <param name="content">待卸载组。</param>
        public void UnloadGroup(AudioContentLease content)
        {
            if (!m_groups.TryGetValue(content, out NativeGroup group)) { return; }
            UnloadNativeGroup(group);
            m_groups.Remove(content);
            ReleaseFiles(group);
        }

        /// <summary>提交单次事件并请求真实结束回调。</summary>
        /// <param name="playID">框架标识。</param>
        /// <param name="key">事件名称。</param>
        /// <param name="emitter">空间对象，空值为二维对象。</param>
        /// <returns>提交结果。</returns>
        public AudioOperationResult Play(long playID, string key, AudioEmitter emitter)
        {
            if (!IsReady) { return Error("引擎未就绪。"); }
            if (emitter != null && !emitter.IsValid) { return Error("空间发声目标已销毁。"); }
            GameObject target = GetEmitter(emitter);
            var playback = new NativePlayback
            { ID = playID, Session = m_session, Backend = this, Emitter = emitter };
            playback.PlayingID = AkUnitySoundEngine.PostEvent(key, target,
                (uint)AkCallbackType.AK_EndOfEvent, m_callback, playback);
            if (playback.PlayingID == AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
            { ReleaseEmitter(emitter); return Error($"PostEvent 失败：{key}"); }
            m_playbacks.Add(playID, playback);
            return new AudioOperationResult();
        }

        /// <summary>精确停止原生 Playing ID。</summary>
        /// <param name="playID">框架标识。</param>
        /// <param name="fadeOutSeconds">淡出秒数。</param>
        /// <returns>是否找到播放。</returns>
        public bool Stop(long playID, float fadeOutSeconds)
        {
            if (!m_playbacks.TryGetValue(playID, out NativePlayback playback)) { return false; }
            int milliseconds = (int)Math.Min(int.MaxValue, fadeOutSeconds * 1000.0);
            AkUnitySoundEngine.StopPlayingID(playback.PlayingID, milliseconds);
            return true;
        }

        /// <summary>精确设置单次播放暂停状态。</summary>
        /// <param name="playID">框架标识。</param>
        /// <param name="paused">暂停或恢复。</param>
        /// <returns>是否找到播放。</returns>
        public bool SetPaused(long playID, bool paused)
        {
            if (!m_playbacks.TryGetValue(playID, out NativePlayback playback)) { return false; }
            AkUnitySoundEngine.ExecuteActionOnPlayingID(paused
                ? AkActionOnEventType.AkActionOnEventType_Pause : AkActionOnEventType.AkActionOnEventType_Resume,
                playback.PlayingID);
            return true;
        }

        /// <summary>设置全局或实例 RTPC。</summary>
        /// <param name="playID">零为全局。</param>
        /// <param name="key">参数名称。</param>
        /// <param name="value">参数数值。</param>
        /// <returns>原生操作结果。</returns>
        public AudioOperationResult SetParameter(long playID, string key, float value)
        {
            if (string.IsNullOrWhiteSpace(key) || float.IsNaN(value) || float.IsInfinity(value))
            { return Error("参数名称或数值无效。"); }
            if (playID == 0) { return Result(AkUnitySoundEngine.SetRTPCValue(key, value), key); }
            if (!m_playbacks.TryGetValue(playID, out NativePlayback playback)) { return Error("播放标识已失效。"); }
            return Result(AkUnitySoundEngine.SetRTPCValueByPlayingID(key, value, playback.PlayingID), key);
        }

        /// <summary>设置全局 State。</summary>
        /// <param name="groupKey">状态组。</param>
        /// <param name="valueKey">状态值。</param>
        /// <returns>原生操作结果。</returns>
        public AudioOperationResult SetState(string groupKey, string valueKey)
        { return Result(AkUnitySoundEngine.SetState(groupKey, valueKey), groupKey); }

        /// <summary>设置对象 Switch。</summary>
        /// <param name="emitter">发声对象。</param>
        /// <param name="groupKey">切换组。</param>
        /// <param name="valueKey">切换值。</param>
        /// <returns>原生操作结果。</returns>
        public AudioOperationResult SetSwitch(AudioEmitter emitter, string groupKey, string valueKey)
        {
            if (emitter != null && !emitter.IsValid) { return Error("发声目标已销毁。"); }
            return Result(AkUnitySoundEngine.SetSwitch(groupKey, valueKey, GetEmitter(emitter)), groupKey);
        }

        /// <summary>绑定明确指定的监听目标。</summary>
        /// <param name="target">空值恢复固定位置。</param>
        public void SetListener(Transform target) { m_listenerTarget = target; }

        /// <summary>为框架诊断面板显示原生 Playing ID。</summary>
        /// <param name="playID">框架标识。</param>
        /// <returns>内部播放描述，失效标识返回空字符串。</returns>
        public string GetPlaybackDescription(long playID)
        {
            return m_playbacks.TryGetValue(playID, out NativePlayback playback)
                ? $"Wwise PlayingID={playback.PlayingID}" : string.Empty;
        }

        internal void Tick()
        {
            if (!IsReady) { return; }
            UpdatePosition(m_listener, m_listenerTarget, Vector3.zero);
            m_emitterSnapshot.Clear();
            m_emitterSnapshot.AddRange(m_emitters.Keys);
            foreach (AudioEmitter emitter in m_emitterSnapshot)
            {
                GameObject target = m_emitters[emitter];
                if (emitter.IsValid) { UpdatePosition(target, emitter.Target, emitter.Position); }
                else { AkUnitySoundEngine.StopAll(target); ReleaseEmitter(emitter); }
            }
            // SDK 在这里把原生通知转入托管回调；退出泵后才执行资源回收。
            AkCallbackManager.PostCallbacks();
            AkUnitySoundEngine.RenderAudio();
            while (m_endedQueue.TryDequeue(out NativePlayback playback))
            {
                if (!m_playbacks.Remove(playback.ID)) { continue; }
                ReleaseEmitter(playback.Emitter);
                m_ended?.Invoke(playback.Session, playback.ID);
            }
        }

        internal void SetFocused(bool focused) { m_focused = focused; UpdateSuspension(); }
        internal void SetPausedBySystem(bool paused) { m_systemPaused = paused; UpdateSuspension(); }

        private void UpdateSuspension()
        {
            if (!IsReady) { return; }
            if (m_systemPaused || !m_focused) { AkUnitySoundEngine.Suspend(); }
            else { AkUnitySoundEngine.WakeupFromSuspend(); }
        }

        private void LoadNativeGroup(AudioContentLease content)
        {
            var group = new NativeGroup { Content = content };
            m_groups.Add(content, group);
            try
            {
                // 原生引擎只使用一个稳定根目录；共享媒体按文件引用保留到最后一个组卸载。
                foreach (AudioContentFile file in content.Group.Files)
                {
                    string target = Path.Combine(m_initDirectory, file.Path);
                    if (!m_fileUsers.TryGetValue(file.Path, out int users))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        File.Copy(Path.Combine(content.DirectoryPath, file.Path), target, false);
                    }
                    m_fileUsers[file.Path] = users + 1;
                    group.Files.Add(file.Path);
                }
                foreach (AudioBankDefinition bank in content.Group.Banks)
                {
                    uint id;
                    uint type = BankType(bank.Type);
                    Check(AkUnitySoundEngine.LoadBank(bank.Name, out id, type),
                        $"加载 Bank {bank.Name}");
                    group.Banks.Add(new NativeBank { ID = id, Type = type });
                }
                if (content.Group.PreparedEvents != null && content.Group.PreparedEvents.Length > 0)
                {
                    Check(AkUnitySoundEngine.PrepareEvent(AkPreparationType.Preparation_Load,
                        content.Group.PreparedEvents, (uint)content.Group.PreparedEvents.Length), "准备事件媒体");
                    group.Prepared = true;
                }
            }
            catch
            {
                try { UnloadNativeGroup(group); }
                catch { ShutdownImmediately(); throw; }
                m_groups.Remove(content);
                ReleaseFiles(group);
                throw;
            }
        }

        private static void UnloadNativeGroup(NativeGroup group)
        {
            if (group.Prepared)
            {
                Check(AkUnitySoundEngine.PrepareEvent(AkPreparationType.Preparation_Unload,
                    group.Content.Group.PreparedEvents, (uint)group.Content.Group.PreparedEvents.Length),
                    "释放事件媒体");
                group.Prepared = false;
            }
            for (int index = group.Banks.Count - 1; index >= 0; index--)
            {
                NativeBank bank = group.Banks[index];
                Check(AkUnitySoundEngine.UnloadBank(bank.ID, IntPtr.Zero, bank.Type), "卸载 Bank");
                group.Banks.RemoveAt(index);
            }
        }

        private void ReleaseFiles(NativeGroup group)
        {
            foreach (string path in group.Files)
            {
                int users = m_fileUsers[path] - 1;
                if (users > 0) { m_fileUsers[path] = users; continue; }
                m_fileUsers.Remove(path);
                File.Delete(Path.Combine(m_initDirectory, path));
            }
            group.Files.Clear();
        }

        private GameObject CreateObject(string name)
        {
            var target = new GameObject(name);
            target.transform.SetParent(m_root.transform, false);
            Check(AkUnitySoundEngine.RegisterGameObj(target, name), "注册音频对象");
            UpdatePosition(target, null, Vector3.zero);
            return target;
        }

        private void AddSearchDirectory(string relativePath)
        {
            string directory = Path.Combine(m_initDirectory, relativePath);
            Directory.CreateDirectory(directory);
            Check(AkUnitySoundEngine.AddBasePath(directory), "登记自动 Bank 搜索目录");
        }

        private GameObject GetEmitter(AudioEmitter emitter)
        {
            if (emitter == null) { return m_defaultEmitter; }
            if (!m_emitters.TryGetValue(emitter, out GameObject target))
            {
                target = CreateObject("SpatialEmitter");
                m_emitters.Add(emitter, target);
            }
            UpdatePosition(target, emitter.Target, emitter.Position);
            return target;
        }

        private void ReleaseEmitter(AudioEmitter emitter)
        {
            if (emitter == null) { return; }
            if (emitter.IsValid && !emitter.IsTransient) { return; }
            foreach (NativePlayback playback in m_playbacks.Values)
            { if (playback.Emitter == emitter) { return; } }
            if (!m_emitters.TryGetValue(emitter, out GameObject target)) { return; }
            AkUnitySoundEngine.UnregisterGameObj(target);
            m_emitters.Remove(emitter);
            UnityEngine.Object.Destroy(target);
        }

        private static void UpdatePosition(GameObject target, Transform source, Vector3 position)
        {
            AkUnitySoundEngine.SetObjectPosition(target, source ? source.position : position,
                source ? source.forward : Vector3.forward, source ? source.up : Vector3.up);
        }

        private static void OnEventCallback(object cookie, AkCallbackType type, AkCallbackInfo info)
        {
            if (type == AkCallbackType.AK_EndOfEvent && cookie is NativePlayback playback)
            { playback.Backend.m_endedQueue.Enqueue(playback); }
        }

        private static uint BankType(EAudioBankType type)
        {
            switch (type)
            {
                case EAudioBankType.Event: return (uint)AkBankTypeEnum.AkBankType_Event;
                case EAudioBankType.Bus: return (uint)AkBankTypeEnum.AkBankType_Bus;
                default: return (uint)AkBankTypeEnum.AkBankType_User;
            }
        }

        private static void Check(AKRESULT result, string operation)
        { if (result != AKRESULT.AK_Success) { throw new InvalidOperationException($"{operation}：{result}"); } }

        private static AudioOperationResult Result(AKRESULT result, string operation)
        { return result == AKRESULT.AK_Success ? new AudioOperationResult() : Error($"{operation}：{result}"); }

        private static AudioOperationResult Error(string message)
        { return new AudioOperationResult(EAudioError.BackendFailure, message); }

        private sealed class NativePlayback
        {
            internal long ID;
            internal long Session;
            internal uint PlayingID;
            internal WwiseAudioBackend Backend;
            internal AudioEmitter Emitter;
        }

        private sealed class NativeGroup
        {
            internal AudioContentLease Content;
            internal bool Prepared;
            internal readonly List<NativeBank> Banks = new List<NativeBank>();
            internal readonly List<string> Files = new List<string>();
        }

        private sealed class NativeBank
        {
            internal uint ID;
            internal uint Type;
        }
    }
}
