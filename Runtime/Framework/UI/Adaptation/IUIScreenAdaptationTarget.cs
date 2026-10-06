namespace AlloyFramework.UI
{
    /// <summary>
    /// 接收全局屏幕适配快照并更新自身布局。
    /// </summary>
    public interface IUIScreenAdaptationTarget
    {
        /// <summary>
        /// 应用最新的屏幕适配快照。
        /// </summary>
        /// <param name="snapshot">不可变的当前屏幕适配数据。</param>
        void ApplyScreenAdaptation(UIScreenAdaptationSnapshot snapshot);
    }
}
