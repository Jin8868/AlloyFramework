using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AlloyFramework
{
    public sealed partial class ResourceManager
    {
        private static readonly ResourceManager Singleton = new ResourceManager();
        private IAssetService _service;

        private ResourceManager()
        {
        }

        public static ResourceManager Instance => Singleton;
        public bool IsReady => _service != null;
        public int TrackedAssetCount => GetService().TrackedAssetCount;

        public IHotUpdateProvider CreateHotUpdateProvider(string packageName)
        {
            if (!(GetService() is IHotUpdateService updateService))
                throw new NotSupportedException("The active resource implementation does not support hot updates.");
            return updateService.CreateHotUpdateProvider(packageName);
        }

        public UniTask EnsurePackageAsync(string packageName, CancellationToken cancellationToken = default)
        {
            return GetService().EnsurePackageAsync(packageName, cancellationToken);
        }

        public UniTask<IAssetHandle<T>> LoadAssetAsync<T>(string location,
            string packageName = ResourceSettings.DefaultPackageName,
            Action<float> progressCallback = null,
            CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            return GetService().LoadAssetAsync<T>(
                location, packageName, progressCallback, cancellationToken);
        }

        public UniTask<IInstanceHandle> InstantiateAsync(string location, Transform parent = null,
            string packageName = ResourceSettings.DefaultPackageName,
            Action<float> progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            return GetService().InstantiateAsync(
                location, packageName, parent, progressCallback, cancellationToken);
        }

        public UniTask<ISceneHandle> LoadSceneAsync(string location,
            Action<float> progressCallback = null,
            LoadSceneMode mode = LoadSceneMode.Single,
            string packageName = ResourceSettings.DefaultPackageName,
            CancellationToken cancellationToken = default)
        {
            return GetService().LoadSceneAsync(
                location, packageName, mode, progressCallback, cancellationToken);
        }

        /// <summary>
        /// 尝试清理指定资源所在 Bundle 及其依赖中的闲置资源，不归还调用者持有的句柄引用。
        /// </summary>
        /// <param name="location">用于定位清理范围的资源地址。</param>
        /// <param name="packageName">已初始化的资源包名称。</param>
        /// <remarks>仅限主线程调用；同步尝试清理，不保证资源立即卸载，也不限定只清理该资源。</remarks>
        /// <exception cref="ArgumentException">资源地址或资源包名称为空。</exception>
        /// <exception cref="InvalidOperationException">资源服务或指定资源包尚未就绪。</exception>
        public void TryUnloadUnusedAsset(
            string location,
            string packageName = ResourceSettings.DefaultPackageName)
        {
            GetService().TryUnloadUnusedAsset(location, packageName);
        }

        public UniTask UnloadUnusedAsync(string packageName = ResourceSettings.DefaultPackageName,
            CancellationToken cancellationToken = default)
        {
            return GetService().UnloadUnusedAsync(packageName, cancellationToken);
        }

        internal void Bind(IAssetService service)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (_service != null) throw new InvalidOperationException("ResourceManager is already initialized.");
            _service = service;
        }

        internal void Unbind(IAssetService service)
        {
            if (ReferenceEquals(_service, service)) _service = null;
        }

        private IAssetService GetService()
        {
            return _service ?? throw new InvalidOperationException("Resource system is not initialized yet.");
        }
    }
}
