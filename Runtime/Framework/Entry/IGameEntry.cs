using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>业务初始化完成后执行的游戏入口及其退出清理契约。</summary>
    public interface IGameEntry
    {
        /// <summary>进入游戏并等待首个业务界面就绪；框架将整个入口计为一步并在返回后关闭 Loading。</summary>
        /// <param name="loadingSession">框架已经开始入口步骤的会话；入口仅报告内部进度，不开始、完成或关闭步骤。</param>
        /// <param name="cancellationToken">用于取消进入游戏的令牌。</param>
        /// <returns>游戏入口执行完成的任务。</returns>
        UniTask MainAsync(IStartupLoadingSession loadingSession, CancellationToken cancellationToken);

        /// <summary>释放游戏入口持有的场景和其他业务资源。</summary>
        /// <returns>游戏入口退出清理完成的任务。</returns>
        UniTask ShutdownAsync();
    }
}
