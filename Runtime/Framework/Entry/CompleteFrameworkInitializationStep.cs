using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>在所有系统就绪后发布框架初始化完成通知。</summary>
    internal sealed class CompleteFrameworkInitializationStep : IFrameworkStartupStep
    {
        /// <summary>设置就绪状态并触发初始化完成回调。</summary>
        /// <param name="context">本轮启动上下文。</param>
        /// <param name="cancellationToken">用于取消启动的令牌。</param>
        /// <returns>初始化完成通知任务。</returns>
        public UniTask ExecuteAsync(FrameworkStartupContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartupLoading.BeginFrameworkStep(EStartupLoadingStage.InitializingSystems, "完成框架初始化回调");
            // 回调读取到的框架状态必须已经就绪。
            context.IsInitialized = true;
            context.Services.StartupController.HandleInitialized();
            cancellationToken.ThrowIfCancellationRequested();
            StartupLoading.CompleteFrameworkStep();
            return UniTask.CompletedTask;
        }

        /// <summary>释放本步骤持有的状态和资源，可处理执行中途失败的情况。</summary>
        /// <param name="context">本轮启动上下文。</param>
        /// <returns>本步骤退出清理完成的任务。</returns>
        public UniTask ShutdownAsync(FrameworkStartupContext context)
        {
            context.IsInitialized = false;
            return UniTask.CompletedTask;
        }
    }
}
