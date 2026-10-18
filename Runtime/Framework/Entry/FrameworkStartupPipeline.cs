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
        private int m_stepCount; // 框架 Loading 的真实步骤总数。
        private bool m_hasRun; // 防止重复初始化同一批系统。

        internal int StepCount => m_stepCount;

        internal FrameworkStartupPipeline(FrameworkStartupContext context)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));

            // 配置先于资源初始化；各步骤自行创建所需的配置实现。
            Add(new ConfigureGameStartupStep());

            // 列表就是框架系统的初始化顺序，退出时逆序关闭成功初始化的系统。
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
            Add(new LoadGameEntryStep());
            Add(new InitializeGameStartupStep());
            Add(new StartGameEntryStep());
        }

        internal async UniTask RunAsync(CancellationToken cancellationToken)
        {
            if (m_hasRun)
            {
                throw new InvalidOperationException("框架启动管线不能重复执行。");
            }

            m_hasRun = true;
            foreach (var step in m_steps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var startedAt = FrameworkStartupLog.Now;
                FrameworkStartupLog.Info($"开始：{step.Name}");
                try
                {
                    await step.ExecuteAsync(m_context, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch
                {
                    FrameworkStartupLog.Failure(step.Name, startedAt);
                    throw;
                }

                FrameworkStartupLog.Step(step.Name, startedAt);
            }
        }

        internal async UniTask ShutdownAsync()
        {
            // 先结束启动展示；失败会话释放后仍保留独立的 AOT 错误面板。
            var loadingSession = m_context.LoadingSession;
            m_context.LoadingSession = null;
            loadingSession?.Dispose();

            // 场景和界面先结束使用业务功能，再逆序释放初始化器和框架系统。
            if (m_context.IsGameEntryStarted)
            {
                m_context.IsGameEntryStarted = false;
                try
                {
                    await m_context.GameEntry.ShutdownAsync();
                }
                catch (Exception exception)
                {
                    AlloyDebug.Error(exception);
                }
            }

            if (m_context.IsGameStartupStarted)
            {
                m_context.IsGameStartupStarted = false;
                try
                {
                    await m_context.GameStartup.ShutdownAsync();
                }
                catch (Exception exception)
                {
                    AlloyDebug.Error(exception);
                }
            }

            ShutdownSystems();
        }

        internal void ShutdownImmediately()
        {
            // 进程退出不能等待异步业务任务，由框架系统执行同步兜底清理。
            var loadingSession = m_context.LoadingSession;
            m_context.LoadingSession = null;
            loadingSession?.Dispose();
            m_context.IsGameEntryStarted = false;
            m_context.IsGameStartupStarted = false;
            ShutdownSystems();
        }

        private void ShutdownSystems()
        {
            // 单个系统释放失败不能阻断后续系统清理。
            for (int index = m_context.InitializedSystems.Count - 1; index >= 0; index--)
            {
                try
                {
                    m_context.InitializedSystems[index].Shutdown();
                }
                catch (Exception exception)
                {
                    AlloyDebug.Error(exception);
                }
            }

            m_context.InitializedSystems.Clear();
            m_context.Services.Clear();
            m_context.GameStartup = null;
            m_context.GameEntry = null;
            m_context.IsGameStartupCompleted = false;
            m_context.IsInitialized = false;
        }

        private void Add(IFrameworkStartupStep step)
        {
            m_steps.Add(step);
            m_stepCount += step.StepCount;
        }
    }
}
