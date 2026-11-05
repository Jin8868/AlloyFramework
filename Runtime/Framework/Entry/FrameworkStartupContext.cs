namespace AlloyFramework
{
    /// <summary>仅保存跨步骤传递的数据和外部需要读取的共享状态。</summary>
    internal sealed class FrameworkStartupContext
    {
        private readonly FrameworkContext m_services = new FrameworkContext(); // 本轮步骤共享的框架服务容器。

        private readonly GameCodePreparationResult m_codePreparationResult =
            new GameCodePreparationResult(); // 代码和入口共享结果。

        internal GameCodePreparationResult CodePreparationResult => m_codePreparationResult;
        internal FrameworkContext Services => m_services;
        internal GameStartupConfiguration Configuration { get; set; }
        internal bool IsInitialized { get; set; }
    }
}
