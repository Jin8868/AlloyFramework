using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>框架启动管线中的单个执行步骤。</summary>
    internal interface IFrameworkStartupStep
    {
        /// <summary>执行当前框架启动步骤。</summary>
        /// <param name="context">本轮启动上下文。</param>
        /// <param name="cancellationToken">用于取消启动的令牌。</param>
        /// <returns>当前步骤执行完成的任务。</returns>
        UniTask ExecuteAsync(FrameworkStartupContext context, CancellationToken cancellationToken);

        /// <summary>逆序退出本步骤；执行中途失败时也应安全清理，不使用启动取消令牌。</summary>
        /// <param name="context">本轮启动上下文。</param>
        /// <returns>本步骤退出清理完成的任务。</returns>
        UniTask ShutdownAsync(FrameworkStartupContext context);
    }
}
