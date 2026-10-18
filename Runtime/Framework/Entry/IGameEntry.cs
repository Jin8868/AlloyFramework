using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>业务初始化完成后执行的游戏入口及其退出清理契约。</summary>
    public interface IGameEntry
    {
        /// <summary>游戏入口占用的真实 Loading 步骤数，不包含业务初始化步骤。</summary>
        int StepCount { get; }

        /// <summary>进入游戏，待首个业务界面就绪后关闭共享 Loading。</summary>
        /// <param name="loadingSession">框架已经交接、业务初始化使用过的同一 Loading 会话。</param>
        /// <param name="cancellationToken">用于取消进入游戏的令牌。</param>
        /// <returns>游戏入口执行完成的任务。</returns>
        UniTask MainAsync(IStartupLoadingSession loadingSession, CancellationToken cancellationToken);

        /// <summary>释放游戏入口持有的场景和其他业务资源。</summary>
        /// <returns>游戏入口退出清理完成的任务。</returns>
        UniTask ShutdownAsync();
    }
}
