using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    public enum HotUpdateState
    {
        Idle,
        GettingVersion,
        UpdatingManifest,
        Downloading,
        Completed,
        Failed
    }

    public readonly struct HotUpdateProgress
    {
        public HotUpdateProgress(string packageName, float fraction, long downloadedBytes, long totalBytes)
        {
            PackageName = packageName;
            Fraction = fraction;
            DownloadedBytes = downloadedBytes;
            TotalBytes = totalBytes;
        }

        public string PackageName { get; }
        public float Fraction { get; }
        public long DownloadedBytes { get; }
        public long TotalBytes { get; }
    }

    public interface IHotUpdateProvider
    {
        string PackageName { get; }
        HotUpdateState State { get; }
        bool NeedUpdate { get; }
        int TotalDownloadCount { get; }
        long TotalDownloadBytes { get; }

        event Action<HotUpdateProgress> ProgressChanged;
        event Action<string, string> FileError;

        UniTask RunAsync(CancellationToken cancellationToken = default);
        UniTask ClearUnusedCacheAsync(CancellationToken cancellationToken = default);
    }

    public interface IHotUpdateService
    {
        IHotUpdateProvider CreateHotUpdateProvider(string packageName);
    }
}
