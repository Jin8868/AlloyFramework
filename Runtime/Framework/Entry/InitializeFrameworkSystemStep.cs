using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>初始化一个显式注册的框架系统。</summary>
    internal sealed class InitializeFrameworkSystemStep : IFrameworkStartupStep
    {
        private readonly FrameworkSystem m_system; // 当前步骤负责初始化的系统。
        private bool m_requiresShutdown; // 已取得系统清理责任，包含初始化中的状态。

        internal InitializeFrameworkSystemStep(FrameworkSystem system)
        {
            m_system = system ?? throw new ArgumentNullException(nameof(system));
        }

        /// <summary>初始化系统，注册服务并记录清理责任。</summary>
        /// <param name="context">本轮启动上下文。</param>
        /// <param name="cancellationToken">用于取消初始化的令牌。</param>
        /// <returns>系统初始化完成的任务。</returns>
        public async UniTask ExecuteAsync(FrameworkStartupContext context, CancellationToken cancellationToken)
        {
            StartupLoading.BeginFrameworkStep(
                EStartupLoadingStage.InitializingSystems,
                "初始化 " + m_system.GetType().Name);
            cancellationToken.ThrowIfCancellationRequested();
            // 调用前记录资源责任，初始化中途失败也由本步骤清理。
            m_requiresShutdown = true;
            await m_system.InitializeAsync(context.Services, cancellationToken);

            // 只有初始化成功的系统才能进入服务容器。
            context.Services.Register(m_system);
            cancellationToken.ThrowIfCancellationRequested();
            StartupLoading.CompleteFrameworkStep();
        }

        /// <summary>释放本步骤持有的状态和资源，可处理执行中途失败的情况。</summary>
        /// <param name="context">本轮启动上下文。</param>
        /// <returns>本步骤退出清理完成的任务。</returns>
        public async UniTask ShutdownAsync(FrameworkStartupContext context)
        {
            if (!m_requiresShutdown) return;

            // 保留同步退出所需的记录，异步清理结束后再解除责任。
            try
            {
                await m_system.ShutdownAsync();
            }
            finally
            {
                m_requiresShutdown = false;
            }
        }

        internal void ShutdownImmediately()
        {
            if (!m_requiresShutdown) return;

            // 应用退出时无法等待异步释放，先解除责任再执行同步兜底。
            m_requiresShutdown = false;
            m_system.Shutdown();
        }
    }
}
