
namespace AlloyFramework.Audio
{
    /// <summary>音频后端可选的实例时间轴能力，不影响其他播放后端。</summary>
    public interface IAudioTimelineBackend
    {
        /// <summary>定位一份独立的原生播放实例。</summary>
        /// <param name="playID">框架播放标识。</param>
        /// <param name="timeMs">目标毫秒数。</param>
        /// <returns>是否接受定位请求。</returns>
        bool Seek(long playID, int timeMs);

        /// <summary>读取独立播放实例的位置。</summary>
        /// <param name="playID">框架播放标识。</param>
        /// <param name="timeMs">返回的毫秒位置。</param>
        /// <returns>本次是否取得有效位置。</returns>
        bool TryGetPosition(long playID, out int timeMs);
    }

    /// <summary>后端可选的首次播放起点能力，提交播放与定位之间不能等待 Unity 帧。</summary>
    public interface IAudioTimelineStartBackend
    {
        /// <summary>提交独立实例并在同一次调用中设置首次播放位置。</summary>
        /// <param name="playID">框架播放标识。</param>
        /// <param name="key">音频事件。</param>
        /// <param name="emitter">可选发声对象。</param>
        /// <param name="timeMs">非负起点毫秒数。</param>
        /// <returns>播放和起点定位的提交结果。</returns>
        AudioOperationResult PlayFrom(long playID, string key, AudioEmitter emitter, int timeMs);
    }
    public sealed partial class AudioManager
    {
        /// <summary>当前后端是否提供独立实例的定位和位置查询。</summary>
        public bool SupportsTimeline => IsReady && m_backend is IAudioTimelineBackend;

        /// <summary>定位已提交且尚未停止的音频实例。</summary>
        /// <param name="playID">框架播放标识。</param>
        /// <param name="timeMs">非负目标毫秒数。</param>
        /// <returns>后端是否接受定位。</returns>
        public bool SeekAudio(long playID, int timeMs)
        {
            if (timeMs < 0 || !IsReady || !(m_backend is IAudioTimelineBackend timeline) ||
                !m_playbacks.TryGetValue(playID, out Playback playback) || !playback.Started ||
                playback.State == EAudioPlaybackState.Stopping) return false;
            return timeline.Seek(playID, timeMs);
        }

        /// <summary>取得活跃音频实例的当前位置。</summary>
        /// <param name="playID">框架播放标识。</param>
        /// <param name="timeMs">返回的毫秒位置。</param>
        /// <returns>是否获得有效位置。</returns>
        public bool TryGetAudioPosition(long playID, out int timeMs)
        {
            timeMs = 0;
            if (!IsReady || !(m_backend is IAudioTimelineBackend timeline) ||
                !m_playbacks.TryGetValue(playID, out Playback playback) || !playback.Started ||
                playback.State == EAudioPlaybackState.Stopping) return false;
            return timeline.TryGetPosition(playID, out timeMs);
        }
    }
}
