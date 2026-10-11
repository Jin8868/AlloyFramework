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

        internal static async UniTask DownloadAsync(ResourcePackage package, CancellationToken cancellationToken)
        {
            var downloader = package.CreateResourceDownloader(
                ResourceSettings.DownloadConcurrency, ResourceSettings.DownloadRetryCount);
            downloader.DownloadUpdateCallback = ReportProgress;
            Debug.Log($"[HotUpdate] {package.PackageName} 缺失文件={downloader.TotalDownloadCount}，" +
                $"下载字节={downloader.TotalDownloadBytes}");
            try
            {
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
            ResourceSettings.PackageDownloadProgress?.Invoke(
                progress.PackageName, progress.Progress, progress.CurrentDownloadBytes, progress.TotalDownloadBytes);
        }
    }
}
