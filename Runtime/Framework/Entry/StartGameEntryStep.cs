using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>业务初始化完成后创建并执行游戏入口，统一管理入口生命周期。</summary>
    internal sealed class StartGameEntryStep : IFrameworkStartupStep
    {
        /// <summary>创建并执行唯一游戏入口，整个过程共用一个业务 Loading 步骤。</summary>
        /// <param name="context">本轮启动上下文。</param>
        /// <param name="cancellationToken">用于取消业务启动的令牌。</param>
        /// <returns>游戏入口执行完成的任务。</returns>
        /// <exception cref="InvalidOperationException">业务初始化尚未完成、入口缺失或重复执行。</exception>
        public async UniTask ExecuteAsync(FrameworkStartupContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.IsGameStartupCompleted || context.GameEntry != null ||
                context.LoadingSession == null || context.IsGameEntryStarted)
            {
                throw new InvalidOperationException("业务入口需要初始化完成且不能重复执行。");
            }

            // 整个入口固定作为一步，业务返回且未取消后才完成并关闭共享展示。
            var loadingSession = context.LoadingSession;
            loadingSession.BeginStep(EStartupLoadingStage.LoadingScene, "进入游戏");

            // 消费已准备的业务程序集，创建成功后立即交给本步骤负责退出清理。
            var gameEntryLoader = new PreparedGameEntryLoader(context.CodePreparationResult);
            context.GameEntry = await gameEntryLoader.LoadAsync(context.Services, cancellationToken);
            if (context.GameEntry == null)
            {
                throw new InvalidOperationException("业务入口加载器未返回有效的游戏入口。");
            }
            cancellationToken.ThrowIfCancellationRequested();

            // 调用前标记已开始，MainAsync 中途失败也能通过逆序退出释放资源。
            context.IsGameEntryStarted = true;
            await context.GameEntry.MainAsync(loadingSession, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            loadingSession.CompleteStep();
            loadingSession.Dispose();
            context.LoadingSession = null;
        }

        /// <summary>释放本步骤持有的状态和资源，可处理执行中途失败的情况。</summary>
        /// <param name="context">本轮启动上下文。</param>
        /// <returns>本步骤退出清理完成的任务。</returns>
        public async UniTask ShutdownAsync(FrameworkStartupContext context)
        {
            // 创建后即使尚未进入 MainAsync，也统一清理；先解除引用以防重复退出。
            var gameEntry = context.GameEntry;
            context.GameEntry = null;
            context.IsGameEntryStarted = false;
            if (gameEntry != null)
            {
                await gameEntry.ShutdownAsync();
            }
        }
    }
}
