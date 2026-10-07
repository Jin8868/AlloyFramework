using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

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
        private bool m_disposed; // 保证本租约仅删除一次独占交付目录。
        public AudioContentGroup Group { get; }
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

        /// <summary>原生卸载完成后释放本组独占文件。</summary>
        public void Dispose()
        {
            if (m_disposed) { return; }
            m_disposed = true;
            if (Directory.Exists(DirectoryPath)) { Directory.Delete(DirectoryPath, true); }
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
        /// <returns>版本隔离的本地资源租约。</returns>
        /// <exception cref="InvalidOperationException">平台或文件版本不匹配。</exception>
        /// <exception cref="KeyNotFoundException">组不存在。</exception>
        public async UniTask<AudioContentLease> PrepareGroupAsync(string groupKey, AudioSettings settings,
            CancellationToken cancellationToken)
        {
            if (Manifest.Platform != settings.Platform) { throw new InvalidOperationException("音频清单平台不匹配。"); }
            if (Manifest.Language != settings.Language) { throw new InvalidOperationException("音频清单语言不匹配。"); }
            AudioContentGroup group = groupKey == "Init" ? Manifest.Init : FindGroup(groupKey);
            string directory = System.IO.Path.Combine(Application.persistentDataPath,
                "AlloyAudio", Manifest.Version, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                // 独占目录保护正在播放的旧版本，交付失败时完整清理，禁止原地覆盖。
                foreach (AudioContentFile file in group.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string relativePath = ValidateRelativePath(file.Path);
                    string location = string.IsNullOrEmpty(file.Location)
                        ? (string.IsNullOrEmpty(m_sourceRoot) ? relativePath
                            : m_sourceRoot.TrimEnd('/', '\\') + "/" + relativePath) : file.Location;
                    using (RawFileLease raw = await ResourceManager.Instance.LoadRawFileAsync(
                        location, m_packageName, cancellationToken))
                    {
                        using (System.Security.Cryptography.SHA256 hash = System.Security.Cryptography.SHA256.Create())
                        using (FileStream source = File.OpenRead(raw.FilePath))
                        {
                            string actualHash = BitConverter.ToString(hash.ComputeHash(source)).Replace("-", "");
                            if (!string.Equals(actualHash, file.Hash, StringComparison.OrdinalIgnoreCase))
                            {
                                throw new InvalidOperationException($"音频文件版本不匹配，请重新导出清单：{file.Path}");
                            }
                        }
                        string target = System.IO.Path.Combine(directory, relativePath);
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                        File.Copy(raw.FilePath, target, false);
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                return new AudioContentLease(group, directory, Manifest.Version, Manifest.BackendVersion);
            }
            catch
            {
                Directory.Delete(directory, true);
                throw;
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
