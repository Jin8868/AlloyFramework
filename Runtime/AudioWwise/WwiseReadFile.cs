using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace AlloyFramework.Audio.Wwise
{
    /// <summary>将资源文件交给 Wwise，必要时建立原文件名链接，不复制音频数据。</summary>
    internal sealed class WwiseReadFile : IDisposable
    {
        private readonly string m_linkPath; // 仅删除框架创建的链接名称，不删除资源文件。
        private string m_ownedLinkRoot; // 跨卷来源旁创建的独占链接目录，释放时一并清理。
        private bool m_disposed; // 避免重复删除链接。

        internal string SourcePath { get; }
        internal string Hash { get; }
        internal string SearchRoot { get; }
        internal int Users { get; set; }

        private WwiseReadFile(string sourcePath, string hash, string searchRoot, string linkPath)
        {
            SourcePath = sourcePath;
            Hash = hash;
            SearchRoot = searchRoot;
            m_linkPath = linkPath;
        }

        internal static WwiseReadFile Create(string sourcePath, string relativePath, string hash, string readRoot)
        {
            sourcePath = Path.GetFullPath(sourcePath);
            string normalizedSource = sourcePath.Replace('\\', '/');
            string suffix = "/" + relativePath.Replace('\\', '/');
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            const StringComparison COMPARISON = StringComparison.OrdinalIgnoreCase;
#else
            const StringComparison COMPARISON = StringComparison.Ordinal;
#endif
            // 模拟模式和保留文件目录的来源可直接注册原目录，无需创建任何文件。
            if (normalizedSource.EndsWith(suffix, COMPARISON))
            {
                string root = normalizedSource.Substring(0, normalizedSource.Length - suffix.Length) + "/";
                return new WwiseReadFile(sourcePath, hash, root, null);
            }

            // 哈希缓存文件名不能直接被 Wwise 按媒体 ID 找到；链接只增加名字，不复制数据。
            string ownedLinkRoot = null;
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            if (!string.Equals(Path.GetPathRoot(sourcePath), Path.GetPathRoot(Path.GetFullPath(readRoot)),
                StringComparison.OrdinalIgnoreCase))
            {
                // 内置资源可能在 D 盘而 persistentDataPath 在 C 盘；硬链接必须落在来源所在卷。
                // 会话和来源共同固定目录，卸载后重开仍能复用原生引擎登记过的搜索路径。
                ownedLinkRoot = Path.Combine(Path.GetDirectoryName(sourcePath),
                    ".AlloyWwise-" + Path.GetFileName(readRoot.TrimEnd('/', '\\')) +
                    "-" + Path.GetFileNameWithoutExtension(sourcePath));
                readRoot = ownedLinkRoot;
            }
#endif
            string linkPath = Path.Combine(readRoot, relativePath);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(linkPath));
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
                bool linked = CreateHardLink(linkPath, sourcePath, IntPtr.Zero);
#elif UNITY_ANDROID && !UNITY_EDITOR
                // Android 外部存储不支持硬链接，别名放在应用内部目录并指向原缓存文件。
                bool linked = CreateSymbolicLink(sourcePath, linkPath) == 0;
#elif UNITY_IOS || UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
                bool linked = Link(sourcePath, linkPath) == 0;
#else
                bool linked = false;
#endif
                if (!linked)
                {
                    int errorCode = Marshal.GetLastWin32Error();
#if UNITY_ANDROID && !UNITY_EDITOR
                    throw new IOException(
                        "无法建立 Wwise 文件名符号链接，请确认读取目录位于应用内部存储且来源文件可读：" +
                        sourcePath + " -> " + linkPath + $"，errno={errorCode}", new Win32Exception(errorCode));
#else
                    throw new IOException(
                        "无法建立 Wwise 文件名硬链接，请确保音频来源目录同卷可写，或使用自定义 Wwise I/O：" +
                        sourcePath + " -> " + linkPath, new Win32Exception(errorCode));
#endif
                }
                return new WwiseReadFile(sourcePath, hash, readRoot, linkPath)
                {
                    m_ownedLinkRoot = ownedLinkRoot
                };
            }
            catch
            {
                // 独占目录只保存本次创建的文件名链接，不含资源文件本体。
                if (ownedLinkRoot != null && Directory.Exists(ownedLinkRoot))
                {
                    Directory.Delete(ownedLinkRoot, true);
                }
                throw;
            }
        }

        /// <summary>原生卸载后移除文件名链接，原始资源仍由内容租约管理。</summary>
        public void Dispose()
        {
            if (m_disposed) { return; }
            m_disposed = true;
            // 先删除本文件的别名；跨卷适配目录由本实例独占，不影响其他租约或来源文件。
            if (!string.IsNullOrEmpty(m_linkPath)) { File.Delete(m_linkPath); }
            if (m_ownedLinkRoot != null && Directory.Exists(m_ownedLinkRoot))
            {
                Directory.Delete(m_ownedLinkRoot, true);
            }
        }

#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
        [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode,
            ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr security);
#elif UNITY_ANDROID && !UNITY_EDITOR
        [DllImport("libc", EntryPoint = "symlink", SetLastError = true)]
        private static extern int CreateSymbolicLink(string existingPath, string newPath);
#elif UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal", EntryPoint = "link", SetLastError = true)]
        private static extern int Link(string existingPath, string newPath);
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
        [DllImport("libc", EntryPoint = "link", SetLastError = true)]
        private static extern int Link(string existingPath, string newPath);
#endif
    }
}
