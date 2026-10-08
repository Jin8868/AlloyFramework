using System;
using System.Collections.Generic;

namespace AlloyFramework.Audio
{
    public enum EAudioVolumeChannel
    {
        Master,
        BGM,
        SFX
    }

    public interface IAudioVolumeBackend
    {
        /// <summary>设置分类音量，影响当前及后续路由到该分类的声音。</summary>
        /// <param name="channel">音量分类。</param>
        /// <param name="volume">0～1 的音量。</param>
        /// <returns>后端操作结果。</returns>
        AudioOperationResult SetChannelVolume(EAudioVolumeChannel channel, float volume);

        /// <summary>仅修改指定播放实例的音量。</summary>
        /// <param name="playID">框架播放标识。</param>
        /// <param name="volume">0～1 的音量。</param>
        /// <returns>后端操作结果。</returns>
        AudioOperationResult SetPlaybackVolume(long playID, float volume);
    }

    public sealed partial class AudioManager
    {
        private float m_masterVolume = 1; // 当前进程总音量，不写入存档。
        private float m_bgmVolume = 1; // 当前进程背景音乐音量。
        private float m_sfxVolume = 1; // 当前进程音效音量。

        /// <summary>设置总音量，范围为 0～1。</summary>
        /// <param name="volume">目标音量。</param>
        /// <returns>操作结果，成功后更新获取接口的设置值。</returns>
        /// <exception cref="ArgumentOutOfRangeException">音量不在 0～1 范围内或不是有限值。</exception>
        public AudioOperationResult SetMasterVolume(float volume)
        { return SetVolume(EAudioVolumeChannel.Master, volume); }

        /// <summary>获取当前总音量设置值，默认值为 1。</summary>
        /// <returns>0～1 的设置值。</returns>
        public float GetMasterVolume() { return m_masterVolume; }

        /// <summary>设置背景音乐音量，范围为 0～1。</summary>
        /// <param name="volume">目标音量。</param>
        /// <returns>操作结果。</returns>
        /// <exception cref="ArgumentOutOfRangeException">音量无效。</exception>
        public AudioOperationResult SetBGMVolume(float volume)
        { return SetVolume(EAudioVolumeChannel.BGM, volume); }

        /// <summary>获取当前背景音乐音量设置值，默认值为 1。</summary>
        /// <returns>0～1 的设置值。</returns>
        public float GetBGMVolume() { return m_bgmVolume; }

        /// <summary>设置音效音量，范围为 0～1。</summary>
        /// <param name="volume">目标音量。</param>
        /// <returns>操作结果。</returns>
        /// <exception cref="ArgumentOutOfRangeException">音量无效。</exception>
        public AudioOperationResult SetSFXVolume(float volume)
        { return SetVolume(EAudioVolumeChannel.SFX, volume); }

        /// <summary>获取当前音效音量设置值，默认值为 1。</summary>
        /// <returns>0～1 的设置值。</returns>
        public float GetSFXVolume() { return m_sfxVolume; }

        /// <summary>设置分类音量，声音分类由制作侧的总线路由决定。</summary>
        /// <param name="channel">总音量、背景音乐或音效。</param>
        /// <param name="volume">0～1 的设置值。</param>
        /// <returns>操作结果；未就绪或后端拒绝时保持原设置。</returns>
        /// <exception cref="ArgumentOutOfRangeException">分类或音量无效。</exception>
        public AudioOperationResult SetVolume(EAudioVolumeChannel channel, float volume)
        {
            ValidateVolume(volume);
            GetVolume(channel);

            // 仅在原生后端接受设置后提交状态，避免获取值与实际控制脱节。
            AudioOperationResult readiness = GetVolumeBackend(out IAudioVolumeBackend backend);
            if (!readiness.IsSuccess) { return ReportVolumeResult(readiness); }
            AudioOperationResult result = backend.SetChannelVolume(channel, volume);
            if (!result.IsSuccess) { return ReportVolumeResult(result); }
            switch (channel)
            {
                case EAudioVolumeChannel.Master: m_masterVolume = volume; break;
                case EAudioVolumeChannel.BGM: m_bgmVolume = volume; break;
                case EAudioVolumeChannel.SFX: m_sfxVolume = volume; break;
            }
            return result;
        }

