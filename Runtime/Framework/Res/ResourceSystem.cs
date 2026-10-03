using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AlloyFramework
{
    [FrameworkSystem(FrameworkSystemPriority.Resource)]
    internal sealed class ResourceSystem : FrameworkSystem, IAssetService, IHotUpdateService
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
