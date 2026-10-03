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

        UniTask UnloadUnusedAsync(string packageName, CancellationToken cancellationToken);
    }
}
