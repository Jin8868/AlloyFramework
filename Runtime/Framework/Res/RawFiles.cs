using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace AlloyFramework
{
    public interface IRawFileService
    {
        /// <summary>加载资源包中的原始文件并返回有效租约。</summary>
        /// <param name="location">资源地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>保持来源有效的原始文件读取租约；仅无物理文件时落地。</returns>
        UniTask<RawFileLease> LoadRawFileAsync(string location, string packageName,
            CancellationToken cancellationToken);
    }

    public sealed class RawFileLease : IDisposable
    {
        private readonly string m_directory; // 仅由本租约持有的快照目录。
        private bool m_disposed; // 幂等释放标志。
        private readonly IDisposable m_sourceOwner; // 直读时持有的资源包句柄，不删除源文件。
        private readonly IDisposable m_readReservation; // 防止框架缓存清理与原生读取交错。
        private readonly FileStream m_readGuard; // 保持只读句柄；文件删除由缓存预留协调。
        public string FilePath { get; }
        public bool IsValid => !m_disposed;
        /// <summary>是否直接借用资源包提供的物理文件。</summary>
        public bool IsPackageFile => m_sourceOwner != null;

        internal RawFileLease(string directory, string filePath)
        {
            m_directory = directory;
            FilePath = filePath;
        }

        private RawFileLease(string filePath, IDisposable sourceOwner, IDisposable readReservation)
        {
            FilePath = Path.GetFullPath(filePath);
            m_sourceOwner = sourceOwner;
            m_readReservation = readReservation;
            m_readGuard = new FileStream(FilePath, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete);
        }

        internal static RawFileLease Borrow(string filePath, IDisposable sourceOwner = null,
            IDisposable readReservation = null)
        {
            return new RawFileLease(filePath, sourceOwner, readReservation);
        }

        /// <summary>归还读取句柄；仅删除本租约自己落地的临时文件，不删除借用文件。</summary>
        public void Dispose()
        {
            if (m_disposed) { return; }
            m_disposed = true;
            try { m_readGuard?.Dispose(); }
            finally
            {
                try { m_sourceOwner?.Dispose(); }
                finally { m_readReservation?.Dispose(); }
            }
            if (!string.IsNullOrEmpty(m_directory) && Directory.Exists(m_directory))
            { Directory.Delete(m_directory, true); }
        }

        internal static RawFileLease Create(byte[] bytes)
        {
            string directory = Path.Combine(Application.persistentDataPath,
                "AlloyRawFiles", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "content.raw");
            try
            {
                File.WriteAllBytes(path, bytes);
                return new RawFileLease(directory, path);
            }
            catch
            {
                Directory.Delete(directory, true);
                throw;
            }
        }

        internal static RawFileLease Copy(string sourcePath)
        {
            string directory = Path.Combine(Application.persistentDataPath,
                "AlloyRawFiles", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "content.raw");
            try { File.Copy(sourcePath, path, false); return new RawFileLease(directory, path); }
            catch { Directory.Delete(directory, true); throw; }
        }

        internal static UniTask<RawFileLease> TryLoadLocalAsync(
            string location, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(location)) { throw new ArgumentException("原始文件地址不能为空。", nameof(location)); }
            cancellationToken.ThrowIfCancellationRequested();

            // 本地文件直接借用，只有压缩包内部等无物理路径来源才需要落地。
            if (location.StartsWith("file:", StringComparison.OrdinalIgnoreCase) &&
                Uri.TryCreate(location, UriKind.Absolute, out Uri localAddress) && localAddress.IsFile &&
                File.Exists(localAddress.LocalPath))
            {
                return UniTask.FromResult(Borrow(localAddress.LocalPath));
            }
            if (location.StartsWith("jar:", StringComparison.Ordinal) ||
                location.StartsWith("file:", StringComparison.Ordinal))
            {
                return LoadAddressAsync(location, cancellationToken);
            }

            if (Path.IsPathRooted(location))
            {
                return UniTask.FromResult(Borrow(location));
            }


            return UniTask.FromResult<RawFileLease>(null);
        }

        private static async UniTask<RawFileLease> LoadAddressAsync(
            string location, CancellationToken cancellationToken)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(location))
            {
                try
                {
                    // 压缩包或非物理地址独立持有请求，取消时显式终止下载再归还操作。
                    UnityWebRequestAsyncOperation requestOperation = request.SendWebRequest();
                    while (!requestOperation.isDone)
                        await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (request.result != UnityWebRequest.Result.Success) throw new IOException(request.error);
                    return Create(request.downloadHandler.data);
                }
                catch
                {
                    request.Abort();
                    throw;
                }
            }
        }

        internal static async UniTask<RawFileLease> CopyAsync(string sourcePath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = Path.Combine(Application.persistentDataPath,
                "AlloyRawFiles", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "content.raw");
            try
            {
                // 大视频按块异步复制，避免主线程 File.Copy 阻塞界面，也不整体分配 byte[]。
                using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 65536, FileOptions.Asynchronous))
                {
                    await source.CopyToAsync(destination, 65536, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                return new RawFileLease(directory, path);
            }
            catch
            {
                Directory.Delete(directory, true);
                throw;
            }
        }
    }

    public sealed partial class ResourceManager
    {
        /// <summary>通过框架借用原始文件；已有物理文件直接读取，APK 内等非物理地址按需落地。</summary>
        /// <param name="location">资源包地址、本地绝对路径或 jar/file 地址。</param>
        /// <param name="packageName">非本地地址所属资源包。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>供音频或其他原生模块使用的文件租约。</returns>
        /// <exception cref="ArgumentException">地址为空。</exception>
        /// <exception cref="NotSupportedException">资源实现不支持原始文件。</exception>
        public async UniTask<RawFileLease> LoadRawFileAsync(string location,
            string packageName = ResourceSettings.DefaultPackageName, CancellationToken cancellationToken = default)
        {
            if (!(GetService() is IRawFileService rawFileService))
            {
                throw new NotSupportedException("当前资源实现不支持原始文件加载。");
            }
            return await rawFileService.LoadRawFileAsync(location, packageName, cancellationToken);
        }
    }

    internal sealed partial class YooAssetService : IRawFileService
    {
        /// <summary>通过资源包借用原始文件，读取结束前保持加载句柄和缓存预留。</summary>
        /// <param name="location">资源地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>保持资源缓存文件有效的读取租约。</returns>
        public async UniTask<RawFileLease> LoadRawFileAsync(string location, string packageName,
            CancellationToken cancellationToken)
        {
            ValidateLocation(location);
            cancellationToken.ThrowIfCancellationRequested();
            YooAsset.RawFileHandle handle = null;
            IDisposable reservation = await RawFileReadGate.ReserveReadAsync(packageName, cancellationToken);
            try
            {
                // 加载完成后将句柄所有权交给读取租约。
                handle = GetPackage(packageName).LoadRawFileAsync(location);
                while (!handle.IsDone) { await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken); }
                cancellationToken.ThrowIfCancellationRequested();
                if (handle.Status != YooAsset.EOperationStatus.Succeed) { throw new IOException(handle.LastError); }

                // 物理文件直接借用，资源句柄和缓存预留随租约一起转交。
                string physicalPath = handle.GetRawFilePath();
                if (File.Exists(physicalPath))
                {
                    RawFileLease lease = RawFileLease.Borrow(physicalPath, handle, reservation);
                    handle = null;
                    reservation = null;
                    return lease;
                }

                // 无物理路径时才落地，生成文件由租约独占并在归还后删除。
                byte[] bytes = handle.GetRawFileData();
                if (bytes == null) { throw new IOException($"原始文件数据为空：{location}"); }
                RawFileLease snapshot = RawFileLease.Create(bytes);
                return snapshot;
            }
            finally
            {
                try { handle?.Release(); }
                finally { reservation?.Dispose(); }
            }
        }
    }
}
