using System.Collections.Generic;
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
#if UNITY_EDITOR
        private readonly List<VolumeProbe> m_volumeProbes = new List<VolumeProbe>(); // 延迟读取原生音量参数。
#endif

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
            float decibels = ToDecibels(volume);
            AKRESULT result = AkUnitySoundEngine.SetRTPCValue(parameter, decibels);
#if UNITY_EDITOR
            RecordVolumeProbe(parameter, volume, decibels, 0, result);
#endif
            return VolumeResult(result, parameter);
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
            float decibels = ToDecibels(volume);
            AKRESULT result = AkUnitySoundEngine.SetRTPCValueByPlayingID(
                INSTANCEVOLUMEPARAMETER, decibels, playback.PlayingID);
#if UNITY_EDITOR
            RecordVolumeProbe(INSTANCEVOLUMEPARAMETER, volume, decibels, playback.PlayingID, result);
#endif
            return VolumeResult(result, INSTANCEVOLUMEPARAMETER);
        }

#if UNITY_EDITOR
        private void RecordVolumeProbe(
            string parameter, float volume, float decibels, uint playingID, AKRESULT result)
        {
            // 保留提交值和原生结果，区分业务传参、换算与参数生效问题。
            Debug.Log($"[音量检查][提交] 参数={parameter}，输入={volume:R}，目标dB={decibels:R}，"
                + $"PlayingID={playingID}，结果={result}");
            if (result != AKRESULT.AK_Success) { return; }
            m_volumeProbes.Add(new VolumeProbe(parameter, decibels, playingID, Time.frameCount + 2));
        }

        private void PollVolumeProbes()
        {
            // RTPC 查询不等待音频线程；延后两帧读取，避免把提交后的旧值误认为设置失败。
            for (int index = m_volumeProbes.Count - 1; index >= 0; index--)
            {
                VolumeProbe probe = m_volumeProbes[index];
                if (Time.frameCount < probe.ReadFrame) { continue; }
                int valueType = probe.PlayingID == 0
                    ? (int)AkQueryRTPCValue.RTPCValue_Global : (int)AkQueryRTPCValue.RTPCValue_PlayingID;
                AKRESULT result = AkUnitySoundEngine.GetRTPCValue(
                    probe.Parameter, AkUnitySoundEngine.AK_INVALID_GAME_OBJECT, probe.PlayingID,
                    out float actualValue, ref valueType);
                Debug.Log($"[音量检查][读取] 参数={probe.Parameter}，目标dB={probe.Decibels:R}，"
                    + $"实际RTPC={actualValue:R}，来源={(AkQueryRTPCValue)valueType}，"
                    + $"PlayingID={probe.PlayingID}，结果={result}（短音效结束或音频线程延迟会影响读取）");
                m_volumeProbes.RemoveAt(index);
            }
        }

        private readonly struct VolumeProbe
        {
            internal readonly string Parameter;
            internal readonly float Decibels;
            internal readonly uint PlayingID;
            internal readonly int ReadFrame;

            internal VolumeProbe(string parameter, float decibels, uint playingID, int readFrame)
            {
                Parameter = parameter;
                Decibels = decibels;
                PlayingID = playingID;
                ReadFrame = readFrame;
            }
        }
#endif

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
