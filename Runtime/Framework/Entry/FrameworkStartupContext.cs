using System.Collections.Generic;

namespace AlloyFramework
{
    /// <summary>仅保存启动步骤之间传递、以及退出清理需要读取的共享状态。</summary>
    internal sealed class FrameworkStartupContext
    {
        private readonly FrameworkContext m_services = new FrameworkContext(); // 本轮步骤共享的框架服务容器。
        private readonly List<FrameworkSystem> m_initializedSystems = new List<FrameworkSystem>(); // 已取得清理责任的系统。

        private readonly GameCodePreparationResult m_codePreparationResult =
            new GameCodePreparationResult(); // 代码和入口共享结果。

        internal GameCodePreparationResult CodePreparationResult => m_codePreparationResult;
        internal FrameworkContext Services => m_services;
        internal List<FrameworkSystem> InitializedSystems => m_initializedSystems;
        internal IGameStartup GameStartup { get; set; }
        internal IGameEntry GameEntry { get; set; }
        internal IStartupLoadingSession LoadingSession { get; set; }
        internal GameStartupConfiguration Configuration { get; set; }
        internal bool IsInitialized { get; set; }
        internal bool IsGameStartupStarted { get; set; }
        internal bool IsGameStartupCompleted { get; set; }
        internal bool IsGameEntryStarted { get; set; }
    }
}
