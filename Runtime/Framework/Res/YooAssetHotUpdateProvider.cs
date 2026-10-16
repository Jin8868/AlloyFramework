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

        /// <summary>等待该包直读租约归还后清理旧版本缓存，清理期间阻止新的直读。</summary>
        /// <param name="cancellationToken">等待清理预留时的取消令牌。</param>
        /// <returns>缓存清理任务；已启动的清理完成后才归还互斥预留。</returns>
        public async UniTask ClearUnusedCacheAsync(CancellationToken cancellationToken = default)
        {
            using (await RawFileReadGate.ReserveCleanupAsync(PackageName, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var operation = _package.ClearCacheFilesAsync(EFileClearMode.ClearUnusedBundleFiles);
                // YooAssets 清理操作不能撤销，取消也不能提前放行新的文件读取。
                while (!operation.IsDone) { await UniTask.Yield(PlayerLoopTiming.Update); }
                RequireSuccess(operation.Status, operation.Error, "clear unused cache");
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        private void RequireSuccess(EOperationStatus status, string error, string action)
        {
            if (status != EOperationStatus.Succeed)
                throw new InvalidOperationException($"Failed to {action} for {PackageName}: {error}");
        }
    }
}
