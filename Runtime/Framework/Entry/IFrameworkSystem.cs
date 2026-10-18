using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>框架系统的初始化、正常异步退出和强制同步退出契约。</summary>
    public interface IFrameworkSystem
    {
        /// <summary>初始化当前系统，失败时由实现清理已分配的部分资源。</summary>
        /// <param name="context">本轮框架服务容器。</param>
        /// <param name="cancellationToken">用于取消初始化的令牌。</param>
        /// <returns>系统初始化完成的任务。</returns>
        UniTask InitializeAsync(FrameworkContext context, CancellationToken cancellationToken);

        /// <summary>等待当前系统退出，完成后才允许关闭其依赖的系统。</summary>
        /// <returns>当前系统清理完成的任务。</returns>
        UniTask ShutdownAsync();

        /// <summary>立即同步释放系统资源，用于无法等待异步任务的强制退出。</summary>
        void Shutdown();
    }

    /// <summary>提供默认异步退出适配，现有同步系统无需重复实现关闭逻辑。</summary>
    public abstract class FrameworkSystem : IFrameworkSystem
    {
        /// <summary>初始化当前系统，失败时由实现清理已分配的部分资源。</summary>
        /// <param name="context">本轮框架服务容器。</param>
        /// <param name="cancellationToken">用于取消初始化的令牌。</param>
        /// <returns>系统初始化完成的任务。</returns>
        public abstract UniTask InitializeAsync(FrameworkContext context, CancellationToken cancellationToken);

        /// <summary>默认调用同步关闭逻辑，需要等待资源退出的系统可覆盖此方法。</summary>
        /// <returns>当前系统清理完成的任务。</returns>
        public virtual UniTask ShutdownAsync()
        {
            Shutdown();
            return UniTask.CompletedTask;
        }

        /// <summary>立即同步释放资源，用于普通同步系统或进程强制退出。</summary>
        public virtual void Shutdown()
        {
        }
    }
}
