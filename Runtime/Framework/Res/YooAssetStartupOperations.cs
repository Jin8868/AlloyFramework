using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace AlloyFramework
{
    internal static class YooAssetStartupOperations
    {
        internal static async UniTask WaitAsync(AsyncOperationBase operation, CancellationToken cancellationToken)
        {
            try
            {
                // 按无进度时间超时，不限制持续产生进度的大文件下载总时长。
                float previousProgress = operation.Progress;
                float lastProgressTime = Time.realtimeSinceStartup;
                while (!operation.IsDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (operation.Progress != previousProgress)
                    {
                        previousProgress = operation.Progress;
                        lastProgressTime = Time.realtimeSinceStartup;
                    }
                    if (Time.realtimeSinceStartup - lastProgressTime > ResourceSettings.OperationTimeoutSeconds)
                    {
                        throw new TimeoutException("资源启动操作等待超时，请检查服务器连接并重启。");
                    }
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();

            }
            finally
            {
                // 取消外层等待不代表底层退出；下载可终止，其余操作保持服务有效直到完成。
                if (!operation.IsDone && operation is DownloaderOperation downloader) downloader.CancelDownload();
                while (!operation.IsDone) await UniTask.Yield(PlayerLoopTiming.Update);
            }
        }

        internal static async UniTask DownloadAsync(ResourcePackage package, CancellationToken cancellationToken)
        {
            var downloader = package.CreateResourceDownloader(
                ResourceSettings.DownloadConcurrency, ResourceSettings.DownloadRetryCount);
            // 显式上下文隔离旧轮次和控制方，取消后的下载回调不能覆盖新一轮。
            var progressReporter = new DownloadProgressReporter(StartupLoading.Snapshot);
            downloader.DownloadUpdateCallback = progressReporter.Report;
            StartupLoading.ReportPackage(EStartupLoadingStage.DownloadingContent,
                "下载资源包 " + package.PackageName, package.PackageName, 0f, 0, downloader.TotalDownloadBytes);
            Debug.Log($"[HotUpdate] {package.PackageName} 缺失文件={downloader.TotalDownloadCount}，" +
                $"下载字节={downloader.TotalDownloadBytes}");
            try
            {
                if (downloader.TotalDownloadCount == 0)
                {
                    Debug.Log("[HotUpdate] 缓存命中或空包，跳过下载：" + package.PackageName);
                    StartupLoading.ReportPackage(EStartupLoadingStage.DownloadingContent,
                        "资源包已就绪 " + package.PackageName, package.PackageName, 1f);
                    return;
                }
                downloader.BeginDownload();
                await WaitAsync(downloader, cancellationToken);
                if (downloader.Status != EOperationStatus.Succeed)
                {
                    throw new InvalidOperationException($"资源下载失败：{package.PackageName}，{downloader.Error}");
                }
            }
            finally
            {
                // 取消或超时同时终止底层下载，不能只结束外层等待。
                if (!downloader.IsDone)
                {
                    downloader.CancelDownload();
                }
                downloader.DownloadUpdateCallback = null;
            }
        }

        private static void ReportProgress(DownloadUpdateData progress)
        {
            // 下载比例优先使用真实字节；未知总量只显示阶段提示。
            float fraction = progress.TotalDownloadBytes > 0
                ? (float)((double)progress.CurrentDownloadBytes / progress.TotalDownloadBytes) : 0f;
            StartupLoading.ReportPackage(EStartupLoadingStage.DownloadingContent,
                "下载资源包 " + progress.PackageName, progress.PackageName, fraction,
                progress.CurrentDownloadBytes, progress.TotalDownloadBytes);
            ResourceSettings.PackageDownloadProgress?.Invoke(
                progress.PackageName, progress.Progress, progress.CurrentDownloadBytes, progress.TotalDownloadBytes);
        }

        private sealed class DownloadProgressReporter
        {
            private readonly int m_round; // 下载创建时所属的启动轮次。
            private readonly EStartupLoadingOwner m_owner; // 下载创建时的控制方。

            internal DownloadProgressReporter(StartupLoadingSnapshot snapshot)
            {
                m_round = snapshot?.Round ?? -1;
                m_owner = snapshot?.Owner ?? EStartupLoadingOwner.Closed;
            }

            /// <summary>仅转发当前启动轮次及控制方的真实下载回调。</summary>
            /// <param name="progress">底层下载进度。</param>
            public void Report(DownloadUpdateData progress)
            {
                var snapshot = StartupLoading.Snapshot;
                if (snapshot == null || snapshot.Round != m_round || snapshot.Owner != m_owner ||
                    snapshot.Stage == EStartupLoadingStage.Failed ||
                    snapshot.Stage == EStartupLoadingStage.Cancelled)
                {
                    return;
                }
                ReportProgress(progress);
            }
        }
    }
}
