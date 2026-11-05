using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>显式读取主包配置，在资源系统启动前确定资源模式和启动实现。</summary>
    internal sealed class ConfigureGameStartupStep : IFrameworkStartupStep
    {
        private readonly IGameStartupConfigurationProvider m_configurationProvider; // 本步骤唯一消费的配置来源。

        internal ConfigureGameStartupStep(IGameStartupConfigurationProvider configurationProvider)
        {
            m_configurationProvider = configurationProvider ??
                throw new ArgumentNullException(nameof(configurationProvider));
        }

        /// <summary>读取配置契约，不提前创建代码准备服务和入口加载器。</summary>
        /// <param name="context">保存后续步骤共享的配置和准备结果。</param>
        /// <param name="cancellationToken">用于取消启动的令牌。</param>
        /// <returns>启动配置完成的任务。</returns>
        /// <exception cref="InvalidOperationException">配置缺失、契约不匹配或未返回有效配置。</exception>
        public UniTask ExecuteAsync(FrameworkStartupContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartupLoading.BeginFrameworkStep(EStartupLoadingStage.InitializingSystems, "读取主包启动配置");
            // 资源模式必须先确定，后续 Resource 初始化才能使用正确的本地或远端资源。
            context.Configuration = m_configurationProvider.CreateStartupConfiguration();
            if (context.Configuration == null)
            {
                throw new InvalidOperationException("主包配置未返回有效的框架启动配置。");
            }

            cancellationToken.ThrowIfCancellationRequested();
            StartupLoading.CompleteFrameworkStep();
            return UniTask.CompletedTask;
        }

        /// <summary>释放本步骤持有的状态和资源，可处理执行中途失败的情况。</summary>
        /// <param name="context">本轮启动上下文。</param>
        /// <returns>本步骤退出清理完成的任务。</returns>
        public UniTask ShutdownAsync(FrameworkStartupContext context)
        {
            context.Configuration = null;
            return UniTask.CompletedTask;
        }
    }
}
