using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>业务代码安装时注册、框架系统全部就绪后执行的初始化契约。</summary>
    public interface IGameStartup
    {
        /// <summary>业务初始化占用的真实 Loading 步骤数，不包含游戏入口步骤。</summary>
        int StepCount { get; }

        /// <summary>初始化业务基础设施，完成后才允许执行业务入口。</summary>
        /// <param name="loadingSession">与业务入口共享的 Loading 会话。</param>
        /// <param name="cancellationToken">用于取消初始化的令牌。</param>
        /// <returns>业务初始化完成的任务。</returns>
        UniTask InitializeAsync(IStartupLoadingSession loadingSession, CancellationToken cancellationToken);

        /// <summary>释放已经开始初始化的业务功能；初始化中途失败时也会调用。</summary>
        /// <returns>业务初始化资源清理完成的任务。</returns>
        UniTask ShutdownAsync();
    }
}
