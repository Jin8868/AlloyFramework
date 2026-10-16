namespace AlloyFramework.Audio.Wwise
{
    public sealed partial class WwiseAudioBackend : IAudioTimelineBackend, IAudioTimelineStartBackend
    {
        /// <summary>在同一次主线程提交中先 PostEvent 再定位，不等待开始通知或 Unity 帧。</summary>
        /// <param name="playID">框架播放标识。</param>
        /// <param name="key">音频事件。</param>
        /// <param name="emitter">可选发声对象。</param>
        /// <param name="timeMs">非负起点毫秒数。</param>
        /// <returns>事件与定位的提交结果，定位失败会请求停止刚创建的实例。</returns>
        public AudioOperationResult PlayFrom(long playID, string key, AudioEmitter emitter, int timeMs)
        {
            if (timeMs < 0) { return Error("音轨起点不能为负数。"); }
            AudioOperationResult result = Play(playID, key, emitter);
            if (!result.IsSuccess || timeMs == 0) { return result; }
            // SDK 命令按顺序进入同次提交，避免业务等待一帧后才将已发声的音轨拉到起点。
            if (Seek(playID, timeMs)) { return result; }
            Stop(playID, 0);
            return Error("Wwise 未接受首次音轨定位，请检查事件是否为可定位的独立音轨。");
        }

        /// <summary>按 Playing ID 定位，不影响相同事件的其他实例。</summary>
        /// <param name="playID">框架播放标识。</param>
        /// <param name="timeMs">目标毫秒数。</param>
        /// <returns>Wwise 是否接受操作。</returns>
        public bool Seek(long playID, int timeMs)
        {
            if (!IsReady || timeMs < 0 || !m_playbacks.TryGetValue(playID, out NativePlayback playback))
                return false;
            return AkUnitySoundEngine.SeekOnEvent(playback.EventKey, AkUnitySoundEngine.AK_INVALID_GAME_OBJECT,
                timeMs, false, playback.PlayingID) == AKRESULT.AK_Success;
        }

        /// <summary>读取当前 Wwise 事件实例的播放位置。</summary>
        /// <param name="playID">框架播放标识。</param>
        /// <param name="timeMs">返回的毫秒位置。</param>
        /// <returns>Wwise 是否返回有效位置。</returns>
        public bool TryGetPosition(long playID, out int timeMs)
        {
            timeMs = 0;
            if (!IsReady || !m_playbacks.TryGetValue(playID, out NativePlayback playback)) return false;
            return AkUnitySoundEngine.GetSourcePlayPosition(playback.PlayingID, out timeMs, true) ==
                AKRESULT.AK_Success;
        }
    }
}
