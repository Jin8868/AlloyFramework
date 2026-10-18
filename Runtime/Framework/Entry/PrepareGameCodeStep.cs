using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>在资源就绪后、业务配置加载前准备业务代码。</summary>
    internal sealed class PrepareGameCodeStep : IFrameworkStartupStep
    {
        /// <summary>当前启动步骤的日志名称。</summary>
        public string Name => "准备业务代码";

        /// <summary>当前步骤占用一个框架 Loading 步骤。</summary>
        public int StepCount => 1;

        /// <summary>根据共享配置创建代码准备服务，并执行业务代码安装入口。</summary>
        /// <param name="context">本轮启动上下文。</param>
        /// <param name="cancellationToken">用于取消代码准备的令牌。</param>
        /// <returns>业务代码准备完成的任务。</returns>
        public async UniTask ExecuteAsync(FrameworkStartupContext context, CancellationToken cancellationToken)
        {
            StartupLoading.BeginFrameworkStep(EStartupLoadingStage.PreparingGameCode, Name);

            // 实现类型来自主包配置，实例仅在当前步骤执行时创建。
            var configuration = context.Configuration;
            var codePreparation = (IGameCodePreparation)Activator.CreateInstance(
                configuration.CodePreparationType,
                context.CodePreparationResult,
                configuration.RuntimeAssemblyName,
                configuration.InstallerTypeName);
            await codePreparation.PrepareAsync(context.Services, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            StartupLoading.CompleteFrameworkStep();
        }
    }
}
