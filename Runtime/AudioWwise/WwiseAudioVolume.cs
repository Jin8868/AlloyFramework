using UnityEngine;

namespace AlloyFramework.Audio.Wwise
{
    public sealed partial class WwiseAudioBackend
    {
        private const float MINIMUMDECIBELS = -200; // 零音量对应的原生衰减下限。
        private const string MASTERVOLUMEPARAMETER = "AlloyMasterVolume"; // 主总线参数。
        private const string BGMVOLUMEPARAMETER = "AlloyBGMVolume"; // 背景音乐总线参数。
        private const string SFXVOLUMEPARAMETER = "AlloySFXVolume"; // 音效总线参数。
        private const string INSTANCEVOLUMEPARAMETER = "AlloyInstanceVolume"; // 单次播放声部音量参数。

        /// <summary>将归一化分类音量转换为 Wwise 总线的分贝参数。</summary>
        /// <param name="channel">音量分类。</param>
        /// <param name="volume">0～1 的线性音量。</param>
        /// <returns>后端结果，失败信息包含参数名称。</returns>
        public AudioOperationResult SetChannelVolume(EAudioVolumeChannel channel, float volume)
        {
            if (!IsReady) { return Error("Wwise 尚未就绪，无法设置分类音量。"); }
            if (!IsValidVolume(volume)) { return Error("音量必须是 0～1 的有限值。"); }
            string parameter;
            switch (channel)
            {
                case EAudioVolumeChannel.Master: parameter = MASTERVOLUMEPARAMETER; break;
                case EAudioVolumeChannel.BGM: parameter = BGMVOLUMEPARAMETER; break;
                case EAudioVolumeChannel.SFX: parameter = SFXVOLUMEPARAMETER; break;
                default: return Error("音量分类无效。");
            }
            return VolumeResult(AkUnitySoundEngine.SetRTPCValue(parameter, ToDecibels(volume)), parameter);
        }

        /// <summary>按原生 PlayingID 设置单次声部音量，不影响同对象的其他播放。</summary>
        /// <param name="playID">框架播放标识。</param>
        /// <param name="volume">0～1 的线性音量。</param>
        /// <returns>后端操作结果。</returns>
        public AudioOperationResult SetPlaybackVolume(long playID, float volume)
        {
            if (!IsReady) { return Error("Wwise 尚未就绪，无法设置单次播放音量。"); }
            if (!IsValidVolume(volume)) { return Error("音量必须是 0～1 的有限值。"); }
            if (!m_playbacks.TryGetValue(playID, out NativePlayback playback))
            { return Error($"播放标识已失效：{playID}"); }
            return VolumeResult(AkUnitySoundEngine.SetRTPCValueByPlayingID(
                INSTANCEVOLUMEPARAMETER, ToDecibels(volume), playback.PlayingID), INSTANCEVOLUMEPARAMETER);
        }

        private static bool IsValidVolume(float volume)
        { return !float.IsNaN(volume) && !float.IsInfinity(volume) && volume >= 0 && volume <= 1; }

        private static float ToDecibels(float volume)
        { return volume <= 0 ? MINIMUMDECIBELS : Mathf.Max(MINIMUMDECIBELS, 20 * Mathf.Log10(volume)); }

        private static AudioOperationResult VolumeResult(AKRESULT result, string parameter)
        {
            return result == AKRESULT.AK_Success ? new AudioOperationResult()
                : Error($"音量参数 {parameter} 设置失败：{result}。请重新加载 Wwise 工程并生成 SoundBank。");
        }
    }
}
