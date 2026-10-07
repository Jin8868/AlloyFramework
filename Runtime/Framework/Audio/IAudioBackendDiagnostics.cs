namespace AlloyFramework.Audio
{
    public interface IAudioBackendDiagnostics
    {
        /// <summary>描述内部播放实例，仅供诊断显示。</summary>
        /// <param name="playID">框架播放标识。</param>
        /// <returns>后端内部状态文本。</returns>
        string GetPlaybackDescription(long playID);
    }
}
