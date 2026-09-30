using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using YooAsset;

namespace AlloyFramework
{
    internal sealed class YooAssetHotUpdateProvider : IHotUpdateProvider
    {
        private readonly ResourcePackage _package;
        private bool _running;

        public YooAssetHotUpdateProvider(ResourcePackage package)
        {
            _package = package;
        }

        public string PackageName => _package.PackageName;
        public HotUpdateState State { get; private set; }
        public bool NeedUpdate { get; private set; }
        public int TotalDownloadCount { get; private set; }
        public long TotalDownloadBytes { get; private set; }

        public event Action<HotUpdateProgress> ProgressChanged;
        public event Action<string, string> FileError;

        public async UniTask RunAsync(CancellationToken cancellationToken = default)
        {
            if (_running) throw new InvalidOperationException($"Hot update is already running: {PackageName}");
            _running = true;
            NeedUpdate = false;
            TotalDownloadCount = 0;
            TotalDownloadBytes = 0;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                State = HotUpdateState.GettingVersion;
                var version = _package.RequestPackageVersionAsync();
                await UniTask.WaitUntil(() => version.IsDone, cancellationToken: cancellationToken);
                RequireSuccess(version.Status, version.Error, "request version");

                State = HotUpdateState.UpdatingManifest;
                var manifest = _package.UpdatePackageManifestAsync(version.PackageVersion);
                await UniTask.WaitUntil(() => manifest.IsDone, cancellationToken: cancellationToken);
                RequireSuccess(manifest.Status, manifest.Error, "update manifest");

                var downloader = _package.CreateResourceDownloader(
                    ResourceSettings.DownloadConcurrency,
                    ResourceSettings.DownloadRetryCount);
                TotalDownloadCount = downloader.TotalDownloadCount;
                TotalDownloadBytes = downloader.TotalDownloadBytes;
                NeedUpdate = TotalDownloadCount > 0;
                if (!NeedUpdate)
                {
                    State = HotUpdateState.Completed;
                    ProgressChanged?.Invoke(new HotUpdateProgress(PackageName, 1f, 0, 0));
                    return;
                }

                State = HotUpdateState.Downloading;
                downloader.DownloadUpdateCallback = data => ProgressChanged?.Invoke(
                    new HotUpdateProgress(PackageName, data.Progress, data.CurrentDownloadBytes, data.TotalDownloadBytes));
                downloader.DownloadErrorCallback = data => FileError?.Invoke(data.FileName, data.ErrorInfo);
                downloader.BeginDownload();
                try
                {
                    await UniTask.WaitUntil(() => downloader.IsDone, cancellationToken: cancellationToken);
                    RequireSuccess(downloader.Status, downloader.Error, "download resources");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    downloader.CancelDownload();
                    throw;
                }

                State = HotUpdateState.Completed;
                ProgressChanged?.Invoke(new HotUpdateProgress(PackageName, 1f, TotalDownloadBytes, TotalDownloadBytes));
            }
            catch
            {
                State = HotUpdateState.Failed;
                throw;
            }
            finally
            {
                _running = false;
            }
        }

        public async UniTask ClearUnusedCacheAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = _package.ClearCacheFilesAsync(EFileClearMode.ClearUnusedBundleFiles);
            await UniTask.WaitUntil(() => operation.IsDone, cancellationToken: cancellationToken);
            RequireSuccess(operation.Status, operation.Error, "clear unused cache");
        }

        private void RequireSuccess(EOperationStatus status, string error, string action)
        {
            if (status != EOperationStatus.Succeed)
                throw new InvalidOperationException($"Failed to {action} for {PackageName}: {error}");
        }
    }
}
