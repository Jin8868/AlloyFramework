using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlloyFramework
{
    /// <summary>协调框架自动启动、业务交接和退出清理。</summary>
    public static class FrameworkBootstrap
    {
        private const string STARTUPSCREENOBJECTNAME = "FrameworkSplash"; // 启动场景中的旧启动画面名称。

        private static FrameworkStartupContext m_startupContext; // 本轮系统和业务入口的生命周期状态。
        private static FrameworkStartupPipeline m_startupPipeline; // 显式注册的启动执行顺序。
        private static CancellationTokenSource m_startupCancellation; // 本轮启动和运行的取消源。
        private static GameObject m_startupScreen; // 启动场景中的旧启动画面对象。
        private static bool m_isStarting; // 是否正在执行完整启动流程。
        private static bool m_isShuttingDown; // 防止退出清理重入。

        /// <summary>框架是否正在启动，包含业务启动阶段。</summary>
        public static bool IsStarting => m_isStarting;

        /// <summary>框架系统是否已就绪，业务入口此时可能尚未执行完成。</summary>
        public static bool IsInitialized => m_startupContext != null && m_startupContext.IsInitialized;

        /// <summary>所有框架系统就绪后触发。</summary>
        public static event Action Initialized;

        /// <summary>启动失败并完成退出清理后触发。</summary>
        public static event Action<Exception> StartupFailed;

        /// <summary>销毁框架启动时保存的启动画面；对象不存在时不执行操作。</summary>
        public static void DestroyStartupScreen()
        {
            var startupScreen = m_startupScreen != null
                ? m_startupScreen
                : GameObject.Find(STARTUPSCREENOBJECTNAME);
            m_startupScreen = null;
            if (startupScreen != null)
            {
                UnityEngine.Object.Destroy(startupScreen);
            }
        }

        /// <summary>获取本轮已经注册的框架服务。</summary>
        /// <typeparam name="TService">请求的服务类型。</typeparam>
        /// <returns>已初始化的服务实例。</returns>
        /// <exception cref="InvalidOperationException">框架尚未启动或请求的服务尚未注册。</exception>
        public static TService GetRequired<TService>() where TService : class
        {
            if (m_startupContext == null)
            {
                throw new InvalidOperationException("框架尚未启动。");
            }

            return m_startupContext.Services.GetRequired<TService>();
        }

        /// <summary>取消启动，等待业务退出并逆序关闭已经就绪的框架系统。</summary>
        /// <returns>本次退出清理任务；重入调用不重复清理。</returns>
        public static async UniTask ShutdownAsync()
        {
            if (m_isShuttingDown)
            {
                return;
            }

            m_isShuttingDown = true;
            m_startupCancellation?.Cancel();
            StartupLoading.Cancel();
            try
            {
                if (m_startupPipeline != null)
                {
                    await m_startupPipeline.ShutdownAsync();
                }
            }
            finally
            {
                CompleteShutdown();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // 关闭域重载时也清除上一轮订阅、服务注入和状态。
            Application.quitting -= HandleApplicationQuitting;
            UniTaskScheduler.UnobservedTaskException -= HandleUnobservedTaskException;
            ResetObjectPools();
            m_startupCancellation?.Cancel();
            m_startupCancellation?.Dispose();
            m_startupCancellation = null;
            m_startupScreen = null;
            m_startupContext = null;
            m_startupPipeline = null;
            m_isStarting = false;
            m_isShuttingDown = false;
            Initialized = null;
            StartupFailed = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Startup()
        {
            if (m_isStarting || IsInitialized)
            {
                return;
            }

            // 这里只创建共享状态，主包配置由管线的第一个步骤读取。
            var startedAt = FrameworkStartupLog.Now;
            FrameworkStartupLog.Info("开始启动框架");
            m_startupContext = new FrameworkStartupContext();
            m_startupCancellation = new CancellationTokenSource();
            m_isStarting = true;
            Application.quitting += HandleApplicationQuitting;
            UniTaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
            RunStartupAsync(m_startupCancellation.Token, startedAt).Forget(HandleStartupException);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CaptureStartupScreen()
        {
            m_startupScreen = GameObject.Find(STARTUPSCREENOBJECTNAME);
        }

        private static async UniTask RunStartupAsync(CancellationToken cancellationToken, double startedAt)
        {
            try
            {
                // 内置展示真正绘制后，才创建系统并执行资源和代码准备。
                await StartupLoading.BeginAsync(cancellationToken);
                m_startupPipeline = new FrameworkStartupPipeline(m_startupContext);
                StartupLoading.ConfigureSteps(m_startupPipeline.StepCount);
                await m_startupPipeline.RunAsync(cancellationToken);
                FrameworkStartupLog.Step("完整启动（含业务入口）", startedAt);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                FrameworkStartupLog.Info("框架启动取消");
                await ShutdownAsync();
            }
            catch (Exception exception)
            {
                // 在清理前冻结状态，保留完全内置的错误界面。
                StartupLoading.Fail(exception);
                FrameworkStartupLog.Failure("框架启动", startedAt);
                await ShutdownAsync();
                StartupFailed?.Invoke(exception);
                throw;
            }
            finally
            {
                m_isStarting = false;
            }
        }

        private static void ShutdownImmediately()
        {
            if (m_isShuttingDown)
            {
                return;
            }

            // 进程退出不能等待异步业务清理，直接关闭框架系统。
            m_isShuttingDown = true;
            m_startupCancellation?.Cancel();
            StartupLoading.Cancel();
            try
            {
                m_startupPipeline?.ShutdownImmediately();
            }
            finally
            {
                CompleteShutdown();
            }
        }

        private static void CompleteShutdown()
        {
            ResetObjectPools();
            m_startupPipeline = null;
            m_isStarting = false;
            m_isShuttingDown = false;
        }

        private static void ResetObjectPools()
        {
            try
            {
                ObjectPoolManager.ResetInstance();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        internal static void HandleInitialized()
        {
            Initialized?.Invoke();
        }

        private static void HandleApplicationQuitting()
        {
            ShutdownImmediately();
        }

        private static void HandleStartupException(Exception exception)
        {
            AlloyDebug.Error(exception);
        }

        private static void HandleUnobservedTaskException(Exception exception)
        {
            AlloyDebug.Error(exception);
        }
    }
}
