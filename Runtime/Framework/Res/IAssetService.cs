using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AlloyFramework
{
    public interface IAssetService : IFrameworkSystem
    {
        int TrackedAssetCount { get; }

        UniTask EnsurePackageAsync(string packageName, CancellationToken cancellationToken);

        UniTask<IAssetHandle<T>> LoadAssetAsync<T>(string location, string packageName,
            Action<float> progressCallback, CancellationToken cancellationToken)
            where T : UnityEngine.Object;

        UniTask<IInstanceHandle> InstantiateAsync(string location, string packageName, Transform parent,
            Action<float> progressCallback, CancellationToken cancellationToken);

        UniTask<ISceneHandle> LoadSceneAsync(string location, string packageName, LoadSceneMode mode,
            Action<float> progressCallback, CancellationToken cancellationToken);

        /// <summary>
        /// 尝试清理指定资源所在 Bundle 及其依赖中的闲置资源，不归还调用者持有的句柄引用。
        /// </summary>
        /// <param name="location">用于定位清理范围的资源地址。</param>
        /// <param name="packageName">已初始化的资源包名称。</param>
        /// <remarks>仅限主线程调用；同步尝试清理，不保证资源立即卸载，也不限定只清理该资源。</remarks>
        /// <exception cref="ArgumentException">资源地址或资源包名称为空。</exception>
        /// <exception cref="InvalidOperationException">资源服务或指定资源包尚未就绪。</exception>
        void TryUnloadUnusedAsset(string location, string packageName);

        UniTask UnloadUnusedAsync(string packageName, CancellationToken cancellationToken);
    }
}
