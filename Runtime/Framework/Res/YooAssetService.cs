using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;

namespace AlloyFramework
{
    internal sealed partial class YooAssetService : FrameworkSystem, IAssetService, IHotUpdateService
    {
        private readonly Dictionary<string, ResourcePackage> _packages = new Dictionary<string, ResourcePackage>();
        private readonly Dictionary<AssetKey, SharedAsset> _assets = new Dictionary<AssetKey, SharedAsset>();
        private readonly SemaphoreSlim _packageGate = new SemaphoreSlim(1, 1);
        private bool _initialized;

        public int TrackedAssetCount => _assets.Count;

        /// <summary>固定发布信息并初始化默认资源包，失败时回收局部资源。</summary>
        /// <param name="context">框架服务容器。</param>
        /// <param name="cancellationToken">初始化取消令牌。</param>
        /// <returns>默认资源包就绪的任务。</returns>
        public override async UniTask InitializeAsync(FrameworkContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 在任何包初始化前固定发布信息，保证代码、资源和音频属于同一次发布。
            if (ResourceSettings.StartupPreparation != null)
            {
                StartupLoading.ReportOperation(EStartupLoadingStage.CheckingRelease, "检查发布版本" );
                await ResourceSettings.StartupPreparation(cancellationToken);
            }
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
            // 准备状态不依赖业务 UI，错误始终能定位到实际资源包。
            StartupLoading.ReportPackage(EStartupLoadingStage.PreparingPackages,
                "初始化资源包 " + packageName, packageName);
            var logStartup = FrameworkBootstrap.IsStarting;
            var packageStartedAt = FrameworkStartupLog.Now;
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
            if (logStartup)
                FrameworkStartupLog.Step($"准备资源包 {packageName} ({ResourceSettings.PlayMode})", packageStartedAt);

            var operationStartedAt = FrameworkStartupLog.Now;
            var operation = package.InitializeAsync(parameters);
            await YooAssetStartupOperations.WaitAsync(operation, cancellationToken);
            if (operation.Status != EOperationStatus.Succeed)
                throw new InvalidOperationException($"Failed to initialize package {packageName}: {operation.Error}");
            if (logStartup)
                FrameworkStartupLog.Step($"初始化资源包 {packageName}", operationStartedAt);

            StartupLoading.ReportPackage(EStartupLoadingStage.PreparingPackages,
                "获取资源包版本 " + packageName, packageName, 1f / 3f);
            var versionStartedAt = FrameworkStartupLog.Now;
            string packageVersion;
            if (ResourceSettings.PlayMode == ResourcePlayMode.Host && ResourceSettings.PackageVersionProvider != null)
            {
                packageVersion = ResourceSettings.PackageVersionProvider(packageName);
            }
            else
            {
                var version = package.RequestPackageVersionAsync();
                await YooAssetStartupOperations.WaitAsync(version, cancellationToken);
                if (version.Status != EOperationStatus.Succeed)
                    throw new InvalidOperationException(
                        $"Failed to request package version {packageName}: {version.Error}");
                packageVersion = version.PackageVersion;
            }
            if (logStartup)
                FrameworkStartupLog.Step($"获取资源包 {packageName} 版本", versionStartedAt);

            StartupLoading.ReportPackage(EStartupLoadingStage.PreparingPackages,
                "激活资源包清单 " + packageName, packageName, 2f / 3f);
            var manifestStartedAt = FrameworkStartupLog.Now;
            var manifest = package.UpdatePackageManifestAsync(packageVersion);
            await YooAssetStartupOperations.WaitAsync(manifest, cancellationToken);
            if (manifest.Status != EOperationStatus.Succeed)
                throw new InvalidOperationException(
                    $"Failed to activate package manifest {packageName}/{packageVersion}: {manifest.Error}");
            if (logStartup)
                FrameworkStartupLog.Step($"激活资源包 {packageName} 清单", manifestStartedAt);

            // Host 模式先下载固定清单里的缺失文件，再允许业务加载资源或 DLL。
            if (ResourceSettings.PlayMode == ResourcePlayMode.Host)
            {
                await YooAssetStartupOperations.DownloadAsync(package, cancellationToken);
            }
            _packages.Add(packageName, package);
            if (packageName == ResourceSettings.DefaultPackageName)
                YooAssets.SetDefaultPackage(package);
            if (logStartup)
                FrameworkStartupLog.Step($"资源包 {packageName} 就绪", packageStartedAt);
        }

