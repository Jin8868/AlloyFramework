using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlloyFramework
{
    /// <summary>协调框架显式启动、业务交接和退出清理。</summary>
    public sealed class FrameworkStartupController : IDisposable
    {
        private FrameworkStartupContext m_startupContext; // 本轮系统和业务入口的生命周期状态。
        private FrameworkStartupPipeline m_startupPipeline; // 显式注册的启动执行顺序。
        private CancellationTokenSource m_startupCancellation; // 本轮启动和运行的取消源。
        private UniTaskCompletionSource m_startupExited; // 启动执行退出后通知关闭流程。
        private UniTaskCompletionSource m_shutdownCompleted; // 多个关闭请求共享的完成结果。
        private GameObject m_startupScreen; // 启动场景中的旧启动画面对象。
        private bool m_isStarting; // 是否正在执行完整启动流程。
        private bool m_hasStarted; // 一个控制器只负责一轮启动。
        private bool m_isDisposed; // 同步退出后阻止异步续体再次操作全局资源。

        internal FrameworkStartupController()
        {
        }

        /// <summary>框架是否正在启动，包含业务启动阶段。</summary>
        public bool IsStarting => m_isStarting;

        /// <summary>框架系统是否已就绪，业务入口此时可能尚未执行完成。</summary>
        public bool IsInitialized => m_startupContext != null && m_startupContext.IsInitialized;

        /// <summary>所有框架系统就绪后触发。</summary>
        public event Action Initialized;

        /// <summary>启动失败并完成退出清理后触发。</summary>
        public event Action<Exception> StartupFailed;

        /// <summary>销毁框架启动时保存的启动画面；对象不存在时不执行操作。</summary>
        private void DestroyStartupScreen()
        {
            var startupScreen = m_startupScreen;
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
        public TService GetRequired<TService>() where TService : class
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
        public UniTask ShutdownAsync()
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
            RunShutdownAsync(completion).Forget(HandleStartupException);
            return completion.Task;
        }

        private async UniTask RunShutdownAsync(UniTaskCompletionSource completion)
        {
            // 外层也捕获最终清理异常，保证关闭等待者始终收到结果。
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

                    if (!m_isDisposed && m_startupPipeline != null)
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

        private void CancelStartup()
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

        private void CancelStartupLoading()
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

        /// <summary>由场景入口显式启动本轮框架；每个控制器仅启动一次，重复请求不执行操作。</summary>
        /// <param name="configurationProvider">本轮启动使用的主包配置提供者。</param>
        /// <param name="startupScreen">展示层就绪后销毁的启动画面；可以为空。</param>
        /// <exception cref="ArgumentNullException">配置提供者为空。</exception>
        /// <exception cref="ObjectDisposedException">控制器已经释放。</exception>
        internal void Startup(
            IGameStartupConfigurationProvider configurationProvider,
            GameObject startupScreen)
        {
            if (m_isDisposed)
            {
                throw new ObjectDisposedException(nameof(FrameworkStartupController));
            }

            if (m_hasStarted)
            {
                return;
            }

            // 配置来源由场景入口决定，具体配置仍由管线首个步骤校验和创建。
            if (configurationProvider == null)
            {
                throw new ArgumentNullException(nameof(configurationProvider));
            }
            // 新实例开始新一轮，清理全局池管理器持有的历史对象池。
            ResetObjectPools();
            m_hasStarted = true;
            var startedAt = FrameworkStartupLog.Now;
            FrameworkStartupLog.Info("开始启动框架");
            m_startupContext = new FrameworkStartupContext
            {
                ConfigurationProvider = configurationProvider
            };
            m_startupContext.Services.StartupController = this;
            m_startupScreen = startupScreen;
            m_startupCancellation = new CancellationTokenSource();
            m_startupExited = new UniTaskCompletionSource();
            m_shutdownCompleted = null;
            m_isStarting = true;
            Application.quitting += HandleApplicationQuitting;
            UniTaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
            RunStartupAsync(m_startupCancellation.Token, startedAt).Forget(HandleStartupException);
        }

        private async UniTask RunStartupAsync(CancellationToken cancellationToken, double startedAt)
        {
            var startupExited = m_startupExited;
            Exception startupException = null;
            bool wasCanceled = false;
            try
            {
                // 内置展示真正绘制后，才创建系统并执行资源和代码准备。
                await StartupLoading.BeginAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                DestroyStartupScreen();
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
                    if (m_isDisposed)
                    {
                        return;
                    }

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

            // 同步退出已经清理资源，不再让异步续体触碰后续运行的全局展示。
            if (m_isDisposed)
            {
                return;
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

        /// <summary>应用退出时同步兜底释放资源；正常运行期间应先等待 ShutdownAsync。</summary>
        /// <remarks>从 Unity 主线程调用；此方法不等待业务异步退出。</remarks>
        public void Dispose()
        {
            if (m_isDisposed)
            {
                return;
            }

            m_isDisposed = true;
            if (m_startupContext == null)
            {
                Initialized = null;
                StartupFailed = null;
                return;
            }

            // 即使异步关闭尚在等待，进程退出也必须执行同步兜底清理。
            CancelStartup();
            CancelStartupLoading();
            try
            {
                m_startupPipeline?.ShutdownImmediately();
            }
            finally
            {
                // 最终清理异常也不能遗留关闭等待者或实例订阅。
                try
                {
                    CompleteShutdown();
                }
                finally
                {
                    m_shutdownCompleted?.TrySetCanceled();
                    Initialized = null;
                    StartupFailed = null;
                }
            }
        }

        private void CompleteShutdown()
        {
            // 全局事件不再持有本轮实例，同步与异步清理重入时只释放一次。
            Application.quitting -= HandleApplicationQuitting;
            UniTaskScheduler.UnobservedTaskException -= HandleUnobservedTaskException;
            if (m_startupContext == null)
            {
                return;
            }

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
                m_startupContext.Services.StartupController = null;
                m_startupContext = null;
                m_startupScreen = null;
                m_isStarting = false;
            }
        }

        private void ResetObjectPools()
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

        internal void HandleInitialized()
        {
            Initialized?.Invoke();
        }

        private void HandleApplicationQuitting()
        {
            Dispose();
        }

        private void HandleStartupException(Exception exception)
        {
            AlloyDebug.Error(exception);
        }

        private void HandleUnobservedTaskException(Exception exception)
        {
            AlloyDebug.Error(exception);
        }
    }
}
