using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AlloyFramework
{
    [FrameworkSystem(FrameworkSystemPriority.Resource)]
    internal sealed class ResourceSystem : FrameworkSystem, IAssetService, IHotUpdateService, IRawFileService
    {
        private IAssetService _implementation;

        public int TrackedAssetCount => GetImplementation().TrackedAssetCount;

        public override async UniTask InitializeAsync(FrameworkContext context, CancellationToken cancellationToken)
        {
            _implementation = ResourceSettings.ServiceFactory?.Invoke() ?? new YooAssetService();
            if (ReferenceEquals(_implementation, this))
                throw new InvalidOperationException("Resource implementation cannot be the ResourceSystem wrapper.");

            try
            {
                await _implementation.InitializeAsync(context, cancellationToken);
                ResourceManager.Instance.Bind(this);
            }
            catch
            {
                _implementation.Shutdown();
                _implementation = null;
                throw;
            }
        }

        public override void Shutdown()
        {
            ResourceManager.Instance.Unbind(this);
            _implementation?.Shutdown();
            _implementation = null;
        }

        public UniTask EnsurePackageAsync(string packageName, CancellationToken cancellationToken) =>
            GetImplementation().EnsurePackageAsync(packageName, cancellationToken);

        public UniTask<IAssetHandle<T>> LoadAssetAsync<T>(string location, string packageName,
            Action<float> progressCallback, CancellationToken cancellationToken) where T : UnityEngine.Object =>
            GetImplementation().LoadAssetAsync<T>(
                location, packageName, progressCallback, cancellationToken);

        public UniTask<IInstanceHandle> InstantiateAsync(string location, string packageName, Transform parent,
            Action<float> progressCallback, CancellationToken cancellationToken) =>
            GetImplementation().InstantiateAsync(
                location, packageName, parent, progressCallback, cancellationToken);

        public UniTask<ISceneHandle> LoadSceneAsync(string location, string packageName, LoadSceneMode mode,
            Action<float> progressCallback, CancellationToken cancellationToken) =>
            GetImplementation().LoadSceneAsync(
                location, packageName, mode, progressCallback, cancellationToken);

        public UniTask UnloadUnusedAsync(string packageName, CancellationToken cancellationToken) =>
            GetImplementation().UnloadUnusedAsync(packageName, cancellationToken);

        /// <summary>
        /// 将原始文件加载请求转发给当前资源实现。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <param name="cancellationToken">取消本次加载的令牌。</param>
        /// <returns>由资源实现提供的原始文件租约。</returns>
        /// <exception cref="InvalidOperationException">资源系统尚未初始化。</exception>
        /// <exception cref="NotSupportedException">当前资源实现不支持原始文件加载。</exception>
        public UniTask<RawFileLease> LoadRawFileAsync(
            string location,
            string packageName,
            CancellationToken cancellationToken)
        {
            // 包装层保留统一生命周期，具体加载能力由当前资源实现提供。
            IAssetService implementation = GetImplementation();
            if (!(implementation is IRawFileService rawFileService))
            {
                throw new NotSupportedException(
                    $"资源实现 {implementation.GetType().FullName} 不支持原始文件加载。" +
                    $"资源包={packageName}，资源地址={location}");
            }

            return rawFileService.LoadRawFileAsync(location, packageName, cancellationToken);
        }

        public IHotUpdateProvider CreateHotUpdateProvider(string packageName)
        {
            if (!(GetImplementation() is IHotUpdateService hotUpdateService))
                throw new NotSupportedException("The active resource implementation does not support hot updates.");
            return hotUpdateService.CreateHotUpdateProvider(packageName);
        }

        private IAssetService GetImplementation() =>
            _implementation ?? throw new InvalidOperationException("Resource system is not initialized.");
    }
}
