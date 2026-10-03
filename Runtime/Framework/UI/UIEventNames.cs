namespace AlloyFramework.UI
{
    /// <summary>
    /// UI 生命周期消息名称。每种消息都携带对应界面的 UIName。
    /// </summary>
    public static class UIEventNames
    {
        /// <summary>界面完成数据初始化，即将开始播放打开动画。</summary>
        public const string Opening = "AlloyFramework.UI.Opening";

        /// <summary>界面打开动画和打开生命周期执行完成，可以正常交互。</summary>
        public const string Opened = "AlloyFramework.UI.Opened";

        /// <summary>界面即将开始执行关闭生命周期和关闭动画。</summary>
        public const string Closing = "AlloyFramework.UI.Closing";

        /// <summary>界面关闭动画执行完成，已经隐藏或即将销毁。</summary>
        public const string Closed = "AlloyFramework.UI.Closed";

        /// <summary>界面实例及其 Controller 已经释放。</summary>
        public const string Destroyed = "AlloyFramework.UI.Destroyed";
    }
}