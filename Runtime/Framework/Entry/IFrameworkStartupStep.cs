using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>框架启动管线中的单个执行步骤。</summary>
    internal interface IFrameworkStartupStep
    {
        /// <summary>当前步骤的日志名称。</summary>
        string Name { get; }

        /// <summary>当前步骤占用的框架 Loading 步骤数。</summary>
        int StepCount { get; }

        /// <summary>执行当前框架启动步骤。</summary>
        /// <param name="context">本轮启动上下文。</param>
        /// <param name="cancellationToken">用于取消启动的令牌。</param>
        /// <returns>当前步骤执行完成的任务。</returns>
        UniTask ExecuteAsync(FrameworkStartupContext context, CancellationToken cancellationToken);
    }
}
