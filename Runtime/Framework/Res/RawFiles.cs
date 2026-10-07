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
        /// <returns>具有独立本地快照的原始文件租约。</returns>
        UniTask<RawFileLease> LoadRawFileAsync(string location, string packageName,
            CancellationToken cancellationToken);
    }

    public sealed class RawFileLease : IDisposable
    {
        private readonly string m_directory; // 仅由本租约持有的快照目录。
        private bool m_disposed; // 幂等释放标志。
        public string FilePath { get; }
        public bool IsValid => !m_disposed;

        internal RawFileLease(string directory, string filePath)
        {
            m_directory = directory;
            FilePath = filePath;
        }

        /// <summary>释放本租约独占的文件快照。</summary>
        public void Dispose()
        {
            if (m_disposed) { return; }
            m_disposed = true;
            if (Directory.Exists(m_directory)) { Directory.Delete(m_directory, true); }
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
    }

    public sealed partial class ResourceManager
    {
        /// <summary>通过框架加载原始文件；本地绝对地址和 APK 内地址均交付为独立本地快照。</summary>
        /// <param name="location">资源包地址、本地绝对路径或 jar/file 地址。</param>
        /// <param name="packageName">非本地地址所属资源包。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>供音频或其他原生模块使用的文件租约。</returns>
        /// <exception cref="ArgumentException">地址为空。</exception>
        /// <exception cref="NotSupportedException">资源实现不支持原始文件。</exception>
        public async UniTask<RawFileLease> LoadRawFileAsync(string location,
            string packageName = ResourceSettings.DefaultPackageName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(location)) { throw new ArgumentException("原始文件地址不能为空。", nameof(location)); }
            cancellationToken.ThrowIfCancellationRequested();

            // 本地制作资源和安装包内文件也通过统一资源入口交付，不由音频模块自行持有源文件。
            if (location.StartsWith("jar:", StringComparison.Ordinal) ||
                location.StartsWith("file:", StringComparison.Ordinal))
            {
                using (UnityWebRequest request = UnityWebRequest.Get(location))
                {
                    request.SendWebRequest();
                    while (!request.isDone) { await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken); }
                    if (request.result != UnityWebRequest.Result.Success) { throw new IOException(request.error); }
                    cancellationToken.ThrowIfCancellationRequested();
                    return RawFileLease.Create(request.downloadHandler.data);
                }
            }

            if (Path.IsPathRooted(location))
            {
                return RawFileLease.Copy(location);
            }

            if (!(GetService() is IRawFileService rawFileService))
            {
                throw new NotSupportedException("当前资源实现不支持原始文件加载。");
            }
            return await rawFileService.LoadRawFileAsync(location, packageName, cancellationToken);
        }
    }

    internal sealed partial class YooAssetService : IRawFileService
    {
        /// <summary>通过资源包加载原始文件并创建稳定的本地快照。</summary>
        /// <param name="location">资源地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>不依赖资源缓存文件名的原始文件租约。</returns>
        public async UniTask<RawFileLease> LoadRawFileAsync(string location, string packageName,
            CancellationToken cancellationToken)
        {
            ValidateLocation(location);
            cancellationToken.ThrowIfCancellationRequested();
            YooAsset.RawFileHandle handle = GetPackage(packageName).LoadRawFileAsync(location);
            try
            {
                // 在释放 YooAsset 句柄前复制数据，缓存清理不会使后续原生读取失效。
                while (!handle.IsDone) { await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken); }
                cancellationToken.ThrowIfCancellationRequested();
                if (handle.Status != YooAsset.EOperationStatus.Succeed) { throw new IOException(handle.LastError); }
                string physicalPath = handle.GetRawFilePath();
                if (File.Exists(physicalPath)) { return RawFileLease.Copy(physicalPath); }
                byte[] bytes = handle.GetRawFileData();
                if (bytes == null) { throw new IOException($"原始文件数据为空：{location}"); }
                return RawFileLease.Create(bytes);
            }
            finally { handle.Release(); }
        }
    }
}
