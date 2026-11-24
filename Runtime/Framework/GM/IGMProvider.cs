namespace AlloyFramework.GM
{
    /// <summary>业务入口可通过此契约启用 GM 并注册命令来源。</summary>
    public interface IGMProvider
    {
        /// <summary>当前运行是否启用 GM。</summary>
        bool IsGMEnabled { get; }

        /// <summary>为本轮游戏注册 GM 命令来源。</summary>
        /// <param name="registry">扫描命令声明并绑定回调的注册器。</param>
        void RegisterCommands(GMRegistry registry);
    }
}
