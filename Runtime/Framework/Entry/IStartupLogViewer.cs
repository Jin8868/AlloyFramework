namespace AlloyFramework
{
    /// <summary>主包日志适配契约，只提供显示能力，不参与启动流程编排。</summary>
    public interface IStartupLogViewer
    {
        /// <summary>日志窗口当前是否可见。</summary>
        bool IsVisible { get; }

        /// <summary>打开日志窗口。</summary>
        void Show();
    }
}
