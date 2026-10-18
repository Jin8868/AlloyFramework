using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>根据共享配置创建加载器，再从准备好的业务程序集中创建入口。</summary>
    internal sealed class LoadGameEntryStep : IFrameworkStartupStep
    {
        /// <summary>当前启动步骤的日志名称。</summary>
        public string Name => "加载业务入口";

        /// <summary>当前步骤占用一个框架 Loading 步骤。</summary>
        public int StepCount => 1;

        /// <summary>加载并校验业务入口，保存到本轮启动上下文。</summary>
        /// <param name="context">本轮启动上下文。</param>
        /// <param name="cancellationToken">用于取消入口加载的令牌。</param>
        /// <returns>业务入口加载完成的任务。</returns>
        /// <exception cref="InvalidOperationException">返回的业务入口为空。</exception>
        public async UniTask ExecuteAsync(FrameworkStartupContext context, CancellationToken cancellationToken)
        {
            StartupLoading.BeginFrameworkStep(EStartupLoadingStage.PreparingGameCode, Name);
            // 两种代码模式的入口加载器都读取前面步骤提交的同一准备结果。
            var gameEntryLoader = (IGameEntryLoader)Activator.CreateInstance(
                context.Configuration.GameEntryLoaderType,
                context.CodePreparationResult);
            context.GameEntry = await gameEntryLoader.LoadAsync(context.Services, cancellationToken);
            if (context.GameEntry == null)
            {
                throw new InvalidOperationException("业务入口加载器未返回有效的游戏入口。");
            }
            cancellationToken.ThrowIfCancellationRequested();
            StartupLoading.CompleteFrameworkStep();
        }
    }
}
