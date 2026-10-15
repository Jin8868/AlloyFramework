namespace AlloyFramework.Audio.Wwise
{
    public sealed partial class WwiseAudioBackend : IAudioTimelineBackend
    {
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
