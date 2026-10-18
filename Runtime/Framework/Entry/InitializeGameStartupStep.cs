using System;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>在全部框架系统就绪后反射创建并执行业务初始化器。</summary>
    internal sealed class InitializeGameStartupStep : IFrameworkStartupStep
    {
        /// <summary>当前启动步骤的日志名称。</summary>
        public string Name => "初始化业务功能";

        /// <summary>业务使用独立进度，不再累计框架 Loading 步骤。</summary>
        public int StepCount => 0;

        /// <summary>交接同一份 Loading 并等待业务初始化全部完成。</summary>
        /// <param name="context">本轮启动上下文。</param>
        /// <param name="cancellationToken">用于取消业务初始化的令牌。</param>
        /// <returns>业务初始化完成的任务。</returns>
        /// <exception cref="InvalidOperationException">启动状态无效、重复初始化或业务步骤数为负数。</exception>
        /// <exception cref="OverflowException">业务声明的步骤总数超出整数范围。</exception>
        public async UniTask ExecuteAsync(FrameworkStartupContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.IsInitialized || context.GameEntry == null || context.LoadingSession != null)
            {
                throw new InvalidOperationException("业务初始化需要框架系统和入口就绪，且不能重复执行。");
            }

            // 业务初始化器在此时才创建，准备代码时不再提前注册或执行。
            var result = context.CodePreparationResult;
            if (!result.IsCompleted || result.RuntimeAssembly == null)
            {
                throw new InvalidOperationException("业务代码尚未准备完成，不能创建业务初始化器。");
            }

            context.GameStartup = CreateGameStartup(result.RuntimeAssembly);

            // 业务类型加载后才能确定总数；初始化和入口共用一次 Loading 接管。
            int startupStepCount = context.GameStartup?.StepCount ?? 0;
            int entryStepCount = context.GameEntry.StepCount;
            if (startupStepCount < 0 || entryStepCount < 0)
            {
                throw new InvalidOperationException("业务声明的 Loading 步骤数不能为负数。");
            }

            int stepCount = checked(startupStepCount + entryStepCount);

            // 完成状态至少展示一帧，再由业务初始化和入口共享后续进度。
            StartupLoading.HandOver();
            await UniTask.NextFrame(cancellationToken);
            context.LoadingSession = StartupLoading.TakeOver(Math.Max(1, stepCount));
            AlloyDebug.Log("[GameStartup] 业务已接管统一 Loading。");
            if (context.GameStartup != null)
            {
                // 调用前记录启动状态，初始化中途失败时管线也会清理业务功能。
                context.IsGameStartupStarted = true;
                await context.GameStartup.InitializeAsync(context.LoadingSession, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }

            context.IsGameStartupCompleted = true;
        }
        private static IGameStartup CreateGameStartup(Assembly runtimeAssembly)
        {
            Type startupType = null;
            foreach (Type candidateType in runtimeAssembly.GetTypes())
            {
                if (candidateType.IsAbstract || candidateType.ContainsGenericParameters ||
                    !typeof(IGameStartup).IsAssignableFrom(candidateType))
                {
                    continue;
                }

                if (startupType != null)
                {
                    throw new InvalidOperationException("业务程序集包含多个 IGameStartup 实现，无法确定初始化器。");
                }

                startupType = candidateType;
            }

            // 无初始化器的项目可直接进入游戏；允许业务程序集中的内部无参构造函数。
            return startupType == null ? null : (IGameStartup)Activator.CreateInstance(startupType, true);
        }
    }
}
