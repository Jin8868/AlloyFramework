using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>初始化一个显式注册的框架系统。</summary>
    internal sealed class InitializeFrameworkSystemStep : IFrameworkStartupStep
    {
        private readonly FrameworkSystem m_system; // 当前步骤负责初始化的系统。

        /// <summary>当前启动步骤的日志名称。</summary>
        public string Name => "初始化 " + m_system.GetType().Name;

        /// <summary>当前步骤占用一个框架 Loading 步骤。</summary>
        public int StepCount => 1;

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
            StartupLoading.BeginFrameworkStep(EStartupLoadingStage.InitializingSystems, Name);
            cancellationToken.ThrowIfCancellationRequested();
            await m_system.InitializeAsync(context.Services, cancellationToken);

            // 初始化成功后先记录清理责任，后续服务注册失败也能释放当前系统。
            context.InitializedSystems.Add(m_system);
            context.Services.Register(m_system);
            cancellationToken.ThrowIfCancellationRequested();
            StartupLoading.CompleteFrameworkStep();
        }
    }
}
