using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;

namespace AlloyFramework
{
    internal sealed class YooAssetService : FrameworkSystem, IAssetService, IHotUpdateService
    {
        private readonly Dictionary<string, ResourcePackage> _packages = new Dictionary<string, ResourcePackage>();
        private readonly Dictionary<AssetKey, SharedAsset> _assets = new Dictionary<AssetKey, SharedAsset>();
        private readonly SemaphoreSlim _packageGate = new SemaphoreSlim(1, 1);
        private bool _initialized;

        public int TrackedAssetCount => _assets.Count;

        public override async UniTask InitializeAsync(FrameworkContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            YooAssets.Initialize();

            try
            {
                await EnsurePackageAsync(ResourceSettings.DefaultPackageName, cancellationToken);
                _initialized = true;
            }
            catch
            {
                Shutdown();
                throw;
            }
        }

        public async UniTask EnsurePackageAsync(string packageName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(packageName)) throw new ArgumentException("Package name is required.", nameof(packageName));
            await _packageGate.WaitAsync(cancellationToken);
            try
            {
                if (_packages.ContainsKey(packageName)) return;
                await InitializePackageAsync(packageName, cancellationToken);
            }
            finally
            {
                _packageGate.Release();
            }
        }

        private async UniTask InitializePackageAsync(string packageName, CancellationToken cancellationToken)
        {
            var package = YooAssets.TryGetPackage(packageName) ?? YooAssets.CreatePackage(packageName);
            InitializeParameters parameters;

            switch (ResourceSettings.PlayMode)
            {
                case ResourcePlayMode.EditorSimulate:
#if UNITY_EDITOR
                    var build = EditorSimulateModeHelper.SimulateBuild(packageName);
                    if (string.IsNullOrEmpty(build.PackageRootDirectory))
                        throw new InvalidOperationException($"YooAssets simulation build failed: {packageName}");
                    parameters = new EditorSimulateModeParameters
                    {
                        EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(build.PackageRootDirectory)
                    };
                    break;
#else
                    throw new InvalidOperationException("EditorSimulate mode is only available in the Editor.");
#endif
                case ResourcePlayMode.Offline:
                    parameters = new OfflinePlayModeParameters
                    {
                        BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters()
                    };
                    break;
                case ResourcePlayMode.Host:
                    if (ResourceSettings.RemoteUrlProvider == null)
                        throw new InvalidOperationException("Host mode requires ResourceSettings.RemoteUrlProvider.");
                    parameters = new HostPlayModeParameters
                    {
                        BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(),
                        CacheFileSystemParameters = FileSystemParameters.CreateDefaultCacheFileSystemParameters(
                            new RemoteServices(packageName, ResourceSettings.RemoteUrlProvider))
                    };
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var operation = package.InitializeAsync(parameters);
            await UniTask.WaitUntil(() => operation.IsDone, cancellationToken: cancellationToken);
            if (operation.Status != EOperationStatus.Succeed)
                throw new InvalidOperationException($"Failed to initialize package {packageName}: {operation.Error}");

            _packages.Add(packageName, package);
            if (packageName == ResourceSettings.DefaultPackageName)
                YooAssets.SetDefaultPackage(package);
        }

        public async UniTask<IAssetHandle<T>> LoadAssetAsync<T>(string location, string packageName,
            IProgress<float> progress, CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            ValidateLocation(location);
            cancellationToken.ThrowIfCancellationRequested();
            var package = GetPackage(packageName);
            var key = new AssetKey(packageName, location, typeof(T));

            if (!_assets.TryGetValue(key, out var shared))
            {
                shared = new SharedAsset(package.LoadAssetAsync<T>(location));
                _assets.Add(key, shared);
            }

            shared.References++;
            try
            {
                await UniTask.WaitUntil(() =>
                {
                    progress?.Report(shared.Handle.Progress);
                    return shared.Handle.IsDone;
                }, cancellationToken: cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (shared.Handle.Status != EOperationStatus.Succeed)
                    throw new InvalidOperationException($"Failed to load {packageName}/{location}: {shared.Handle.LastError}");

                var asset = shared.Handle.GetAssetObject<T>();
                if (asset == null)
                    throw new InvalidOperationException($"Asset has wrong type or is null: {packageName}/{location} ({typeof(T).Name})");

                progress?.Report(1f);
                return new AssetLease<T>(asset, () => Release(key, shared));
            }
            catch
            {
                Release(key, shared);
                throw;
            }
        }

        public async UniTask<IInstanceHandle> InstantiateAsync(string location, string packageName, Transform parent,
            IProgress<float> progress, CancellationToken cancellationToken)
        {
            var prefab = await LoadAssetAsync<GameObject>(location, packageName, progress, cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var instance = UnityEngine.Object.Instantiate(prefab.Asset, parent);
                return new InstanceLease(instance, prefab);
            }
            catch
            {
                prefab.Dispose();
                throw;
            }
        }

        public async UniTask<ISceneHandle> LoadSceneAsync(string location, string packageName, LoadSceneMode mode,
            IProgress<float> progress, CancellationToken cancellationToken)
        {
            ValidateLocation(location);
            cancellationToken.ThrowIfCancellationRequested();
            var handle = GetPackage(packageName).LoadSceneAsync(location, mode);
            try
            {
                await UniTask.WaitUntil(() =>
                {
                    progress?.Report(handle.Progress);
                    return handle.IsDone;
                }, cancellationToken: cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (handle.Status != EOperationStatus.Succeed)
                    throw new InvalidOperationException($"Failed to load scene {packageName}/{location}: {handle.LastError}");
                progress?.Report(1f);
                return new SceneLease(handle);
            }
            catch
            {
                if (handle.IsValid && !handle.IsDone)
                    await UniTask.WaitUntil(() => handle.IsDone || !handle.IsValid);

                if (handle.IsValid)
                {
                    if (handle.Status == EOperationStatus.Succeed && handle.SceneObject.IsValid() && handle.SceneObject.isLoaded)
                    {
                        var unload = handle.UnloadAsync();
                        await UniTask.WaitUntil(() => unload.IsDone);
                    }
                    else
                    {
                        handle.Release();
                    }
                }
                throw;
            }
        }

        public async UniTask UnloadUnusedAsync(string packageName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = GetPackage(packageName).UnloadUnusedAssetsAsync();
            await UniTask.WaitUntil(() => operation.IsDone, cancellationToken: cancellationToken);
            if (operation.Status != EOperationStatus.Succeed)
                throw new InvalidOperationException($"Failed to unload unused assets from {packageName}: {operation.Error}");
        }

        public IHotUpdateProvider CreateHotUpdateProvider(string packageName)
        {
            if (ResourceSettings.PlayMode != ResourcePlayMode.Host)
                throw new InvalidOperationException("Hot update requires Host resource mode.");
            return new YooAssetHotUpdateProvider(GetPackage(packageName));
        }

        public override void Shutdown()
        {
            foreach (var shared in _assets.Values)
            {
                if (shared.Handle.IsValid) shared.Handle.Release();
            }
            _assets.Clear();
            _packages.Clear();
            _initialized = false;
            if (YooAssets.Initialized) YooAssets.Destroy();
        }

        private ResourcePackage GetPackage(string packageName)
        {
            if (!_initialized) throw new InvalidOperationException("Resource system is not initialized.");
            if (string.IsNullOrWhiteSpace(packageName)) throw new ArgumentException("Package name is required.", nameof(packageName));
            if (!_packages.TryGetValue(packageName, out var package))
                throw new InvalidOperationException($"Package is not initialized: {packageName}. Call EnsurePackageAsync first.");
            return package;
        }

        private void Release(AssetKey key, SharedAsset shared)
        {
            if (shared.References <= 0) return;
            shared.References--;
            if (shared.References != 0) return;
            if (_assets.TryGetValue(key, out var current) && ReferenceEquals(current, shared))
                _assets.Remove(key);
            if (shared.Handle.IsValid) shared.Handle.Release();
        }

        private static void ValidateLocation(string location)
        {
            if (string.IsNullOrWhiteSpace(location)) throw new ArgumentException("Asset location is required.", nameof(location));
        }

        private readonly struct AssetKey : IEquatable<AssetKey>
        {
            private readonly string _package;
            private readonly string _location;
            private readonly Type _type;

            public AssetKey(string package, string location, Type type)
            {
                _package = package;
                _location = location;
                _type = type;
            }

            public bool Equals(AssetKey other) => _package == other._package && _location == other._location && _type == other._type;
            public override bool Equals(object obj) => obj is AssetKey other && Equals(other);
            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = _package != null ? _package.GetHashCode() : 0;
                    hash = (hash * 397) ^ (_location != null ? _location.GetHashCode() : 0);
                    return (hash * 397) ^ (_type != null ? _type.GetHashCode() : 0);
                }
            }
        }

        private sealed class SharedAsset
        {
            public SharedAsset(YooAsset.AssetHandle handle) { Handle = handle; }
            public YooAsset.AssetHandle Handle { get; }
            public int References { get; set; }
        }

        private sealed class AssetLease<T> : IAssetHandle<T> where T : UnityEngine.Object
        {
            private readonly Action _release;
            private T _asset;
            public AssetLease(T asset, Action release) { _asset = asset; _release = release; }
            public T Asset => !IsDisposed ? _asset : throw new ObjectDisposedException(nameof(AssetLease<T>));
            public bool IsDisposed { get; private set; }
            public void Dispose()
            {
                if (IsDisposed) return;
                IsDisposed = true;
                _asset = null;
                _release();
            }
        }

        private sealed class InstanceLease : IInstanceHandle
        {
            private readonly IAssetHandle<GameObject> _prefab;
            private GameObject _instance;
            public InstanceLease(GameObject instance, IAssetHandle<GameObject> prefab) { _instance = instance; _prefab = prefab; }
            public GameObject Instance => !IsDisposed ? _instance : throw new ObjectDisposedException(nameof(InstanceLease));
            public bool IsDisposed { get; private set; }
            public void Dispose()
            {
                if (IsDisposed) return;
                IsDisposed = true;
                if (_instance != null) UnityEngine.Object.Destroy(_instance);
                _instance = null;
                _prefab.Dispose();
            }
        }

        private sealed class SceneLease : ISceneHandle
        {
            private readonly YooAsset.SceneHandle _handle;
            public SceneLease(YooAsset.SceneHandle handle) { _handle = handle; }
            public Scene Scene => !IsUnloaded ? _handle.SceneObject : default;
            public bool IsUnloaded { get; private set; }
            public async UniTask UnloadAsync()
            {
                if (IsUnloaded) return;
                if (!_handle.IsValid)
                {
                    IsUnloaded = true;
                    return;
                }
                if (!_handle.SceneObject.IsValid() || !_handle.SceneObject.isLoaded)
                {
                    _handle.Release();
                    IsUnloaded = true;
                    return;
                }
                var operation = _handle.UnloadAsync();
                await UniTask.WaitUntil(() => operation.IsDone);
                if (operation.Status != EOperationStatus.Succeed)
                    throw new InvalidOperationException($"Failed to unload scene: {operation.Error}");
                IsUnloaded = true;
            }
        }

        private sealed class RemoteServices : IRemoteServices
        {
            private readonly string _packageName;
            private readonly Func<string, string, string> _urlProvider;
            public RemoteServices(string packageName, Func<string, string, string> urlProvider)
            {
                _packageName = packageName;
                _urlProvider = urlProvider;
            }
            public string GetRemoteMainURL(string fileName) => _urlProvider(_packageName, fileName);
            public string GetRemoteFallbackURL(string fileName) => _urlProvider(_packageName, fileName);
        }
    }
}
