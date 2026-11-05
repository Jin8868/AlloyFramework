using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>固定执行业务入口的创建、初始化、进入游戏和退出清理。</summary>
    internal sealed class StartGameEntryStep : IFrameworkStartupStep
    {
        private IGameEntry m_gameEntry; // 本步骤创建并负责退出的业务入口。
        private IStartupLoadingSession m_loadingSession; // 本步骤交接并负责释放的展示会话。
        private bool m_hasExecuted; // 同一步骤实例只允许执行一次。

        /// <summary>创建唯一入口，等待业务初始化完成后进入游戏。</summary>
        /// <param name="context">跨步骤共享的服务、配置和代码准备结果。</param>
        /// <param name="cancellationToken">用于取消本轮启动的令牌。</param>
        /// <returns>首页就绪且启动展示关闭的任务。</returns>
        /// <exception cref="InvalidOperationException">框架尚未就绪、重复执行或入口创建失败。</exception>
        public async UniTask ExecuteAsync(FrameworkStartupContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.IsInitialized || m_hasExecuted)
            {
                throw new InvalidOperationException("游戏入口需要框架系统就绪，且不能重复执行。");
            }
            m_hasExecuted = true;

            // 先创建入口并取得清理责任，初始化失败时也调用同一入口的退出方法。
            StartupLoading.BeginFrameworkStep(EStartupLoadingStage.PreparingGameCode, "创建游戏入口");
            var loader = new PreparedGameEntryLoader(context.CodePreparationResult);
            m_gameEntry = await loader.LoadAsync(context.Services, cancellationToken);
            if (m_gameEntry == null)
            {
                throw new InvalidOperationException("业务入口加载器未返回有效的游戏入口。");
            }
            cancellationToken.ThrowIfCancellationRequested();
            StartupLoading.CompleteFrameworkStep();

            // 初始化模块可以自行报告当前操作，框架固定控制初始化与 Main 的先后关系。
            StartupLoading.HandOver();
            await UniTask.NextFrame(cancellationToken);
            m_loadingSession = StartupLoading.TakeOver();
            await m_gameEntry.InitializeAsync(m_loadingSession, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // 整个 Main 作为一个操作，首页就绪并返回后才关闭展示。
            m_loadingSession.BeginStep(EStartupLoadingStage.LoadingScene, "进入游戏");
            await m_gameEntry.MainAsync(m_loadingSession, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            m_loadingSession.CompleteStep();
            m_loadingSession.Dispose();
            m_loadingSession = null;
        }

        /// <summary>退出已创建的入口，并确保失败或取消后释放展示会话。</summary>
        /// <param name="context">本轮启动上下文。</param>
        /// <returns>业务资源及展示会话清理完成的任务。</returns>
        public async UniTask ShutdownAsync(FrameworkStartupContext context)
        {
            var gameEntry = m_gameEntry;
            m_gameEntry = null;
            try
            {
                if (gameEntry != null) await gameEntry.ShutdownAsync();
            }
            finally
            {
                // 业务退出异常也不能遗留展示会话，失败面板由展示层按既定规则保留。
                var loadingSession = m_loadingSession;
                m_loadingSession = null;
                loadingSession?.Dispose();
            }
        }
    }
}
