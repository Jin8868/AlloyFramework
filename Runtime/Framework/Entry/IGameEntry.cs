using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>框架统一调用的业务初始化、游戏入口和退出清理契约。</summary>
    public interface IGameEntry
    {
        /// <summary>安装业务模块；成功返回后框架才会调用 MainAsync。</summary>
        /// <param name="loadingSession">共享展示会话，业务模块可报告当前初始化操作，不关闭展示。</param>
        /// <param name="cancellationToken">用于取消初始化的令牌。</param>
        /// <returns>全部业务模块就绪的任务。</returns>
        UniTask InitializeAsync(IStartupLoadingSession loadingSession, CancellationToken cancellationToken);

        /// <summary>进入游戏并等待首个业务界面就绪；框架将整个入口计为一步并在返回后关闭 Loading。</summary>
        /// <param name="loadingSession">框架已经开始入口步骤的会话；入口仅报告内部进度，不开始、完成或关闭步骤。</param>
        /// <param name="cancellationToken">用于取消进入游戏的令牌。</param>
        /// <returns>游戏入口执行完成的任务。</returns>
        UniTask MainAsync(IStartupLoadingSession loadingSession, CancellationToken cancellationToken);

        /// <summary>先释放界面和场景，再逆序退出业务模块；初始化中途失败时也会调用。</summary>
        /// <returns>游戏入口退出清理完成的任务。</returns>
        UniTask ShutdownAsync();
    }
}
