using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlloyFramework.Audio
{
    [Flags]
    public enum EAudioCapabilities
    {
        None = 0,
        Spatial = 1,
        Pause = 2,
        InstanceParameter = 4,
        GlobalParameter = 8,
        State = 16,
        Switch = 32
    }

    public enum EAudioReadinessMode { LoadIfNeeded, RequireReady }
    public enum EAudioPlaybackState { Requested, Preparing, Playing, Paused, Stopping, Ended, Failed }
    public enum EAudioEndReason { Completed, Stopped, ScopeDisposed, Shutdown, Unknown }
    public enum EAudioError
    { None, NotReady, UnknownKey, ResourceFailure, BackendFailure, UnsupportedCapability, Cancelled }
    public enum EAudioBankType { User, Event, Bus }

    public sealed class AudioSettings
    {
        public string Platform { get; set; }
        public string Language { get; set; } = "English(US)";
        public EAudioReadinessMode DefaultReadiness { get; set; } = EAudioReadinessMode.LoadIfNeeded;
        public int PlaybackHistoryLimit { get; set; } = 256;
    }

    public struct AudioPlayOptions
    {
        public AudioScope Scope { get; set; }
        public AudioEmitter Emitter { get; set; }
        public Vector3? Position { get; set; }
        public EAudioReadinessMode? ReadinessMode { get; set; }
    }

    public readonly struct AudioOperationResult
    {
        public bool IsSuccess => Error == EAudioError.None;
        public EAudioError Error { get; }
        public string Message { get; }

        /// <summary>创建音频操作结果。</summary>
        /// <param name="error">错误类型，None 表示成功。</param>
        /// <param name="message">错误上下文。</param>
        public AudioOperationResult(EAudioError error = EAudioError.None, string message = null)
        {
            Error = error;
            Message = message;
        }
    }

    public readonly struct AudioPlaybackInfo
    {
        public long PlayID { get; }
        public string Key { get; }
        public long ScopeID { get; }
        public EAudioPlaybackState State { get; }
        public EAudioEndReason EndReason { get; }
        public AudioOperationResult Result { get; }

        /// <summary>创建不可变播放快照。</summary>
        /// <param name="playID">框架播放标识。</param>
        /// <param name="key">事件名称。</param>
        /// <param name="scopeID">生命周期归属。</param>
        /// <param name="state">当前状态。</param>
        /// <param name="endReason">结束原因。</param>
        /// <param name="result">操作结果。</param>
        public AudioPlaybackInfo(long playID, string key, long scopeID, EAudioPlaybackState state,
            EAudioEndReason endReason, AudioOperationResult result)
        {
            PlayID = playID;
            Key = key;
            ScopeID = scopeID;
            State = state;
            EndReason = endReason;
            Result = result;
        }
    }

    public readonly struct AudioStartResult
    {
        public long PlayID { get; }
        public AudioOperationResult Result { get; }

        /// <summary>创建播放提交结果。</summary>
        /// <param name="playID">框架播放标识。</param>
        /// <param name="result">提交结果。</param>
        public AudioStartResult(long playID, AudioOperationResult result)
        {
            PlayID = playID;
            Result = result;
        }
    }

    public readonly struct AudioPlaybackNotification
    {
        public AudioPlaybackInfo Playback { get; }

        /// <summary>创建音频生命周期通知。</summary>
        /// <param name="playback">通知时的播放快照。</param>
        public AudioPlaybackNotification(AudioPlaybackInfo playback) { Playback = playback; }
    }

    public static class AudioEventNames
    {
        /// <summary>引擎已接受播放的消息名称。</summary>
        public const string STARTED = "Audio.Started";
        /// <summary>播放已结束的消息名称。</summary>
        public const string ENDED = "Audio.Ended";
        /// <summary>音频请求失败的消息名称。</summary>
        public const string FAILED = "Audio.Failed";
    }

    public sealed class AudioEmitter : IDisposable
    {
        private readonly bool m_hasTarget; // 区分固定坐标与已销毁的跟随目标。
        private bool m_disposed; // 显式释放空间发声对象。
        public Transform Target { get; }
        public Vector3 Position { get; }

        /// <summary>绑定持续跟随的空间发声目标。</summary>
        /// <param name="target">需要跟随的目标。</param>
        /// <exception cref="ArgumentNullException">目标不存在。</exception>
        public AudioEmitter(Transform target)
        {
            Target = target ? target : throw new ArgumentNullException(nameof(target));
            m_hasTarget = true;
        }

        internal AudioEmitter(Vector3 position) { Position = position; }
        public bool IsValid => !m_disposed && (!m_hasTarget || Target);
        public bool IsTransient => !m_hasTarget;

        /// <summary>释放发声对象，后端在原生声音退出后注销对象。</summary>
        public void Dispose() { m_disposed = true; }
    }

    public interface IAudioBackend
    {
        bool IsReady { get; }
        EAudioCapabilities Capabilities { get; }

        /// <summary>初始化唯一音频引擎及默认发声和监听对象。</summary>
        /// <param name="settings">框架音频设置。</param>
        /// <param name="initialContent">已交付的初始化资源。</param>
        /// <param name="session">当前会话代号。</param>
        /// <param name="ended">将真实结束通知交回框架。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>初始化任务。</returns>
        UniTask InitializeAsync(AudioSettings settings, AudioContentLease initialContent, long session,
            Action<long, long> ended, CancellationToken cancellationToken);

        /// <summary>关闭本适配器持有的原生资源和引擎。</summary>
        /// <returns>关闭任务。</returns>
        UniTask ShutdownAsync();

        /// <summary>紧急退出时同步终止原生引擎。</summary>
        void ShutdownImmediately();

        /// <summary>加载组中事件定义和媒体，失败时回滚。</summary>
        /// <param name="content">保持有效的文件租约。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>加载任务。</returns>
        UniTask LoadGroupAsync(AudioContentLease content, CancellationToken cancellationToken);

        /// <summary>在播放退出后卸载音频组。</summary>
        /// <param name="content">待卸载的文件租约。</param>
        void UnloadGroup(AudioContentLease content);

        /// <summary>提交一份独立的播放实例。</summary>
        /// <param name="playID">框架播放标识。</param>
        /// <param name="key">事件名称。</param>
        /// <param name="emitter">空间发声对象，空值使用默认二维对象。</param>
        /// <returns>提交结果。</returns>
        AudioOperationResult Play(long playID, string key, AudioEmitter emitter);

        /// <summary>停止指定播放。</summary>
        /// <param name="playID">框架播放标识。</param>
        /// <param name="fadeOutSeconds">淡出秒数。</param>
        /// <returns>操作是否被接受。</returns>
        bool Stop(long playID, float fadeOutSeconds);

        /// <summary>设置指定播放的暂停状态。</summary>
        /// <param name="playID">框架播放标识。</param>
        /// <param name="paused">是否暂停。</param>
        /// <returns>操作是否被接受。</returns>
        bool SetPaused(long playID, bool paused);

        /// <summary>设置全局或实例参数。</summary>
        /// <param name="playID">零表示全局，否则表示播放实例。</param>
        /// <param name="key">参数名称。</param>
        /// <param name="value">项目约定的参数值。</param>
        /// <returns>操作结果。</returns>
        AudioOperationResult SetParameter(long playID, string key, float value);

        /// <summary>设置全局离散状态。</summary>
        /// <param name="groupKey">状态组。</param>
        /// <param name="valueKey">状态值。</param>
        /// <returns>操作结果。</returns>
        AudioOperationResult SetState(string groupKey, string valueKey);

        /// <summary>设置发声对象级离散状态。</summary>
        /// <param name="emitter">发声对象。</param>
        /// <param name="groupKey">切换组。</param>
        /// <param name="valueKey">切换值。</param>
        /// <returns>操作结果。</returns>
        AudioOperationResult SetSwitch(AudioEmitter emitter, string groupKey, string valueKey);

        /// <summary>显式绑定空间监听器。</summary>
        /// <param name="target">监听目标，空值恢复固定二维监听位置。</param>
        void SetListener(Transform target);
    }
}
