using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlloyFramework
{
    /// <summary>显式读取主包配置，在资源系统启动前确定资源模式和启动实现。</summary>
    internal sealed class ConfigureGameStartupStep : IFrameworkStartupStep
    {
        private const string CONFIGURATIONLOCATION = "GameHotUpdateSettings"; // 约定的主包 Resources 启动配置地址。

        /// <summary>当前启动步骤的日志名称。</summary>
        public string Name => "读取主包启动配置";

        /// <summary>启动配置占用一个框架 Loading 步骤。</summary>
        public int StepCount => 1;

        /// <summary>读取配置契约，不提前创建代码准备服务和入口加载器。</summary>
        /// <param name="context">保存后续步骤共享的配置和准备结果。</param>
        /// <param name="cancellationToken">用于取消启动的令牌。</param>
        /// <returns>启动配置完成的任务。</returns>
        /// <exception cref="InvalidOperationException">配置缺失、契约不匹配或未返回有效配置。</exception>
        public UniTask ExecuteAsync(FrameworkStartupContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartupLoading.BeginFrameworkStep(EStartupLoadingStage.InitializingSystems, Name);
            var asset = Resources.Load<ScriptableObject>(CONFIGURATIONLOCATION);
            if (!(asset is IGameStartupConfigurationProvider provider))
            {
                throw new InvalidOperationException("主包启动配置缺失或未实现 IGameStartupConfigurationProvider。");
            }

            // 资源模式必须先确定，后续 Resource 初始化才能使用正确的本地或远端资源。
            context.Configuration = provider.CreateStartupConfiguration();
            if (context.Configuration == null)
            {
                throw new InvalidOperationException("主包配置未返回有效的框架启动配置。");
            }

            cancellationToken.ThrowIfCancellationRequested();
            StartupLoading.CompleteFrameworkStep();
            return UniTask.CompletedTask;
        }
    }
}