        /// <summary>获取分类音量设置值，不测量实际输出响度。</summary>
        /// <param name="channel">音量分类。</param>
        /// <returns>0～1 的设置值。</returns>
        /// <exception cref="ArgumentOutOfRangeException">分类无效。</exception>
        public float GetVolume(EAudioVolumeChannel channel)
        {
            switch (channel)
            {
                case EAudioVolumeChannel.Master: return m_masterVolume;
                case EAudioVolumeChannel.BGM: return m_bgmVolume;
                case EAudioVolumeChannel.SFX: return m_sfxVolume;
                default: throw new ArgumentOutOfRangeException(nameof(channel));
            }
        }

        /// <summary>设置单次播放音量；尚在准备资源的请求会在提交时应用。</summary>
        /// <param name="playID">活跃的框架播放标识。</param>
        /// <param name="volume">0～1 的设置值。</param>
        /// <returns>操作结果；已结束的标识不能设置。</returns>
        /// <exception cref="ArgumentOutOfRangeException">音量无效。</exception>
        public AudioOperationResult SetVolume(long playID, float volume)
        {
            ValidateVolume(volume);
            AudioOperationResult readiness = GetVolumeBackend(out IAudioVolumeBackend backend);
            if (!readiness.IsSuccess) { return ReportVolumeResult(readiness); }
            if (!m_playbacks.TryGetValue(playID, out Playback playback) || playback.Terminal)
            { return ReportVolumeResult(Failure(EAudioError.BackendFailure, $"播放标识已失效：{playID}")); }

            // 准备阶段只更新请求值，已提交实例使用 PlayingID 精确控制。
            if (playback.Started)
            {
                AudioOperationResult result = backend.SetPlaybackVolume(playID, volume);
                if (!result.IsSuccess) { return ReportVolumeResult(result); }
            }
            playback.Volume = volume;
            return new AudioOperationResult();
        }

        /// <summary>获取单次活跃播放的音量设置值，不包含分类音量的乘积。</summary>
        /// <param name="playID">框架播放标识。</param>
        /// <returns>0～1 的设置值。</returns>
        /// <exception cref="KeyNotFoundException">播放不存在或已经结束。</exception>
        public float GetVolume(long playID)
        {
            if (TryGetVolume(playID, out float volume)) { return volume; }
            throw new KeyNotFoundException($"播放标识已失效：{playID}");
        }

        /// <summary>尝试获取活跃播放的音量，适合声音可能已结束的调用场景。</summary>
        /// <param name="playID">框架播放标识。</param>
        /// <param name="volume">成功时返回设置值，失败时返回 0。</param>
        /// <returns>是否找到活跃播放。</returns>
        public bool TryGetVolume(long playID, out float volume)
        {
            if (m_playbacks.TryGetValue(playID, out Playback playback) && !playback.Terminal)
            {
                volume = playback.Volume;
                return true;
            }
            volume = 0;
            return false;
        }

        private AudioOperationResult GetVolumeBackend(out IAudioVolumeBackend backend)
        {
            backend = m_backend as IAudioVolumeBackend;
            if (!IsReady) { return Failure(EAudioError.NotReady, "音频未就绪，无法设置音量。"); }
            return backend != null ? new AudioOperationResult()
                : Failure(EAudioError.UnsupportedCapability, "当前后端不支持音量控制。");
        }

        private void ApplyChannelVolumes()
        {
            if (!(m_backend is IAudioVolumeBackend backend)) { return; }
            RequireVolumeApplied(backend.SetChannelVolume(EAudioVolumeChannel.Master, m_masterVolume));
            RequireVolumeApplied(backend.SetChannelVolume(EAudioVolumeChannel.BGM, m_bgmVolume));
            RequireVolumeApplied(backend.SetChannelVolume(EAudioVolumeChannel.SFX, m_sfxVolume));
        }

        private static void RequireVolumeApplied(AudioOperationResult result)
        {
            if (!result.IsSuccess) { throw new InvalidOperationException(result.Message); }
        }

        private static AudioOperationResult ReportVolumeResult(AudioOperationResult result)
        {
            if (!result.IsSuccess) { AlloyDebug.Error($"[Audio/Volume] {result.Error}: {result.Message}"); }
            return result;
        }

        private static void ValidateVolume(float volume)
        {
            if (float.IsNaN(volume) || float.IsInfinity(volume) || volume < 0 || volume > 1)
            { throw new ArgumentOutOfRangeException(nameof(volume), "音量必须是 0～1 的有限值。"); }
        }
    }
}
