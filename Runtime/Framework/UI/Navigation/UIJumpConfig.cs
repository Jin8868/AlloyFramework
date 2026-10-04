namespace AlloyFramework.UI
{
    public enum EUIJumpMode { Overlay, Push, Replace }

    public enum EUIBackMode { ReturnToSource, InheritSource, Explicit, Disabled }

    public sealed class UIJumpConfig
    {
        /// <summary>跳转配置的唯一标识。</summary>
        public int JumpID { get; }
        /// <summary>目标界面的稳定名称。</summary>
        public string TargetUIName { get; }
        /// <summary>目标界面的打开方式。</summary>
        public EUIJumpMode JumpMode { get; }
        /// <summary>目标界面的返回方式。</summary>
        public EUIBackMode BackMode { get; }
        /// <summary>显式返回使用的跳转配置标识。</summary>
        public int BackJumpID { get; }

        /// <summary>
        /// 创建一条与具体配置工具无关的 UI 跳转配置。
        /// </summary>
        /// <param name="jumpID">跳转配置的唯一标识。</param>
        /// <param name="targetUIName">目标界面的稳定名称。</param>
        /// <param name="jumpMode">目标界面的打开方式。</param>
        /// <param name="backMode">目标界面的返回方式。</param>
        /// <param name="backJumpID">显式返回使用的跳转配置标识。</param>
        public UIJumpConfig(
            int jumpID,
            string targetUIName,
            EUIJumpMode jumpMode,
            EUIBackMode backMode,
            int backJumpID)
        {
            JumpID = jumpID;
            TargetUIName = targetUIName;
            JumpMode = jumpMode;
            BackMode = backMode;
            BackJumpID = backJumpID;
        }
    }
}
