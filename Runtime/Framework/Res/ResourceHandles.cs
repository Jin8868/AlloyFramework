using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AlloyFramework
{
    public interface IAssetHandle<out T> : IDisposable where T : UnityEngine.Object
    {
        T Asset { get; }
        bool IsDisposed { get; }
    }

    public interface IInstanceHandle : IDisposable
    {
        GameObject Instance { get; }
        bool IsDisposed { get; }
    }

    public interface ISceneHandle
    {
        Scene Scene { get; }
        bool IsUnloaded { get; }
        UniTask UnloadAsync();
    }
}
