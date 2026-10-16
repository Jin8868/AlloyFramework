using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace AlloyFramework.Audio.Wwise
{
    /// <summary>将资源文件直接交给 Wwise，必要时仅建立同一数据的文件名硬链接。</summary>
    internal sealed class WwiseReadFile : IDisposable
    {
        private readonly string m_linkPath; // 仅删除框架创建的硬链接名称，不删除资源文件。
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

            // 哈希缓存文件名不能直接被 Wwise 按媒体 ID 找到；硬链接只增加名字，不复制数据。
            string linkPath = Path.Combine(readRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(linkPath));
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            bool linked = CreateHardLink(linkPath, sourcePath, IntPtr.Zero);
#elif UNITY_IOS || UNITY_ANDROID || UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
            bool linked = Link(sourcePath, linkPath) == 0;
#else
            bool linked = false;
#endif
            if (!linked)
            {
                throw new IOException(
                    "无法建立 Wwise 文件名硬链接；直读模式不会回退为完整复制。请使用同卷可写缓存或自定义 Wwise I/O：" +
                    sourcePath, new Win32Exception(Marshal.GetLastWin32Error()));
            }
            return new WwiseReadFile(sourcePath, hash, readRoot, linkPath);
        }

        /// <summary>原生卸载后移除文件名硬链接，原始资源仍由内容租约管理。</summary>
        public void Dispose()
        {
            if (m_disposed) { return; }
            m_disposed = true;
            if (!string.IsNullOrEmpty(m_linkPath)) { File.Delete(m_linkPath); }
        }

#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
        [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode,
            ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr security);
#elif UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal", EntryPoint = "link", SetLastError = true)]
        private static extern int Link(string existingPath, string newPath);
#elif UNITY_ANDROID || UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
        [DllImport("libc", EntryPoint = "link", SetLastError = true)]
        private static extern int Link(string existingPath, string newPath);
#endif
    }
}
