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
            IProgress<float> progress, CancellationToken cancellationToken)
            where T : UnityEngine.Object;

        UniTask<IInstanceHandle> InstantiateAsync(string location, string packageName, Transform parent,
            IProgress<float> progress, CancellationToken cancellationToken);

        UniTask<ISceneHandle> LoadSceneAsync(string location, string packageName, LoadSceneMode mode,
            IProgress<float> progress, CancellationToken cancellationToken);

        UniTask UnloadUnusedAsync(string packageName, CancellationToken cancellationToken);
    }
}
