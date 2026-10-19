using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AlloyFramework
{
    /// <summary>统一管理资源请求的生命周期，并在所有在途请求退出后关闭实际资源实现。</summary>
    internal sealed class ResourceServiceManager : FrameworkSystem, IAssetService, IHotUpdateService, IRawFileService
    {
        private IAssetService m_implementation; // 当前项目选择的资源实现。
        private CancellationTokenSource m_lifetime; // 资源服务存活期间的取消源。
        private UniTaskCompletionSource m_operationsDrained; // 当前一批请求全部退出的通知。
        private UniTaskCompletionSource m_shutdownCompleted; // 所有正常关闭请求共享的结果。
        private int m_activeOperationCount; // 包含回滚阶段的在途请求数。
        private bool m_isShuttingDown; // 拒绝新请求并防止重复关闭。

        public int TrackedAssetCount => GetImplementation().TrackedAssetCount;

        /// <summary>初始化项目资源实现并向业务入口绑定服务。</summary>
        /// <param name="context">框架服务容器。</param>
        /// <param name="cancellationToken">用于取消初始化的令牌。</param>
        /// <returns>资源服务就绪的任务。</returns>
        /// <exception cref="InvalidOperationException">资源工厂返回了包装层自身。</exception>
        public override async UniTask InitializeAsync(FrameworkContext context, CancellationToken cancellationToken)
        {
            m_lifetime = new CancellationTokenSource();
            try
            {
                m_implementation = ResourceSettings.ServiceFactory?.Invoke() ?? new YooAssetService();
                if (ReferenceEquals(m_implementation, this))
                {
                    m_implementation = null;
                    throw new InvalidOperationException("资源实现不能是 ResourceServiceManager 包装层自身。");
                }

                await m_implementation.InitializeAsync(context, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                ResourceManager.Instance.Bind(this);
            }
            catch
            {
                // 初始化尚未登记到框架管线，当前实例自行完成局部清理。
                try { await ShutdownAsync(); }
                catch (Exception cleanupException) { AlloyDebug.Error(cleanupException); }
                throw;
            }
        }

        /// <summary>停止接收请求，取消并等待在途操作退出，再关闭实际资源实现。</summary>
        /// <returns>共享的正常关闭任务。</returns>
        /// <remarks>资源操作及其回调内部不能等待整个资源服务关闭。</remarks>
        public override UniTask ShutdownAsync()
        {
            if (m_shutdownCompleted != null) return m_shutdownCompleted.Task;
            var completion = new UniTaskCompletionSource();
            m_shutdownCompleted = completion;
            m_isShuttingDown = true;
            ResourceManager.Instance.Unbind(this);
            RunShutdownAsync(completion).Forget(ReportAsyncException);
            return completion.Task;
        }

        private async UniTask RunShutdownAsync(UniTaskCompletionSource completion)
        {
            Exception failure = null;
            try
            {
                var drained = m_activeOperationCount == 0 ? null : m_operationsDrained;
                CancelLifetime();
                if (drained != null) await drained.Task;
                if (m_implementation != null) await m_implementation.ShutdownAsync();
            }
            catch (Exception exception)
            {
                failure = exception;
                // 异步释放失败时才执行同步兜底，避免正常退出重复关闭项目实现。
                try { m_implementation?.Shutdown(); }
                catch (Exception cleanupException) { AlloyDebug.Error(cleanupException); }
            }
            finally
            {
                ReleaseState();
            }

            if (failure == null) completion.TrySetResult();
            else completion.TrySetException(failure);
        }

        /// <summary>强制同步终止资源服务；进程退出时不等待在途操作。</summary>
        public override void Shutdown()
        {
            m_isShuttingDown = true;
            ResourceManager.Instance.Unbind(this);
            CancelLifetime();
            try
            {
                m_implementation?.Shutdown();
            }
            finally
            {
                ReleaseState();
            }
        }

        private void ReleaseState()
        {
            m_implementation = null;
            var lifetime = m_lifetime;
            m_lifetime = null;
            lifetime?.Dispose();
        }

        private CancellationTokenSource BeginOperation(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetImplementation();
            if (m_lifetime == null) throw new InvalidOperationException("资源服务尚未初始化。");
            var operation = CancellationTokenSource.CreateLinkedTokenSource(m_lifetime.Token, cancellationToken);
            if (m_activeOperationCount == 0) m_operationsDrained = new UniTaskCompletionSource();
            m_activeOperationCount++;
            return operation;
        }

        private static void ReportAsyncException(Exception exception)
        {
            AlloyDebug.Error(exception);
        }

        private void EndOperation(CancellationTokenSource operation)
        {
            operation.Dispose();
            m_activeOperationCount--;
            if (m_activeOperationCount == 0) m_operationsDrained.TrySetResult();
        }

        private void CancelLifetime()
        {
            try { m_lifetime?.Cancel(); }
            catch (Exception exception) { AlloyDebug.Error(exception); }
        }

        /// <summary>确保指定资源包就绪，并将初始化任务纳入服务关闭等待。</summary>
        /// <param name="packageName">资源包名称。</param>
        /// <param name="cancellationToken">调用者取消令牌。</param>
        /// <returns>资源包就绪的任务。</returns>
        public async UniTask EnsurePackageAsync(string packageName, CancellationToken cancellationToken)
        {
            var operation = BeginOperation(cancellationToken);
            try
            {
                await m_implementation.EnsurePackageAsync(packageName, operation.Token);
                operation.Token.ThrowIfCancellationRequested();
            }
            finally { EndOperation(operation); }
        }

        /// <summary>加载资源，调用者取消只影响当前请求的引用。</summary>
        /// <param name="location">资源地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <param name="progressCallback">当前请求的进度通知。</param>
        /// <param name="cancellationToken">调用者取消令牌。</param>
        /// <returns>独立的资源租约。</returns>
        public async UniTask<IAssetHandle<T>> LoadAssetAsync<T>(
            string location, string packageName, Action<float> progressCallback, CancellationToken cancellationToken)
            where T : UnityEngine.Object
        {
            var operation = BeginOperation(cancellationToken);
            try
            {
                var handle = await m_implementation.LoadAssetAsync<T>(
                    location, packageName, progressCallback, operation.Token);
                try
                {
                    operation.Token.ThrowIfCancellationRequested();
                    return handle;
                }
                catch { handle.Dispose(); throw; }
            }
            finally { EndOperation(operation); }
        }

        /// <summary>实例化资源，并等待本次创建或回滚结束后才允许服务退出。</summary>
        /// <param name="location">预制体地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <param name="parent">实例父节点。</param>
        /// <param name="progressCallback">当前请求的进度通知。</param>
        /// <param name="cancellationToken">调用者取消令牌。</param>
        /// <returns>实例及其预制体引用的租约。</returns>
        public async UniTask<IInstanceHandle> InstantiateAsync(
            string location, string packageName, Transform parent,
            Action<float> progressCallback, CancellationToken cancellationToken)
        {
            var operation = BeginOperation(cancellationToken);
            try
            {
                var handle = await m_implementation.InstantiateAsync(
                    location, packageName, parent, progressCallback, operation.Token);
                try
                {
                    operation.Token.ThrowIfCancellationRequested();
                    return handle;
                }
                catch { handle.Dispose(); throw; }
            }
            finally { EndOperation(operation); }
        }

        /// <summary>加载场景，并将后续场景卸载纳入同一资源生命周期。</summary>
        /// <param name="location">场景地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <param name="mode">场景加载模式。</param>
        /// <param name="progressCallback">当前请求的进度通知。</param>
        /// <param name="cancellationToken">调用者取消令牌。</param>
        /// <returns>受服务生命周期约束的场景租约。</returns>
        public async UniTask<ISceneHandle> LoadSceneAsync(
            string location, string packageName, LoadSceneMode mode,
            Action<float> progressCallback, CancellationToken cancellationToken)
        {
            var operation = BeginOperation(cancellationToken);
            try
            {
                var handle = await m_implementation.LoadSceneAsync(
                    location, packageName, mode, progressCallback, operation.Token);
                try
                {
                    operation.Token.ThrowIfCancellationRequested();
                    return new ManagedSceneHandle(this, handle);
                }
                catch { await handle.UnloadAsync(); throw; }
            }
            finally { EndOperation(operation); }
        }

        /// <summary>清理指定包的闲置资源，关闭时等待已开始的清理结束。</summary>
        /// <param name="packageName">资源包名称。</param>
        /// <param name="cancellationToken">调用者取消令牌。</param>
        /// <returns>清理退出的任务。</returns>
        public async UniTask UnloadUnusedAsync(string packageName, CancellationToken cancellationToken)
        {
            var operation = BeginOperation(cancellationToken);
            try
            {
                await m_implementation.UnloadUnusedAsync(packageName, operation.Token);
                operation.Token.ThrowIfCancellationRequested();
            }
            finally { EndOperation(operation); }
        }

        /// <summary>加载本地、压缩包地址或资源包中的原始文件。</summary>
        /// <param name="location">文件地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <param name="cancellationToken">调用者取消令牌。</param>
        /// <returns>持有文件有效性的租约。</returns>
        /// <exception cref="NotSupportedException">资源实现不支持原始文件。</exception>
        public async UniTask<RawFileLease> LoadRawFileAsync(
            string location, string packageName, CancellationToken cancellationToken)
        {
            var operation = BeginOperation(cancellationToken);
            try
            {
                // 本地文件和网络请求也必须受资源服务关闭约束，不能绕开生命周期入口。
                var localLease = await RawFileLease.TryLoadLocalAsync(location, operation.Token);
                if (localLease != null)
                {
                    try
                    {
                        operation.Token.ThrowIfCancellationRequested();
                        return localLease;
                    }
                    catch { localLease.Dispose(); throw; }
                }
                if (!(m_implementation is IRawFileService rawFileService))
                {
                    throw new NotSupportedException("当前资源实现不支持原始文件加载。");
                }
                var lease = await rawFileService.LoadRawFileAsync(location, packageName, operation.Token);
                try
                {
                    operation.Token.ThrowIfCancellationRequested();
                    return lease;
                }
                catch { lease.Dispose(); throw; }
            }
            finally { EndOperation(operation); }
        }

        /// <summary>创建同样受资源服务关闭约束的热更新提供器。</summary>
        /// <param name="packageName">资源包名称。</param>
        /// <returns>受管理的热更新提供器。</returns>
        /// <exception cref="NotSupportedException">资源实现不支持热更新。</exception>
        public IHotUpdateProvider CreateHotUpdateProvider(string packageName)
        {
            if (!(GetImplementation() is IHotUpdateService hotUpdateService))
            {
                throw new NotSupportedException("当前资源实现不支持热更新。");
            }
            return new ManagedHotUpdateProvider(this, hotUpdateService.CreateHotUpdateProvider(packageName));
        }

        private IAssetService GetImplementation()
        {
            if (m_isShuttingDown || m_implementation == null)
                throw new InvalidOperationException("资源服务尚未初始化或正在关闭。");
            return m_implementation;
        }

        private sealed class ManagedSceneHandle : ISceneHandle
        {
            private readonly ResourceServiceManager m_owner; // 所属的资源生命周期。
            private readonly ISceneHandle m_handle; // 实际场景租约。
            private UniTaskCompletionSource m_unloadCompleted; // 合并重复场景卸载请求。

            internal ManagedSceneHandle(ResourceServiceManager owner, ISceneHandle handle)
            {
                m_owner = owner;
                m_handle = handle;
            }

            public Scene Scene => m_handle.Scene;
            public bool IsUnloaded => m_handle.IsUnloaded;

            /// <summary>卸载场景，等待底层卸载退出后才允许资源服务销毁。</summary>
            /// <returns>共享的场景卸载任务。</returns>
            public UniTask UnloadAsync()
            {
                if (m_unloadCompleted != null) return m_unloadCompleted.Task;
                if (m_handle.IsUnloaded) return UniTask.CompletedTask;
                var completion = new UniTaskCompletionSource();
                m_unloadCompleted = completion;
                RunUnloadAsync(completion).Forget(ReportAsyncException);
                return completion.Task;
            }

            private async UniTask RunUnloadAsync(UniTaskCompletionSource completion)
            {
                CancellationTokenSource operation = null;
                Exception failure = null;
                try
                {
                    operation = m_owner.BeginOperation(CancellationToken.None);
                    await m_handle.UnloadAsync();
                }
                catch (Exception exception) { failure = exception; }
                finally
                {
                    if (operation != null) m_owner.EndOperation(operation);
                }

                if (failure == null) completion.TrySetResult();
                else completion.TrySetException(failure);
            }
        }

        private sealed class ManagedHotUpdateProvider : IHotUpdateProvider
        {
            private readonly ResourceServiceManager m_owner; // 所属的资源生命周期。
            private readonly IHotUpdateProvider m_provider; // 项目实际提供的热更新实现。

            internal ManagedHotUpdateProvider(ResourceServiceManager owner, IHotUpdateProvider provider)
            {
                m_owner = owner;
                m_provider = provider;
            }

            public string PackageName => m_provider.PackageName;
            public HotUpdateState State => m_provider.State;
            public bool NeedUpdate => m_provider.NeedUpdate;
            public int TotalDownloadCount => m_provider.TotalDownloadCount;
            public long TotalDownloadBytes => m_provider.TotalDownloadBytes;
            public event Action<HotUpdateProgress> ProgressChanged
            {
                add { m_provider.ProgressChanged += value; }
                remove { m_provider.ProgressChanged -= value; }
            }
            public event Action<string, string> FileError
            {
                add { m_provider.FileError += value; }
                remove { m_provider.FileError -= value; }
            }

            /// <summary>执行热更新，关闭服务时取消并等待此调用退出。</summary>
            /// <param name="cancellationToken">调用者取消令牌。</param>
            /// <returns>热更新操作退出的任务。</returns>
            public async UniTask RunAsync(CancellationToken cancellationToken = default)
            {
                var operation = m_owner.BeginOperation(cancellationToken);
                try
                {
                    await m_provider.RunAsync(operation.Token);
                    operation.Token.ThrowIfCancellationRequested();
                }
                finally { m_owner.EndOperation(operation); }
            }

            /// <summary>清理缓存，服务关闭时等待底层清理与读取预留释放。</summary>
            /// <param name="cancellationToken">调用者取消令牌。</param>
            /// <returns>缓存清理退出的任务。</returns>
            public async UniTask ClearUnusedCacheAsync(CancellationToken cancellationToken = default)
            {
                var operation = m_owner.BeginOperation(cancellationToken);
                try
                {
                    await m_provider.ClearUnusedCacheAsync(operation.Token);
                    operation.Token.ThrowIfCancellationRequested();
                }
                finally { m_owner.EndOperation(operation); }
            }
        }
    }
}
