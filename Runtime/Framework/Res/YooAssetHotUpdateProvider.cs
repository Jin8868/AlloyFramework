using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using YooAsset;

namespace AlloyFramework
{
    internal sealed class YooAssetHotUpdateProvider : IHotUpdateProvider
    {
        private readonly ResourcePackage m_package; // 热更新操作所属的资源包。
        private bool m_running; // 是否已有热更新操作在执行。

        /// <summary>绑定资源包，生命周期由资源服务包装层管理。</summary>
        /// <param name="package">资源包。</param>
        public YooAssetHotUpdateProvider(ResourcePackage package)
        {
            m_package = package;
        }

        public string PackageName => m_package.PackageName;
        public HotUpdateState State { get; private set; }
        public bool NeedUpdate { get; private set; }
        public int TotalDownloadCount { get; private set; }
        public long TotalDownloadBytes { get; private set; }

        public event Action<HotUpdateProgress> ProgressChanged;
        public event Action<string, string> FileError;

        /// <summary>执行版本检查、清单更新和资源下载，并在退出时解除下载回调。</summary>
        /// <param name="cancellationToken">当前请求和资源服务的联合取消令牌。</param>
        /// <returns>热更新及底层操作退出的任务。</returns>
        /// <exception cref="InvalidOperationException">已有更新正在执行或底层操作失败。</exception>
        public async UniTask RunAsync(CancellationToken cancellationToken = default)
        {
            if (m_running) throw new InvalidOperationException($"Hot update is already running: {PackageName}");
            m_running = true;
            NeedUpdate = false;
            TotalDownloadCount = 0;
            TotalDownloadBytes = 0;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                State = HotUpdateState.GettingVersion;
                var version = m_package.RequestPackageVersionAsync();
                await YooAssetStartupOperations.WaitAsync(version, cancellationToken);
                RequireSuccess(version.Status, version.Error, "request version");

                State = HotUpdateState.UpdatingManifest;
                var manifest = m_package.UpdatePackageManifestAsync(version.PackageVersion);
                await YooAssetStartupOperations.WaitAsync(manifest, cancellationToken);
                RequireSuccess(manifest.Status, manifest.Error, "update manifest");

                var downloader = m_package.CreateResourceDownloader(
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
                downloader.DownloadUpdateCallback = ReportProgress;
                downloader.DownloadErrorCallback = ReportFileError;
                try
                {
                    downloader.BeginDownload();
                    await YooAssetStartupOperations.WaitAsync(downloader, cancellationToken);
                    RequireSuccess(downloader.Status, downloader.Error, "download resources");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    downloader.CancelDownload();
                    throw;
                }
                finally
                {
                    if (!downloader.IsDone) downloader.CancelDownload();
                    downloader.DownloadUpdateCallback = null;
                    downloader.DownloadErrorCallback = null;
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
                m_running = false;
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
                var operation = m_package.ClearCacheFilesAsync(EFileClearMode.ClearUnusedBundleFiles);
                // YooAssets 清理操作不能撤销，取消也不能提前放行新的文件读取。
                while (!operation.IsDone) { await UniTask.Yield(PlayerLoopTiming.Update); }
                RequireSuccess(operation.Status, operation.Error, "clear unused cache");
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        private void ReportProgress(DownloadUpdateData progress)
        {
            ProgressChanged?.Invoke(new HotUpdateProgress(
                PackageName, progress.Progress, progress.CurrentDownloadBytes, progress.TotalDownloadBytes));
        }

        private void ReportFileError(DownloadErrorData error)
        {
            FileError?.Invoke(error.FileName, error.ErrorInfo);
        }

        private void RequireSuccess(EOperationStatus status, string error, string action)
        {
            if (status != EOperationStatus.Succeed)
                throw new InvalidOperationException($"Failed to {action} for {PackageName}: {error}");
        }
    }
}
