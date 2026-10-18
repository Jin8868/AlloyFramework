using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>在所有系统就绪后发布框架初始化完成通知。</summary>
    internal sealed class CompleteFrameworkInitializationStep : IFrameworkStartupStep
    {
        /// <summary>当前启动步骤的日志名称。</summary>
        public string Name => "完成框架初始化回调";

        /// <summary>当前步骤占用一个框架 Loading 步骤。</summary>
        public int StepCount => 1;

        /// <summary>设置就绪状态并触发初始化完成回调。</summary>
        /// <param name="context">本轮启动上下文。</param>
        /// <param name="cancellationToken">用于取消启动的令牌。</param>
        /// <returns>初始化完成通知任务。</returns>
        public UniTask ExecuteAsync(FrameworkStartupContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartupLoading.BeginFrameworkStep(EStartupLoadingStage.InitializingSystems, Name);
            // 回调读取到的框架状态必须已经就绪。
            context.IsInitialized = true;
            FrameworkBootstrap.HandleInitialized();
            cancellationToken.ThrowIfCancellationRequested();
            StartupLoading.CompleteFrameworkStep();
            return UniTask.CompletedTask;
        }
    }
}
