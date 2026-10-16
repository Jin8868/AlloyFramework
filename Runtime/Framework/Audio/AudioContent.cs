using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework.Audio
{
    [Serializable]
    public sealed class AudioContentManifest
    {
        /// <summary>生成资源对应的平台。</summary>
        public string Platform;
        /// <summary>本清单包含的非 SFX 语言。</summary>
        public string Language;
        /// <summary>制作工具导出的后端版本，初始化时校验。</summary>
        public string BackendVersion;
        /// <summary>生成器和所有输出内容的版本摘要。</summary>
        public string Version;
        /// <summary>初始化资源组。</summary>
        public AudioContentGroup Init;
        /// <summary>可交付资源组。</summary>
        public AudioContentGroup[] Groups;
        /// <summary>事件到资源组映射。</summary>
        public AudioEventDependency[] Events;
    }

    [Serializable]
    public sealed class AudioEventDependency
    {
        /// <summary>音效人员提供的 Event 名称。</summary>
        public string Key;
        /// <summary>该事件依赖的框架资源组。</summary>
        public string[] Groups;
    }

    [Serializable]
    public sealed class AudioContentGroup
    {
        /// <summary>框架资源组名称。</summary>
        public string Key;
        /// <summary>组内全部原始文件。</summary>
        public AudioContentFile[] Files;
        /// <summary>需要原生加载的 Bank。</summary>
        public AudioBankDefinition[] Banks;
        /// <summary>需要准备独立媒体的事件名称。</summary>
        public string[] PreparedEvents;
        /// <summary>需要先加载并在本组卸载后才释放的共享依赖。</summary>
        public string[] Dependencies;
    }

    [Serializable]
    public sealed class AudioContentFile
    {
        /// <summary>交付目录内的相对路径。</summary>
        public string Path;
        /// <summary>交付前校验的 SHA256 摘要。</summary>
        public string Hash;
        /// <summary>可选的框架资源包地址，为空时使用制作目录中的相对路径。</summary>
        public string Location;
    }

    [Serializable]
    public sealed class AudioBankDefinition
    {
        /// <summary>传入后端的 Bank 名称。</summary>
        public string Name;
        /// <summary>相对文件路径。</summary>
        public string Path;
        /// <summary>Bank 原生类型。</summary>
        public EAudioBankType Type;
    }

    public interface IAudioContentProvider
    {
        AudioContentManifest Manifest { get; }

        /// <summary>通过框架资源系统准备组文件并保持它们有效。</summary>
        /// <param name="groupKey">组名称，Init 表示初始化组。</param>
        /// <param name="settings">平台和语言设置。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>尚未原生加载的文件租约。</returns>
        UniTask<AudioContentLease> PrepareGroupAsync(string groupKey, AudioSettings settings,
            CancellationToken cancellationToken);
    }

    public sealed class AudioContentLease : IDisposable
    {
        private readonly Dictionary<string, RawFileLease> m_files =
            new Dictionary<string, RawFileLease>(); // 组内原始文件租约，原生卸载后才归还。
        private bool m_disposed; // 保证本租约只释放一次。
        public AudioContentGroup Group { get; }
        /// <summary>旧式目录交付的根目录；直读交付请使用 GetFilePath。</summary>
        public string DirectoryPath { get; }
        public string Version { get; }
        public string BackendVersion { get; }

        internal AudioContentLease(AudioContentGroup group, string directoryPath, string version, string backendVersion)
        {
            Group = group;
            DirectoryPath = directoryPath;
            Version = version;
            BackendVersion = backendVersion;
        }

        /// <summary>返回组内文件的实际物理路径，不假定所有文件位于同一目录。</summary>
        /// <param name="relativePath">音频清单中的相对文件路径。</param>
        /// <returns>保持有效的本地物理文件路径。</returns>
        /// <exception cref="ObjectDisposedException">内容租约已释放。</exception>
        /// <exception cref="KeyNotFoundException">直读交付未包含该文件。</exception>
        public string GetFilePath(string relativePath)
        {
            if (m_disposed) { throw new ObjectDisposedException(nameof(AudioContentLease)); }
            relativePath = relativePath.Replace('\\', '/');
            if (m_files.TryGetValue(relativePath, out RawFileLease file)) { return file.FilePath; }
            if (!string.IsNullOrEmpty(DirectoryPath)) { return System.IO.Path.Combine(DirectoryPath, relativePath); }
            throw new KeyNotFoundException("音频内容未包含文件：" + relativePath);
        }

        internal void RetainFile(string relativePath, RawFileLease file)
        {
            m_files.Add(relativePath, file);
        }

        /// <summary>原生卸载完成后归还本组原始文件租约，不删除借用的资源文件。</summary>
        public void Dispose()
        {
            if (m_disposed) { return; }
            m_disposed = true;
            Exception firstFailure = null;
            // 单个归还失败也继续归还其余文件，避免永久占住资源包缓存清理。
            foreach (RawFileLease file in m_files.Values)
            {
                try { file.Dispose(); }
                catch (Exception exception) { if (firstFailure == null) { firstFailure = exception; } }
            }
            m_files.Clear();
            if (!string.IsNullOrEmpty(DirectoryPath) && Directory.Exists(DirectoryPath))
            { Directory.Delete(DirectoryPath, true); }
            if (firstFailure != null) { throw new IOException("部分音频文件租约归还失败。", firstFailure); }
        }
    }

    public sealed class ResourceAudioContentProvider : IAudioContentProvider
    {
        private readonly string m_sourceRoot; // 本地制作或安装包目录，也可使用资源包地址。
        private readonly string m_packageName; // 原始文件所属资源包。
        public AudioContentManifest Manifest { get; }

        /// <summary>配置基于框架资源入口的音频交付提供器。</summary>
        /// <param name="manifest">由制作元数据导出的依赖清单。</param>
        /// <param name="sourceRoot">未配置 Location 的文件来源根目录。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <exception cref="ArgumentNullException">清单为空。</exception>
        public ResourceAudioContentProvider(AudioContentManifest manifest, string sourceRoot,
            string packageName = ResourceSettings.DefaultPackageName)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            ValidateRelativePath(manifest.Version);
            m_sourceRoot = sourceRoot ?? string.Empty;
            m_packageName = packageName;
        }

        /// <summary>校验并交付组文件，保持 Wwise 使用的文件名和语言目录。</summary>
        /// <param name="groupKey">资源组名称。</param>
        /// <param name="settings">当前平台和语言。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>保持原始文件有效的本地资源租约。</returns>
        /// <exception cref="InvalidOperationException">平台或文件版本不匹配。</exception>
        /// <exception cref="KeyNotFoundException">组不存在。</exception>
        public async UniTask<AudioContentLease> PrepareGroupAsync(string groupKey, AudioSettings settings,
            CancellationToken cancellationToken)
        {
            if (Manifest.Platform != settings.Platform) { throw new InvalidOperationException("音频清单平台不匹配。"); }
            if (Manifest.Language != settings.Language) { throw new InvalidOperationException("音频清单语言不匹配。"); }
            AudioContentGroup group = groupKey == "Init" ? Manifest.Init : FindGroup(groupKey);
            var content = new AudioContentLease(group, string.Empty, Manifest.Version, Manifest.BackendVersion);
            try
            {
                // 内容租约接管资源句柄；成功、取消和失败均不创建完整音频副本。
                foreach (AudioContentFile file in group.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string relativePath = ValidateRelativePath(file.Path);
                    string location = string.IsNullOrEmpty(file.Location)
                        ? (string.IsNullOrEmpty(m_sourceRoot) ? relativePath
                            : m_sourceRoot.TrimEnd('/', '\\') + "/" + relativePath) : file.Location;
                    RawFileLease raw = null;
                    try
                    {
                        raw = await ResourceManager.Instance.LoadRawFileAsync(
                            location, m_packageName, cancellationToken);
                        if (settings.VerifyContentHashes || !raw.IsPackageFile)
                        {
                            await CopyVerifiedFileAsync(
                                raw.FilePath, null, file.Hash, file.Path, cancellationToken);
                        }

                        cancellationToken.ThrowIfCancellationRequested();
                        content.RetainFile(relativePath, raw);
                        raw = null;
                    }
                    finally { raw?.Dispose(); }
                }
                cancellationToken.ThrowIfCancellationRequested();
                return content;
            }
            catch
            {
                content.Dispose();
                throw;
            }
        }

        private static async UniTask<long> CopyVerifiedFileAsync(
            string sourcePath,
            string targetPath,
            string expectedHash,
            string relativePath,
            CancellationToken cancellationToken)
        {
            // 纯文件工作离开 Unity 主线程；不创建携带状态的闭包，也不提前释放源文件租约。
#if !UNITY_WEBGL || UNITY_EDITOR
            await UniTask.SwitchToThreadPool();
#endif
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (targetPath != null) { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetPath)); }
                long byteCount = 0;
                const int BUFFERSIZE = 262144;
                byte[] buffer = new byte[BUFFERSIZE];
                using (var hash = new AudioContentHash())
                using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, BUFFERSIZE, FileOptions.SequentialScan))
                using (FileStream destination = targetPath == null ? null : new FileStream(
                    targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    BUFFERSIZE, FileOptions.SequentialScan))
                {
                    // 音频直读路径只计算摘要，不建立交付副本。
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int bytesRead = source.Read(buffer, 0, buffer.Length);
                        if (bytesRead == 0) { break; }
                        hash.Append(buffer, bytesRead);
                        destination?.Write(buffer, 0, bytesRead);
                        byteCount += bytesRead;
#if UNITY_WEBGL && !UNITY_EDITOR
                        // 无线程平台分帧处理，仍使用固定大小缓冲区。
                        await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
#endif
                    }
                    // 完整处理最后一块摘要后比对导出清单。
                    destination?.Flush();
                    byte[] digest = hash.Complete();
                    cancellationToken.ThrowIfCancellationRequested();
                    string actualHash = BitConverter.ToString(digest).Replace("-", string.Empty);
                    if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("音频文件版本不匹配，请重新导出清单：" + relativePath);
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                return byteCount;
            }
            finally
            {
#if !UNITY_WEBGL || UNITY_EDITOR
                // 即使校验失败或取消，也先回主线程，再执行资源释放与原有异常处理。
                await UniTask.SwitchToMainThread();
#endif
            }
        }

        private AudioContentGroup FindGroup(string key)
        {
            foreach (AudioContentGroup group in Manifest.Groups) { if (group.Key == key) { return group; } }
            throw new KeyNotFoundException($"音频组不存在：{key}");
        }

        private static string ValidateRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path) || System.IO.Path.IsPathRooted(path) || path.Contains(":") ||
                Array.IndexOf(path.Replace('\\', '/').Split('/'), "..") >= 0)
            {
                throw new InvalidOperationException($"音频清单包含非法相对路径：{path}");
            }
            return path.Replace('\\', '/');
        }
    }
}
