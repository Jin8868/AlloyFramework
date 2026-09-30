using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AlloyFramework
{
    public sealed class ResourceManager
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
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            return GetService().LoadAssetAsync<T>(location, packageName, progress, cancellationToken);
        }

        public UniTask<IInstanceHandle> InstantiateAsync(string location, Transform parent = null,
            string packageName = ResourceSettings.DefaultPackageName,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
        {
            return GetService().InstantiateAsync(location, packageName, parent, progress, cancellationToken);
        }

        public UniTask<ISceneHandle> LoadSceneAsync(string location,
            LoadSceneMode mode = LoadSceneMode.Single,
            string packageName = ResourceSettings.DefaultPackageName,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
        {
            return GetService().LoadSceneAsync(location, packageName, mode, progress, cancellationToken);
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