        /// <summary>加载共享资源，并为当前调用者建立独立引用。</summary>
        /// <param name="location">资源地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <param name="progressCallback">当前调用者的进度通知。</param>
        /// <param name="cancellationToken">调用者与资源服务的联合取消令牌。</param>
        /// <returns>共享资源的独立租约。</returns>
        /// <exception cref="InvalidOperationException">资源加载失败或资源类型不匹配。</exception>
        public async UniTask<IAssetHandle<T>> LoadAssetAsync<T>(string location, string packageName,
            Action<float> progressCallback, CancellationToken cancellationToken) where T : UnityEngine.Object
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
                // 每个等待者只持有自己的引用，取消不能终止其他等待者的共享加载。
                while (!shared.Handle.IsDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progressCallback?.Invoke(shared.Handle.Progress);
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (shared.Handle.Status != EOperationStatus.Succeed)
                    throw new InvalidOperationException(
                        $"Failed to load {packageName}/{location}: {shared.Handle.LastError}");

                var asset = shared.Handle.GetAssetObject<T>();
                if (asset == null)
                    throw new InvalidOperationException(
                        $"Asset has wrong type or is null: {packageName}/{location} ({typeof(T).Name})");

                progressCallback?.Invoke(1f);
                cancellationToken.ThrowIfCancellationRequested();
                return new AssetLease<T>(this, key, shared, asset);
            }
            catch
            {
                Release(key, shared);
                throw;
            }
        }

