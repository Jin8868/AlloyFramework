namespace AlloyFramework.UI
{
    /// <summary>
    /// 全屏背景在可用区域内的显示策略。
    /// </summary>
    public enum EUIFullScreenBackgroundMode
    {
        /// <summary>
        /// 保持比例并裁切超出区域的部分。
        /// </summary>
        Cover,

        /// <summary>
        /// 保持比例完整显示，未覆盖区域由父节点承担。
        /// </summary>
        Contain,

        /// <summary>
        /// 拉伸到完整父区域。
        /// </summary>
        Stretch,

        /// <summary>
        /// 不修改业务自定义布局。
        /// </summary>
        Custom
    }
}
