using System;
using System.Collections.Generic;
using System.Threading;
using AlloyFramework.Audio;
using AlloyFramework.Net;
using AlloyFramework.Timing;
using AlloyFramework.UI;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>以显式注册顺序组织框架系统、代码准备与业务入口。</summary>
    internal sealed class FrameworkStartupPipeline
    {
        private readonly List<IFrameworkStartupStep> m_steps = new List<IFrameworkStartupStep>(); // 固定启动顺序。
        private readonly FrameworkStartupContext m_context; // 本轮生命周期和服务状态。
        private int m_startedStepCount; // 已开始且仍需逆序清理的步骤数。
        private bool m_shutdownImmediately; // 同步退出后停止异步清理续体。
        private UniTaskCompletionSource m_shutdownCompleted; // 重复退出共享同一个清理结果。
        private bool m_hasRun; // 防止重复初始化同一批系统。

        internal FrameworkStartupPipeline(
            FrameworkStartupContext context,
            IGameStartupConfigurationProvider configurationProvider)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));

            // 配置先于资源初始化；各步骤自行创建所需的配置实现。
            Add(new ConfigureGameStartupStep(configurationProvider));

            // 列表就是框架系统的初始化顺序，退出时逆序清理已经开始的步骤。
            Add(new InitializeFrameworkSystemStep(new GameLoopManager()));
            Add(new InitializeFrameworkSystemStep(new TimerManager()));
            Add(new InitializeFrameworkSystemStep(new ResourceServiceManager()));
            Add(new PrepareGameCodeStep());

            // 业务代码必须先安装配置加载器，Config 随后才能加载业务配置。
            Add(new InitializeFrameworkSystemStep(new ConfigManager()));
            Add(new InitializeFrameworkSystemStep(new ModelManager()));
            Add(new InitializeFrameworkSystemStep(new AudioLifecycleManager()));
            Add(new InitializeFrameworkSystemStep(new NetworkManager()));
            Add(new InitializeFrameworkSystemStep(new UIManager()));
            Add(new CompleteFrameworkInitializationStep());
            Add(new StartGameEntryStep());
        }

        internal async UniTask RunAsync(CancellationToken cancellationToken)
        {
            if (m_hasRun)
            {
                throw new InvalidOperationException("框架启动管线不能重复执行。");
            }

            m_hasRun = true;
            for (int index = 0; index < m_steps.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var step = m_steps[index];
                string stepName = step.GetType().Name;
                // 执行前取得清理责任，失败的当前步骤同样参与回滚。
                m_startedStepCount = index + 1;
                var startedAt = FrameworkStartupLog.Now;
                FrameworkStartupLog.Info($"开始：{stepName}");
                try
                {
                    await step.ExecuteAsync(m_context, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // 正常退出只传播取消，让控制器完成关闭，不记录步骤失败。
                    throw;
                }
                catch
                {
                    FrameworkStartupLog.Failure(stepName, startedAt);
                    throw;
                }

                FrameworkStartupLog.Step(stepName, startedAt);
            }
        }

        internal UniTask ShutdownAsync()
        {
            if (m_shutdownCompleted != null) return m_shutdownCompleted.Task;
            if (m_shutdownImmediately) return UniTask.CompletedTask;

            // 先发布共享任务，避免清理回调重入后重复释放步骤。
            m_shutdownCompleted = new UniTaskCompletionSource();
            RunShutdownAsync(m_shutdownCompleted).Forget();
            return m_shutdownCompleted.Task;
        }

        private async UniTask RunShutdownAsync(UniTaskCompletionSource completion)
        {
            try
            {
                while (m_startedStepCount > 0 && !m_shutdownImmediately)
                {
                    m_startedStepCount--;
                    var step = m_steps[m_startedStepCount];
                    try
                    {
                        await step.ShutdownAsync(m_context);
                    }
                    catch (Exception exception)
                    {
                        // 单一步骤清理失败不能阻断其余依赖的逆序释放。
                        AlloyDebug.Error(exception);
                    }
                }
                ClearContext();
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        internal void ShutdownImmediately()
        {
            if (m_shutdownImmediately) return;
            m_shutdownImmediately = true;
            m_startedStepCount = 0;

            // 应用退出不能等待业务异步清理，保留系统的同步释放作为兜底。
            try
            {
                for (int index = m_steps.Count - 1; index >= 0; index--)
                {
                    try
                    {
                        if (m_steps[index] is InitializeFrameworkSystemStep systemStep)
                        {
                            systemStep.ShutdownImmediately();
                        }
                    }
                    catch (Exception exception)
                    {
                        AlloyDebug.Error(exception);
                    }
                }
            }
            finally
            {
                try
                {
                    ClearContext();
                }
                finally
                {
                    m_shutdownCompleted?.TrySetCanceled();
                }
            }
        }

        private void ClearContext()
        {
            // 最终释放只处理共享引用；资源清理由各步骤完成，同步退出也会经过这里。
            m_context.Services.Clear();
            m_context.Configuration = null;
            m_context.IsInitialized = false;
        }

        private void Add(IFrameworkStartupStep step)
        {
            m_steps.Add(step);
        }
    }
}