        /// <summary>加载预制体并创建实例，取消时回收未交付的实例和资源引用。</summary>
        /// <param name="location">预制体地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <param name="parent">实例父节点。</param>
        /// <param name="progressCallback">当前请求的进度通知。</param>
        /// <param name="cancellationToken">联合取消令牌。</param>
        /// <returns>实例及其预制体引用的租约。</returns>
        public async UniTask<IInstanceHandle> InstantiateAsync(string location, string packageName, Transform parent,
            Action<float> progressCallback, CancellationToken cancellationToken)
        {
            var prefab = await LoadAssetAsync<GameObject>(
                location, packageName, progressCallback, cancellationToken);
            GameObject instance = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                instance = UnityEngine.Object.Instantiate(prefab.Asset, parent);
                // Awake 等同步业务回调可能提出关闭，交付前再次检查取消。
                cancellationToken.ThrowIfCancellationRequested();
                return new InstanceLease(instance, prefab);
            }
            catch
            {
                if (instance != null) UnityEngine.Object.Destroy(instance);
                prefab.Dispose();
                throw;
            }
        }

        /// <summary>加载场景，取消后等待底层操作结束并卸载未交付的场景。</summary>
        /// <param name="location">场景地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <param name="mode">场景加载模式。</param>
        /// <param name="progressCallback">当前请求的进度通知。</param>
        /// <param name="cancellationToken">联合取消令牌。</param>
        /// <returns>加载成功的场景租约。</returns>
        /// <exception cref="InvalidOperationException">场景加载失败。</exception>
        public async UniTask<ISceneHandle> LoadSceneAsync(string location, string packageName, LoadSceneMode mode,
            Action<float> progressCallback, CancellationToken cancellationToken)
        {
            ValidateLocation(location);
            cancellationToken.ThrowIfCancellationRequested();
            var handle = GetPackage(packageName).LoadSceneAsync(location, mode);
            try
            {
                while (!handle.IsDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progressCallback?.Invoke(handle.Progress);
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (handle.Status != EOperationStatus.Succeed)
                    throw new InvalidOperationException(
                        $"Failed to load scene {packageName}/{location}: {handle.LastError}");
                progressCallback?.Invoke(1f);
                cancellationToken.ThrowIfCancellationRequested();
                return new SceneLease(handle);
            }
            catch
            {
                if (handle.IsValid && !handle.IsDone)
                    while (handle.IsValid && !handle.IsDone) await UniTask.Yield(PlayerLoopTiming.Update);

                if (handle.IsValid)
                {
                    if (handle.Status == EOperationStatus.Succeed &&
                        handle.SceneObject.IsValid() && handle.SceneObject.isLoaded)
                    {
                        var unload = handle.UnloadAsync();
                        while (!unload.IsDone) await UniTask.Yield(PlayerLoopTiming.Update);
                    }
                    else
                    {
                        handle.Release();
                    }
                }
                throw;
            }
        }

        /// <summary>等待不可撤销的闲置资源清理结束后，再报告取消或执行结果。</summary>
        /// <param name="packageName">资源包名称。</param>
        /// <param name="cancellationToken">联合取消令牌。</param>
        /// <returns>底层清理退出的任务。</returns>
        /// <exception cref="InvalidOperationException">底层资源清理失败。</exception>
        public async UniTask UnloadUnusedAsync(string packageName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = GetPackage(packageName).UnloadUnusedAssetsAsync();
            while (!operation.IsDone) await UniTask.Yield(PlayerLoopTiming.Update);
            cancellationToken.ThrowIfCancellationRequested();
            if (operation.Status != EOperationStatus.Succeed)
                throw new InvalidOperationException(
                    $"Failed to unload unused assets from {packageName}: {operation.Error}");
        }

        public IHotUpdateProvider CreateHotUpdateProvider(string packageName)
        {
            if (ResourceSettings.PlayMode != ResourcePlayMode.Host)
                throw new InvalidOperationException("Hot update requires Host resource mode.");
            return new YooAssetHotUpdateProvider(GetPackage(packageName));
        }

        /// <summary>归还共享加载句柄并销毁底层资源系统；正常关闭由包装层先等待请求排空。</summary>
        public override void Shutdown()
        {
            foreach (var shared in new List<SharedAsset>(_assets.Values))
            {
                // 单个句柄释放失败不能跳过其余句柄和全局资源销毁。
                try { if (shared.Handle.IsValid) shared.Handle.Release(); }
                catch (Exception exception) { AlloyDebug.Error(exception); }
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
            private readonly YooAssetService m_owner; // 归还当前租约引用的资源实现。
            private readonly AssetKey m_key; // 共享资源索引。
            private readonly SharedAsset m_shared; // 多个请求共享的实际加载。
            private T m_asset; // 当前租约可访问的资源。
            private bool m_disposed; // 当前租约是否已经归还。

            internal AssetLease(YooAssetService owner, AssetKey key, SharedAsset shared, T asset)
            {
                m_owner = owner;
                m_key = key;
                m_shared = shared;
                m_asset = asset;
            }

            public T Asset => !IsDisposed ? m_asset : throw new ObjectDisposedException(nameof(AssetLease<T>));
            public bool IsDisposed => m_disposed || !m_shared.Handle.IsValid;

            /// <summary>只归还当前租约的引用，重复归还不会影响其他持有者。</summary>
            public void Dispose()
            {
                if (m_disposed) return;
                m_disposed = true;
                m_asset = null;
                m_owner.Release(m_key, m_shared);
            }
        }

        private sealed class InstanceLease : IInstanceHandle
        {
            private bool m_disposed; // 当前实例是否已完成显式归还。
            private readonly IAssetHandle<GameObject> _prefab;
            private GameObject _instance;
            /// <summary>绑定实例与其预制体引用。</summary>
            /// <param name="instance">场景实例。</param>
            /// <param name="prefab">实例依赖的预制体租约。</param>
            public InstanceLease(GameObject instance, IAssetHandle<GameObject> prefab)
            {
                _instance = instance;
                _prefab = prefab;
            }
            public GameObject Instance => !IsDisposed
                ? _instance : throw new ObjectDisposedException(nameof(InstanceLease));
            public bool IsDisposed => m_disposed || _instance == null || _prefab.IsDisposed;
            /// <summary>销毁实例并归还预制体引用，服务已关闭时也允许安全重复调用。</summary>
            public void Dispose()
            {
                if (m_disposed) return;
                m_disposed = true;
                if (_instance != null) UnityEngine.Object.Destroy(_instance);
                _instance = null;
                _prefab.Dispose();
            }
        }

        private sealed class SceneLease : ISceneHandle
        {
            private bool m_unloaded; // 场景是否已显式卸载完成。
            private readonly YooAsset.SceneHandle _handle;
            public SceneLease(YooAsset.SceneHandle handle) { _handle = handle; }
            public Scene Scene => !IsUnloaded ? _handle.SceneObject : default;
            public bool IsUnloaded => m_unloaded || !_handle.IsValid;
            /// <summary>等待场景卸载完成，已经失效的底层句柄按已卸载处理。</summary>
            /// <returns>场景卸载完成的任务。</returns>
            public async UniTask UnloadAsync()
            {
                if (IsUnloaded) return;
                if (!_handle.IsValid)
                {
                    m_unloaded = true;
                    return;
                }
                if (!_handle.SceneObject.IsValid() || !_handle.SceneObject.isLoaded)
                {
                    _handle.Release();
                    m_unloaded = true;
                    return;
                }
                var operation = _handle.UnloadAsync();
                while (!operation.IsDone) await UniTask.Yield(PlayerLoopTiming.Update);
                if (operation.Status != EOperationStatus.Succeed)
                    throw new InvalidOperationException($"Failed to unload scene: {operation.Error}");
                m_unloaded = true;
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
