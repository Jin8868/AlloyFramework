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
        private static UniTaskCompletionSource m_startupExited; // 启动执行退出后通知关闭流程。
        private static UniTaskCompletionSource m_shutdownCompleted; // 多个关闭请求共享的完成结果。
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

        /// <summary>取消启动，等待当前启动步骤退出，再逆序关闭业务和已就绪的框架系统。</summary>
        /// <returns>共享的退出清理任务；重复调用会等待同一次清理完成。</returns>
        /// <remarks>从 Unity 主线程调用；启动步骤内部只能请求关闭，不能等待关闭完成。</remarks>
        public static UniTask ShutdownAsync()
        {
            if (m_shutdownCompleted != null)
            {
                return m_shutdownCompleted.Task;
            }

            if (m_startupContext == null)
            {
                return UniTask.CompletedTask;
            }

            // 先发布共享任务，取消回调同步重入时也只能加入同一次关闭。
            var completion = new UniTaskCompletionSource();
            m_shutdownCompleted = completion;
            m_isShuttingDown = true;
            RunShutdownAsync(completion).Forget(HandleStartupException);
            return completion.Task;
        }

        private static async UniTask RunShutdownAsync(UniTaskCompletionSource completion)
        {
            try
            {
                try
                {
                    // 取消只是请求，不能在当前步骤仍访问服务时释放其依赖。
                    CancelStartup();
                    CancelStartupLoading();
                    var startupExited = m_startupExited;
                    if (startupExited != null)
                    {
                        await startupExited.Task;
                    }

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
            catch (Exception exception)
            {
                completion.TrySetException(exception);
                return;
            }

            completion.TrySetResult();
        }

        private static void CancelStartup()
        {
            try
            {
                m_startupCancellation?.Cancel();
            }
            catch (Exception exception)
            {
                // 取消回调异常不能跳过等待、资源释放和最终状态复位。
                AlloyDebug.Error(exception);
            }
        }

        private static void CancelStartupLoading()
        {
            try
            {
                StartupLoading.Cancel();
            }
            catch (Exception exception)
            {
                // 展示层退出失败不应阻断框架资源释放。
                AlloyDebug.Error(exception);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // 关闭域重载时也清除上一轮订阅、服务注入和状态。
            Application.quitting -= HandleApplicationQuitting;
            UniTaskScheduler.UnobservedTaskException -= HandleUnobservedTaskException;
            ResetObjectPools();
            CancelStartup();
            m_startupCancellation?.Dispose();
            m_startupCancellation = null;
            m_startupScreen = null;
            m_startupContext = null;
            m_startupPipeline = null;
            m_startupExited = null;
            m_shutdownCompleted = null;
            m_isStarting = false;
            m_isShuttingDown = false;
            Initialized = null;
            StartupFailed = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Startup()
        {
            if (m_isStarting || m_isShuttingDown || IsInitialized)
            {
                return;
            }

            // 这里只创建共享状态，主包配置由管线的第一个步骤读取。
            var startedAt = FrameworkStartupLog.Now;
            FrameworkStartupLog.Info("开始启动框架");
            m_startupContext = new FrameworkStartupContext();
            m_startupCancellation = new CancellationTokenSource();
            m_startupExited = new UniTaskCompletionSource();
            m_shutdownCompleted = null;
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
            var startupExited = m_startupExited;
            Exception startupException = null;
            bool wasCanceled = false;
            try
            {
                // 内置展示真正绘制后，才创建系统并执行资源和代码准备。
                await StartupLoading.BeginAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                m_startupPipeline = new FrameworkStartupPipeline(m_startupContext);
                StartupLoading.ConfigureSteps(m_startupPipeline.StepCount);
                await m_startupPipeline.RunAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                FrameworkStartupLog.Step("完整启动（含业务入口）", startedAt);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                wasCanceled = true;
                FrameworkStartupLog.Info("框架启动取消");
            }
            catch (Exception exception)
            {
                startupException = exception;
                try
                {
                    // 在清理前冻结状态，展示失败也不能阻断原始启动异常的清理。
                    StartupLoading.Fail(exception);
                    FrameworkStartupLog.Failure("框架启动", startedAt);
                }
                catch (Exception presentationException)
                {
                    AlloyDebug.Error(presentationException);
                }
            }
            finally
            {
                // 此信号只覆盖启动执行与局部回滚，不能包含后面的关闭等待。
                m_isStarting = false;
                startupExited.TrySetResult();
            }

            if (wasCanceled || startupException != null)
            {
                // 先通知启动已退出，再加入共享关闭任务，避免启动与关闭相互等待。
                try
                {
                    await ShutdownAsync();
                }
                finally
                {
                    if (startupException != null)
                    {
                        HandleStartupException(startupException);
                        StartupFailed?.Invoke(startupException);
                    }
                }
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
            CancelStartup();
            CancelStartupLoading();
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
            try
            {
                m_startupCancellation?.Dispose();
            }
            finally
            {
                // 正常关闭在启动执行与清理结束后释放取消源，共享关闭结果保留给后续调用者。
                m_startupCancellation = null;
                ResetObjectPools();
                m_startupPipeline = null;
                m_startupContext = null;
                m_isStarting = false;
                m_isShuttingDown = false;
            }
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
