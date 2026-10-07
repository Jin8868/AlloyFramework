using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlloyFramework.Audio
{
    public sealed class AudioManager : IUpdateable
    {
        private static readonly AudioManager m_instance = new AudioManager(); // 唯一业务入口。
        private static long m_nextID; // 不随会话重置的进程内标识。
        private readonly Dictionary<long, Playback> m_playbacks = new Dictionary<long, Playback>(); // 活跃请求。
        private readonly Dictionary<long, AudioPlaybackInfo> m_history =
            new Dictionary<long, AudioPlaybackInfo>(); // 有界终态记录。
        private readonly Queue<long> m_historyOrder = new Queue<long>(); // 历史淘汰顺序。
        private readonly List<Playback> m_updateSnapshot = new List<Playback>(); // 主线程取消检查复用缓冲。
        private readonly List<GroupEntry> m_retiredGroups = new List<GroupEntry>(); // 卸载失败的依赖保留到 Term。
        private readonly Dictionary<string, GroupEntry> m_groups = new Dictionary<string, GroupEntry>(); // 共享组。
        private readonly Dictionary<string, AudioEventDependency> m_events =
            new Dictionary<string, AudioEventDependency>(); // 制作侧导出的依赖映射。
        private IAudioBackend m_backend; // 可替换播放实现。
        private IAudioContentProvider m_provider; // 框架资源交付实现。
        private AudioSettings m_settings; // 会话设置。
        private AudioContentLease m_initContent; // 引擎存活期间持有 Init。
        private CancellationTokenSource m_sessionCancellation; // 会话退出取消共享准备。
        private UniTaskCompletionSource m_initialization; // 合并重复初始化。
        private UniTaskCompletionSource m_shutdown; // 合并正常关闭并等待异步交付退出。
        private long m_session; // 拒绝旧会话回调。
        private bool m_shuttingDown; // 阻止关闭期间新请求。
        private Func<CancellationToken, UniTask> m_installer; // AOT 项目入口提供的实现安装步骤。

        private AudioManager() { }
        public static AudioManager Instance => m_instance;
        public bool IsReady => !m_shuttingDown && m_backend != null && m_backend.IsReady;
        public string BackendName => m_backend?.GetType().Name;
        public EAudioCapabilities Capabilities => m_backend?.Capabilities ?? EAudioCapabilities.None;

        /// <summary>注册项目安装步骤，使业务启动管线不引用播放 SDK。</summary>
        /// <param name="installer">在资源系统就绪后执行的具名安装方法。</param>
        public void SetInstaller(Func<CancellationToken, UniTask> installer) { m_installer = installer; }

        /// <summary>执行项目安装步骤并初始化引擎。</summary>
        /// <param name="cancellationToken">启动取消令牌。</param>
        /// <returns>音频就绪任务。</returns>
        /// <exception cref="InvalidOperationException">没有配置项目安装步骤。</exception>
        public async UniTask ConfigureAndInitializeAsync(CancellationToken cancellationToken = default)
        {
            if (m_backend == null)
            {
                if (m_installer == null) { throw new InvalidOperationException("项目未配置音频安装步骤。"); }
                await m_installer(cancellationToken);
            }
            await InitializeAsync(cancellationToken);
        }

        /// <summary>安装后端与内容交付器，尚不启动引擎。</summary>
        /// <param name="backend">播放后端。</param>
        /// <param name="provider">资源交付器。</param>
        /// <param name="settings">当前平台设置。</param>
        /// <exception cref="InvalidOperationException">已有安装或清单无效。</exception>
        /// <exception cref="ArgumentNullException">依赖为空。</exception>
        public void Install(IAudioBackend backend, IAudioContentProvider provider, AudioSettings settings)
        {
            if (m_backend != null) { throw new InvalidOperationException("音频实现已安装，请先关闭。"); }
            if (backend == null || provider == null || settings == null)
            { throw new ArgumentNullException("音频安装依赖"); }

            // 安装前验证循环与缺失依赖，避免共享加载相互等待。
            var definitions = new Dictionary<string, AudioContentGroup>();
            if (provider.Manifest == null || provider.Manifest.Init == null || provider.Manifest.Groups == null ||
                provider.Manifest.Events == null || provider.Manifest.Platform != settings.Platform)
            { throw new InvalidOperationException("音频清单缺失或平台不匹配。"); }
            foreach (AudioContentGroup group in provider.Manifest.Groups) { definitions.Add(group.Key, group); }
            foreach (AudioContentGroup group in definitions.Values)
            { ValidateDependencies(group.Key, definitions, new HashSet<string>()); }
            var events = new Dictionary<string, AudioEventDependency>();
            foreach (AudioEventDependency dependency in provider.Manifest.Events)
            {
                foreach (string key in dependency.Groups)
                { if (!definitions.ContainsKey(key)) { throw new InvalidOperationException($"缺少音频组：{key}"); } }
                events.Add(dependency.Key, dependency);
            }
            m_backend = backend;
            m_provider = provider;
            m_settings = settings;
            m_sessionCancellation = new CancellationTokenSource();
            m_session++;
            m_shuttingDown = false;
            m_shutdown = null;
            foreach (var pair in events) { m_events.Add(pair.Key, pair.Value); }
        }

        /// <summary>加载 Init 并初始化唯一引擎，重复调用共享初始化任务。</summary>
        /// <param name="cancellationToken">只取消当前等待者。</param>
        /// <returns>引擎就绪任务。</returns>
        /// <exception cref="InvalidOperationException">尚未安装实现。</exception>
        public UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (m_backend == null || m_shuttingDown) { throw new InvalidOperationException("音频实现尚未安装。"); }
            if (m_initialization == null)
            {
                m_initialization = new UniTaskCompletionSource();
                InitializeCoreAsync(m_session).Forget();
            }
            return m_initialization.Task.AttachExternalCancellation(cancellationToken);
        }

        /// <summary>使用回调等待初始化，成功传入空异常，失败只通知一次。</summary>
        /// <param name="onCompleted">初始化完成回调。</param>
        /// <param name="cancellationToken">取消当前等待者。</param>
        /// <exception cref="ArgumentNullException">回调为空。</exception>
        public void Initialize(Action<Exception> onCompleted, CancellationToken cancellationToken = default)
        {
            if (onCompleted == null) { throw new ArgumentNullException(nameof(onCompleted)); }
            new InitializationRequest
            { Manager = this, Callback = onCompleted, Token = cancellationToken }.RunAsync().Forget();
        }

        /// <summary>创建独立业务生命周期，不绑定特定场景对象。</summary>
        /// <param name="name">诊断中显示的归属名称。</param>
        /// <returns>可释放的作用域。</returns>
        public AudioScope CreateScope(string name = null)
        { return new AudioScope(this, Interlocked.Increment(ref m_nextID), m_session, name); }

        /// <summary>获取预加载资源组，多个持有者共享原生资源。</summary>
        /// <param name="groupKey">清单组名称。</param>
        /// <param name="scope">可选作用域。</param>
        /// <param name="cancellationToken">本持有者取消令牌。</param>
        /// <returns>独立引用租约。</returns>
        /// <exception cref="InvalidOperationException">尚未就绪或作用域过期。</exception>
        /// <exception cref="ObjectDisposedException">作用域已释放。</exception>
        /// <exception cref="KeyNotFoundException">资源组不存在。</exception>
        public async UniTask<AudioGroupLease> AcquireGroupAsync(string groupKey, AudioScope scope = null,
            CancellationToken cancellationToken = default)
        {
            RequireReady();
            if (scope?.IsDisposed == true) { throw new ObjectDisposedException(nameof(AudioScope)); }
            if (scope != null && scope.Session != m_session) { throw new InvalidOperationException("作用域已过期。"); }
            GroupEntry entry = GetOrStartGroup(groupKey);
            CancellationTokenSource linked = scope == null ? null
                : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, scope.Token);
            CancellationToken effectiveToken = linked?.Token ?? cancellationToken;
            try
            {
                await entry.Ready.Task.AttachExternalCancellation(effectiveToken);
                effectiveToken.ThrowIfCancellationRequested();
                if (!IsReady || entry.Session != m_session) { throw new OperationCanceledException(); }
                var lease = new AudioGroupLease(this, groupKey, entry, scope);
                scope?.Track(lease);
                return lease;
            }
            catch { ReleaseGroup(entry); throw; }
            finally { linked?.Dispose(); }
        }

        /// <summary>按事件清单预加载依赖，租约统一交给指定作用域。</summary>
        /// <param name="key">事件名称。</param>
        /// <param name="scope">持有预加载引用的作用域。</param>
        /// <param name="cancellationToken">当前预加载取消令牌。</param>
        /// <returns>所有事件依赖就绪任务。</returns>
        /// <exception cref="ArgumentNullException">没有指定作用域。</exception>
        /// <exception cref="KeyNotFoundException">事件不在清单中。</exception>
        public async UniTask PreloadAudioAsync(string key, AudioScope scope,
            CancellationToken cancellationToken = default)
        {
            RequireReady();
            if (scope == null) { throw new ArgumentNullException(nameof(scope)); }
            if (!m_events.TryGetValue(key, out AudioEventDependency dependency))
            { throw new KeyNotFoundException($"事件未收录：{key}"); }
            var acquired = new List<AudioGroupLease>();
            try
            {
                foreach (string groupKey in dependency.Groups)
                { acquired.Add(await AcquireGroupAsync(groupKey, scope, cancellationToken)); }
            }
            catch
            {
                // 仅回滚本次预加载，作用域原本持有的其他资源保持有效。
                foreach (AudioGroupLease lease in acquired) { lease.Dispose(); }
                throw;
            }
        }

        /// <summary>立即返回请求标识，按依赖准备后提交播放。</summary>
        /// <param name="key">制作侧 Event 名称。</param>
        /// <param name="options">作用域、空间与就绪策略。</param>
        /// <param name="onStarted">提交成功或失败后仅调用一次。</param>
        /// <param name="cancellationToken">取消请求或停止播放。</param>
        /// <returns>框架播放标识，不表示已成功发声。</returns>
        /// <exception cref="ArgumentException">Key 或空间配置无效。</exception>
        /// <exception cref="ObjectDisposedException">作用域已释放。</exception>
        /// <exception cref="InvalidOperationException">音频未就绪或作用域已过期。</exception>
        /// <exception cref="NotSupportedException">后端不支持请求的空间能力。</exception>
        public long PlayAudio(string key, AudioPlayOptions? options = null,
            Action<AudioStartResult> onStarted = null, CancellationToken cancellationToken = default)
        {
            return RequestPlayback(key, options, onStarted, cancellationToken).ID;
        }

        /// <summary>等待资源准备和原生提交成功，不等待声音结束。</summary>
        /// <param name="key">事件名称。</param>
        /// <param name="options">播放配置，空值使用项目默认策略。</param>
        /// <param name="cancellationToken">请求取消令牌。</param>
        /// <returns>成功提交的框架播放标识。</returns>
        /// <exception cref="InvalidOperationException">未就绪或提交失败。</exception>
        public async UniTask<long> PlayAudioAsync(string key, AudioPlayOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Playback playback = RequestPlayback(key, options, null, cancellationToken);
            AudioStartResult result = await playback.Start.Task;
            if (result.Result.Error == EAudioError.Cancelled) { throw new OperationCanceledException(playback.Token); }
            if (!result.Result.IsSuccess) { throw new InvalidOperationException(result.Result.Message); }
            return result.PlayID;
        }

        private Playback RequestPlayback(string key, AudioPlayOptions? options,
            Action<AudioStartResult> onStarted, CancellationToken cancellationToken)
        {
            RequireReady();
            if (string.IsNullOrWhiteSpace(key)) { throw new ArgumentException("音频 Key 不能为空。", nameof(key)); }
            AudioPlayOptions effective = options ??
                new AudioPlayOptions { ReadinessMode = m_settings.DefaultReadiness };
            effective.ReadinessMode = effective.ReadinessMode ?? m_settings.DefaultReadiness;
            if (effective.ReadinessMode != EAudioReadinessMode.LoadIfNeeded &&
                effective.ReadinessMode != EAudioReadinessMode.RequireReady)
            { throw new ArgumentOutOfRangeException(nameof(options)); }
            if (effective.Scope?.IsDisposed == true) { throw new ObjectDisposedException(nameof(AudioScope)); }
            if (effective.Scope != null && effective.Scope.Session != m_session)
            { throw new InvalidOperationException("作用域已过期。"); }
            if (effective.Position.HasValue && effective.Emitter != null)
            { throw new ArgumentException("Position 与 Emitter 不能同时指定。"); }
            if (effective.Emitter != null && !effective.Emitter.IsValid)
            { throw new ArgumentException("发声对象已释放。"); }
            if ((effective.Emitter != null || effective.Position.HasValue) &&
                (Capabilities & EAudioCapabilities.Spatial) == 0)
            { throw new NotSupportedException("后端不支持空间音频。"); }
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
                m_sessionCancellation.Token, effective.Scope?.Token ?? CancellationToken.None);
            var playback = new Playback
            {
                ID = Interlocked.Increment(ref m_nextID), Key = key, Options = effective,
                Callback = onStarted, Session = m_session, Token = cancellation.Token,
                Cancellation = cancellation, Reason = EAudioEndReason.Unknown
            };
            m_playbacks.Add(playback.ID, playback);
            PreparePlaybackAsync(playback).Forget();
            return playback;
        }

        /// <summary>停止单次播放，淡出期间继续持有资源。</summary>
        /// <param name="playID">框架播放标识。</param>
        /// <param name="fadeOutSeconds">非负淡出秒数。</param>
        /// <returns>是否找到并接受该操作。</returns>
        public bool StopAudio(long playID, float fadeOutSeconds = 0)
        {
            if (float.IsNaN(fadeOutSeconds) || float.IsInfinity(fadeOutSeconds) || fadeOutSeconds < 0)
            { return false; }
            if (!m_playbacks.TryGetValue(playID, out Playback playback)) { return false; }
            playback.Reason = EAudioEndReason.Stopped;
            return StopPlayback(playback, fadeOutSeconds);
        }

        /// <summary>暂停指定实例。</summary>
        /// <param name="playID">框架播放标识。</param>
        /// <returns>是否接受暂停。</returns>
        public bool PauseAudio(long playID) { return SetPaused(playID, true); }

        /// <summary>恢复指定实例。</summary>
        /// <param name="playID">框架播放标识。</param>
        /// <returns>是否接受恢复。</returns>
        public bool ResumeAudio(long playID) { return SetPaused(playID, false); }

        /// <summary>设置全局或单实例参数。</summary>
        /// <param name="key">参数名。</param>
        /// <param name="value">制作侧约定单位的数值。</param>
        /// <param name="playID">零为全局，否则为实例标识。</param>
        /// <returns>操作结果。</returns>
        public AudioOperationResult SetParameter(string key, float value, long playID = 0)
        {
            EAudioCapabilities capability = playID == 0
                ? EAudioCapabilities.GlobalParameter : EAudioCapabilities.InstanceParameter;
            if (!IsReady) { return Failure(EAudioError.NotReady, "音频未就绪。"); }
            if ((Capabilities & capability) == 0)
            { return Failure(EAudioError.UnsupportedCapability, "后端不支持此参数作用域。"); }
            return m_backend.SetParameter(playID, key, value);
        }

        /// <summary>设置项目约定的全局参数。</summary>
        /// <param name="key">参数名称。</param>
        /// <param name="value">项目约定单位的参数值。</param>
        /// <returns>操作结果。</returns>
        public AudioOperationResult SetGlobalParameter(string key, float value)
        { return SetParameter(key, value); }

        /// <summary>只设置指定实例参数，不影响同对象的其他声音。</summary>
        /// <param name="playID">框架实例标识。</param>
        /// <param name="key">参数名称。</param>
        /// <param name="value">项目约定单位的数值。</param>
        /// <returns>操作结果。</returns>
        public AudioOperationResult SetParameter(long playID, string key, float value)
        {
            return playID > 0 ? SetParameter(key, value, playID)
                : Failure(EAudioError.BackendFailure, "实例播放标识必须大于零。");
        }

        /// <summary>设置全局离散状态。</summary>
        /// <param name="groupKey">状态组。</param>
        /// <param name="valueKey">状态值。</param>
        /// <returns>操作结果。</returns>
        public AudioOperationResult SetState(string groupKey, string valueKey)
        {
            if ((Capabilities & EAudioCapabilities.State) == 0)
            { return Failure(EAudioError.UnsupportedCapability, "后端不支持全局状态。"); }
            return IsReady ? m_backend.SetState(groupKey, valueKey)
                : Failure(EAudioError.NotReady, "音频未就绪。");
        }

        /// <summary>设置指定发声对象的切换值。</summary>
        /// <param name="emitter">空间发声对象，空值为默认二维对象。</param>
        /// <param name="groupKey">切换组。</param>
        /// <param name="valueKey">切换值。</param>
        /// <returns>操作结果。</returns>
        public AudioOperationResult SetSwitch(AudioEmitter emitter, string groupKey, string valueKey)
        {
            if ((Capabilities & EAudioCapabilities.Switch) == 0)
            { return Failure(EAudioError.UnsupportedCapability, "后端不支持切换值。"); }
            return IsReady ? m_backend.SetSwitch(emitter, groupKey, valueKey)
                : Failure(EAudioError.NotReady, "音频未就绪。");
        }

        /// <summary>显式选择空间监听目标。</summary>
        /// <param name="target">目标为空时恢复固定监听位置。</param>
        /// <exception cref="InvalidOperationException">音频尚未就绪。</exception>
        public void SetListener(Transform target) { RequireReady(); m_backend.SetListener(target); }

        /// <summary>查询活跃或近期结束的请求。</summary>
        /// <param name="playID">播放标识。</param>
        /// <param name="info">不可变快照。</param>
        /// <returns>是否找到记录。</returns>
        public bool TryGetPlayback(long playID, out AudioPlaybackInfo info)
        {
            if (m_playbacks.TryGetValue(playID, out Playback playback)) { info = Snapshot(playback); return true; }
            return m_history.TryGetValue(playID, out info);
        }

        /// <summary>获取诊断用播放快照，不暴露内部可变状态。</summary>
        /// <returns>活跃与近期终态记录。</returns>
        public AudioPlaybackInfo[] GetPlaybackSnapshots()
        {
            var snapshots = new List<AudioPlaybackInfo>(m_history.Values);
            foreach (Playback playback in m_playbacks.Values) { snapshots.Add(Snapshot(playback)); }
            return snapshots.ToArray();
        }

        /// <summary>获取诊断用组引用摘要。</summary>
        /// <returns>组名称、引用与就绪状态。</returns>
        public string[] GetGroupDescriptions()
        {
            var descriptions = new List<string>();
            foreach (GroupEntry entry in m_groups.Values)
            { descriptions.Add($"{entry.Key}：引用={entry.Users}，原生已加载={entry.NativeLoaded}"); }
            return descriptions.ToArray();
        }

        /// <summary>获取可选后端的单实例诊断描述，业务无需解析它。</summary>
        /// <param name="playID">框架播放标识。</param>
        /// <returns>后端诊断信息。</returns>
        public string GetBackendPlaybackDescription(long playID)
        {
            return m_backend is IAudioBackendDiagnostics diagnostics
                ? diagnostics.GetPlaybackDescription(playID) : string.Empty;
        }

        /// <summary>在主线程处理取消，不依赖游戏时间缩放。</summary>
        /// <param name="deltaTime">游戏时间间隔。</param>
        /// <param name="unscaledDeltaTime">真实时间间隔。</param>
        public void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (m_backend != null && m_initContent != null && !m_backend.IsReady && !m_shuttingDown)
            {
                AlloyDebug.Error("[Audio] 后端异常终止，正在关闭本会话。");
                ShutdownImmediately();
                return;
            }
            m_updateSnapshot.Clear();
            m_updateSnapshot.AddRange(m_playbacks.Values);
            foreach (Playback playback in m_updateSnapshot)
            {
                if (playback.Token.IsCancellationRequested || playback.Options.Scope?.IsDisposed == true)
                { StopPlayback(playback, 0); }
            }
        }

        /// <summary>先关闭原生引擎，再释放所有文件与引用。</summary>
        /// <returns>关闭任务。</returns>
        public UniTask ShutdownAsync()
        {
            if (m_backend == null) { return UniTask.CompletedTask; }
            if (m_shutdown == null)
            {
                m_shutdown = new UniTaskCompletionSource();
                ShutdownCoreAsync(m_shutdown).Forget();
            }
            return m_shutdown.Task;
        }

        private async UniTask ShutdownCoreAsync(UniTaskCompletionSource completion)
        {
            try
            {
                m_shuttingDown = true;
                var pendingGroups = new List<GroupEntry>(m_groups.Values);
                UniTaskCompletionSource initialization = m_initialization;
                m_sessionCancellation.Cancel();
                m_backend.ShutdownImmediately();

                // 等待已取消交付真正退出，禁止资源服务先关闭而仍有后台句柄等待释放。
                if (initialization != null)
                { try { await initialization.Task; } catch (Exception) { } }
                foreach (GroupEntry entry in pendingGroups)
                { try { await entry.Ready.Task; } catch (Exception) { } }
                ShutdownImmediately();
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
                AlloyDebug.Error(exception);
            }
        }

        /// <summary>框架退出时幂等地终止引擎，旧会话不会影响新请求。</summary>
        public void ShutdownImmediately()
        {
            if (m_backend == null) { return; }
            m_shuttingDown = true;
            m_sessionCancellation.Cancel();
            m_backend.ShutdownImmediately();
            foreach (Playback playback in new List<Playback>(m_playbacks.Values))
            {
                playback.Reason = EAudioEndReason.Shutdown;
                Finish(playback, Failure(EAudioError.Cancelled, "音频系统关闭。"));
            }
            foreach (GroupEntry entry in new List<GroupEntry>(m_groups.Values))
            { entry.Content?.Dispose(); entry.Content = null; entry.Released = true; }
            foreach (GroupEntry entry in m_retiredGroups)
            { entry.Content?.Dispose(); foreach (AudioGroupLease lease in entry.Dependencies) { lease.Dispose(); } }
            m_retiredGroups.Clear();
            m_groups.Clear();
            m_initContent?.Dispose();
            m_initContent = null;
            m_events.Clear();
            m_backend = null;
            m_provider = null;
            m_initialization = null;
            m_sessionCancellation.Dispose();
            m_sessionCancellation = null;
            m_session++;
        }

        /// <summary>停止指定归属，保留其他归属播放。</summary>
        /// <param name="scope">目标作用域。</param>
        /// <param name="fadeOutSeconds">淡出秒数。</param>
        /// <exception cref="ArgumentOutOfRangeException">淡出时长无效。</exception>
        public void StopScope(AudioScope scope, float fadeOutSeconds = 0)
        {
            if (scope == null) { return; }
            if (float.IsNaN(fadeOutSeconds) || float.IsInfinity(fadeOutSeconds) || fadeOutSeconds < 0)
            { throw new ArgumentOutOfRangeException(nameof(fadeOutSeconds)); }
            foreach (Playback playback in new List<Playback>(m_playbacks.Values))
            {
                if (playback.Options.Scope != scope) { continue; }
                playback.Reason = scope.IsDisposed ? EAudioEndReason.ScopeDisposed : EAudioEndReason.Stopped;
                StopPlayback(playback, fadeOutSeconds);
            }
        }

        internal void ReleaseGroup(object value)
        {
            var entry = (GroupEntry)value;
            if (entry.Released || entry.Users <= 0) { return; }
            entry.Users--;
            if (entry.Users == 0 && entry.LoadFinished) { UnloadGroup(entry); }
        }

        private async UniTask InitializeCoreAsync(long session)
        {
            IAudioBackend backend = m_backend;
            IAudioContentProvider provider = m_provider;
            UniTaskCompletionSource completion = m_initialization;
            CancellationToken token = m_sessionCancellation.Token;
            AudioContentLease content = null;
            try
            {
                content = await provider.PrepareGroupAsync("Init", m_settings, token);
                token.ThrowIfCancellationRequested();
                await backend.InitializeAsync(m_settings, content, session, OnEnded, token);
                if (session != m_session) { throw new OperationCanceledException(); }
                m_initContent = content;
                content = null;
                completion.TrySetResult();
                AlloyDebug.Log($"[Audio] 引擎与 Init 就绪：{BackendName}");
            }
            catch (Exception exception)
            {
                // 仅关闭当前初始化拥有的后端，不能破坏重入后的新会话。
                backend.ShutdownImmediately();
                content?.Dispose();
                completion.TrySetException(exception);
                AlloyDebug.Error(exception);
            }
        }

        private GroupEntry GetOrStartGroup(string key)
        {
            if (m_groups.TryGetValue(key, out GroupEntry entry)) { entry.Users++; return entry; }
            AudioContentGroup definition = null;
            foreach (AudioContentGroup candidate in m_provider.Manifest.Groups)
            { if (candidate.Key == key) { definition = candidate; break; } }
            if (definition == null) { throw new KeyNotFoundException($"音频组不存在：{key}"); }
            entry = new GroupEntry { Key = key, Session = m_session, Users = 1 };
            m_groups.Add(key, entry);
            LoadGroupAsync(entry, definition).Forget();
            return entry;
        }

        private async UniTask LoadGroupAsync(GroupEntry entry, AudioContentGroup definition)
        {
            IAudioBackend backend = m_backend;
            IAudioContentProvider provider = m_provider;
            CancellationToken token = m_sessionCancellation.Token;
            try
            {
                if (definition.Dependencies != null)
                {
                    foreach (string key in definition.Dependencies)
                    { entry.Dependencies.Add(await AcquireGroupAsync(key, cancellationToken: token)); }
                }
                entry.Content = await provider.PrepareGroupAsync(entry.Key, m_settings, token);
                token.ThrowIfCancellationRequested();
                await backend.LoadGroupAsync(entry.Content, token);
                entry.NativeLoaded = true;
                token.ThrowIfCancellationRequested();
                entry.LoadFinished = true;
                entry.Ready.TrySetResult();
            }
            catch (Exception exception)
            {
                // 先完成回滚，再唤醒等待者；等待者释放引用可能同步重入卸载逻辑。
                bool retain = false;
                if (entry.NativeLoaded && entry.Session == m_session && backend.IsReady)
                {
                    try { backend.UnloadGroup(entry.Content); }
                    catch (Exception rollbackFailure)
                    {
                        retain = true;
                        entry.Released = true;
                        m_groups.Remove(entry.Key);
                        m_retiredGroups.Add(entry);
                        AlloyDebug.Error(rollbackFailure);
                    }
                }
                entry.NativeLoaded = false;
                if (!retain)
                {
                    entry.Content?.Dispose();
                    entry.Content = null;
                    foreach (AudioGroupLease lease in entry.Dependencies) { lease.Dispose(); }
                    entry.Dependencies.Clear();
                }
                entry.LoadFinished = true;
                entry.Ready.TrySetException(exception);
            }
            if (entry.Users == 0) { UnloadGroup(entry); }
        }

        private void UnloadGroup(GroupEntry entry)
        {
            if (entry.Released) { return; }
            entry.Released = true;
            if (entry.Session == m_session)
            {
                m_groups.Remove(entry.Key);
                if (entry.NativeLoaded && entry.Content != null && m_backend?.IsReady == true)
                {
                    try { m_backend.UnloadGroup(entry.Content); }
                    catch (Exception exception)
                    {
                        // 原生卸载未确认时，保留整个依赖链，关闭引擎后才释放。
                        m_retiredGroups.Add(entry);
                        AlloyDebug.Error(exception);
                        return;
                    }
                }
            }
            entry.Content?.Dispose();
            entry.Content = null;
            foreach (AudioGroupLease lease in entry.Dependencies) { lease.Dispose(); }
            entry.Dependencies.Clear();
        }

        private async UniTask PreparePlaybackAsync(Playback playback)
        {
            try
            {
                if (!IsReady) { throw new InvalidOperationException("音频尚未就绪。"); }
                if (string.IsNullOrWhiteSpace(playback.Key) ||
                    !m_events.TryGetValue(playback.Key, out AudioEventDependency dependency))
                { Finish(playback, Failure(EAudioError.UnknownKey, $"未收录事件：{playback.Key}")); return; }
                if (playback.Options.Position.HasValue && playback.Options.Emitter != null)
                { throw new ArgumentException("Position 与 Emitter 不能同时指定。"); }
                playback.State = EAudioPlaybackState.Preparing;
                foreach (string key in dependency.Groups)
                {
                    if (playback.Options.ReadinessMode == EAudioReadinessMode.RequireReady &&
                        (!m_groups.TryGetValue(key, out GroupEntry entry) ||
                            !entry.LoadFinished || !entry.NativeLoaded))
                    { Finish(playback, Failure(EAudioError.NotReady, $"事件资源尚未预加载：{playback.Key}")); return; }
                    AudioGroupLease lease = await AcquireGroupAsync(key, cancellationToken: playback.Token);
                    if (playback.Terminal) { lease.Dispose(); return; }
                    playback.Leases.Add(lease);
                }
                // 等待结束后再次检查，禁止已销毁界面或旧会话补播。
                playback.Token.ThrowIfCancellationRequested();
                if (playback.Terminal) { return; }
                if (playback.Session != m_session || playback.Options.Scope?.IsDisposed == true)
                { throw new OperationCanceledException(); }
                AudioEmitter emitter = playback.Options.Emitter;
                if (playback.Options.Position.HasValue) { emitter = new AudioEmitter(playback.Options.Position.Value); }
                AudioOperationResult result = m_backend.Play(playback.ID, playback.Key, emitter);
                if (!result.IsSuccess) { Finish(playback, result); return; }
                playback.State = EAudioPlaybackState.Playing;
                playback.Started = true;
                Publish(AudioEventNames.STARTED, playback);
                NotifyStart(playback, result);
            }
            catch (OperationCanceledException)
            { Finish(playback, Failure(EAudioError.Cancelled, "音频请求已取消。")); }
            catch (Exception exception)
            {
                Finish(playback, Failure(IsReady ? EAudioError.ResourceFailure : EAudioError.NotReady,
                    exception.Message));
            }
            finally
            {
                playback.PreparationFinished = true;
                if (playback.Terminal) { playback.Cancellation.Dispose(); }
            }
        }

        private bool StopPlayback(Playback playback, float fade)
        {
            if (playback.Terminal) { return false; }
            if (!playback.Started)
            {
                playback.Cancellation.Cancel();
                Finish(playback, Failure(EAudioError.Cancelled, "播放提交前已停止。"));
                return true;
            }
            if (playback.State == EAudioPlaybackState.Stopping) { return true; }
            if (m_backend?.Stop(playback.ID, fade) != true) { return false; }
            playback.State = EAudioPlaybackState.Stopping;
            return true;
        }

        private bool SetPaused(long id, bool paused)
        {
            if (!IsReady || !m_playbacks.TryGetValue(id, out Playback playback) ||
                !playback.Started || playback.State == EAudioPlaybackState.Stopping) { return false; }
            if ((Capabilities & EAudioCapabilities.Pause) == 0) { return false; }
            if (!m_backend.SetPaused(id, paused)) { return false; }
            playback.State = paused ? EAudioPlaybackState.Paused : EAudioPlaybackState.Playing;
            return true;
        }

        private void OnEnded(long session, long id)
        {
            if (session != m_session || !m_playbacks.TryGetValue(id, out Playback playback)) { return; }
            Finish(playback, new AudioOperationResult());
        }

        private void Finish(Playback playback, AudioOperationResult result)
        {
            if (playback.Terminal) { return; }
            playback.Terminal = true;
            if (playback.PreparationFinished) { playback.Cancellation.Dispose(); }
            playback.Result = result;
            playback.State = result.IsSuccess || playback.Started
                ? EAudioPlaybackState.Ended : EAudioPlaybackState.Failed;
            m_playbacks.Remove(playback.ID);
            foreach (AudioGroupLease lease in playback.Leases) { lease.Dispose(); }
            playback.Leases.Clear();
            m_history[playback.ID] = Snapshot(playback);
            m_historyOrder.Enqueue(playback.ID);
            int limit = Math.Max(0, m_settings?.PlaybackHistoryLimit ?? 256);
            while (m_historyOrder.Count > limit) { m_history.Remove(m_historyOrder.Dequeue()); }
            NotifyStart(playback, result);
            Publish(playback.State == EAudioPlaybackState.Failed
                ? AudioEventNames.FAILED : AudioEventNames.ENDED, playback);
            if (!result.IsSuccess && result.Error != EAudioError.Cancelled)
            { AlloyDebug.Error($"[Audio] Key={playback.Key} PlayID={playback.ID} {result.Error}: {result.Message}"); }
        }

        private static void NotifyStart(Playback playback, AudioOperationResult result)
        {
            Action<AudioStartResult> callback = playback.Callback;
            playback.Callback = null;
            playback.Start.TrySetResult(new AudioStartResult(playback.ID, result));
            try { callback?.Invoke(new AudioStartResult(playback.ID, result)); }
            catch (Exception exception) { AlloyDebug.Error(exception); }
        }

        private static void Publish(string key, Playback playback)
        {
            try { EventSystem.Instance.SendEvent(key, new AudioPlaybackNotification(Snapshot(playback))); }
            catch (Exception exception) { AlloyDebug.Error(exception); }
        }

        private static AudioPlaybackInfo Snapshot(Playback playback)
        {
            return new AudioPlaybackInfo(playback.ID, playback.Key, playback.Options.Scope?.ID ?? 0,
                playback.State, playback.Reason, playback.Result);
        }

        private static AudioOperationResult Failure(EAudioError error, string message)
        { return new AudioOperationResult(error, message); }

        private void RequireReady()
        { if (!IsReady) { throw new InvalidOperationException("音频系统尚未就绪。"); } }

        private static void ValidateDependencies(string key, Dictionary<string, AudioContentGroup> groups,
            HashSet<string> visiting)
        {
            if (!groups.TryGetValue(key, out AudioContentGroup group) || !visiting.Add(key))
            { throw new InvalidOperationException($"音频组依赖缺失或循环：{key}"); }
            if (group.Dependencies != null)
            {
                foreach (string dependency in group.Dependencies)
                { ValidateDependencies(dependency, groups, visiting); }
            }
            visiting.Remove(key);
        }

        private sealed class GroupEntry
        {
            internal string Key;
            internal long Session;
            internal int Users;
            internal bool LoadFinished;
            internal bool NativeLoaded;
            internal bool Released;
            internal AudioContentLease Content;
            internal readonly UniTaskCompletionSource Ready = new UniTaskCompletionSource();
            internal readonly List<AudioGroupLease> Dependencies = new List<AudioGroupLease>();
        }

        private sealed class Playback
        {
            internal long ID;
            internal long Session;
            internal string Key;
            internal AudioPlayOptions Options;
            internal Action<AudioStartResult> Callback;
            internal CancellationToken Token;
            internal CancellationTokenSource Cancellation;
            internal bool PreparationFinished;
            internal bool Terminal;
            internal bool Started;
            internal EAudioPlaybackState State;
            internal EAudioEndReason Reason;
            internal AudioOperationResult Result;
            internal readonly List<AudioGroupLease> Leases = new List<AudioGroupLease>();
            internal readonly UniTaskCompletionSource<AudioStartResult> Start =
                new UniTaskCompletionSource<AudioStartResult>();
        }

        private sealed class InitializationRequest
        {
            internal AudioManager Manager;
            internal Action<Exception> Callback;
            internal CancellationToken Token;

            internal async UniTask RunAsync()
            {
                Exception failure = null;
                try { await Manager.InitializeAsync(Token); }
                catch (Exception exception) { failure = exception; }
                try { Callback(failure); }
                catch (Exception exception) { AlloyDebug.Error(exception); }
            }
        }
    }
}
