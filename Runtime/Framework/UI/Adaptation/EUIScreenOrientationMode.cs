namespace AlloyFramework.UI
{
    /// <summary>
    /// UI 根节点使用的屏幕方向缩放策略。
    /// </summary>
    public enum EUIScreenOrientationMode
    {
        /// <summary>
        /// 始终使用横屏缩放配置。
        /// </summary>
        FixedLandscape,

        /// <summary>
        /// 始终使用竖屏缩放配置。
        /// </summary>
        FixedPortrait,

        /// <summary>
        /// 根据当前屏幕宽高自动切换横竖屏缩放配置。
        /// </summary>
        AutoRotate
    }
}
