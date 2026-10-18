using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>业务初始化完成后执行真正的游戏入口。</summary>
    internal sealed class StartGameEntryStep : IFrameworkStartupStep
    {
        /// <summary>当前启动步骤的日志名称。</summary>
        public string Name => "执行业务入口";

        /// <summary>继续使用业务 Loading，不累计框架步骤。</summary>
        public int StepCount => 0;

        /// <summary>使用业务初始化共享的 Loading 会话启动游戏入口。</summary>
        /// <param name="context">本轮启动上下文。</param>
        /// <param name="cancellationToken">用于取消业务启动的令牌。</param>
        /// <returns>游戏入口执行完成的任务。</returns>
        /// <exception cref="InvalidOperationException">业务初始化尚未完成、入口缺失或重复执行。</exception>
        public async UniTask ExecuteAsync(FrameworkStartupContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.IsGameStartupCompleted || context.GameEntry == null || context.IsGameEntryStarted)
            {
                throw new InvalidOperationException("业务入口需要初始化完成且不能重复执行。");
            }

            // 调用前取得清理责任，入口中途失败时也会执行退出方法。
            context.IsGameEntryStarted = true;
            await context.GameEntry.MainAsync(context.LoadingSession, cancellationToken);
        }
    }
}
