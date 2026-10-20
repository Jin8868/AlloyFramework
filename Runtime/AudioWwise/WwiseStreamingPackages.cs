using System;
using System.Collections.Generic;
using System.IO;
using AlloyFramework.Audio;

namespace AlloyFramework.Audio.Wwise
{
    public sealed partial class WwiseAudioBackend
    {
        private readonly Dictionary<string, NativeStreamingPackage> m_streamingPackages =
            new Dictionary<string, NativeStreamingPackage>(); // 媒体 ID 文件路径到共享 Wwise 文件包的映射。

        private void RetainStreamingPackage(AudioContentLease content, AudioContentFile file, NativeGroup group)
        {
            string source = content.GetFilePath(file.Path);
            if (m_fileUsers.ContainsKey(file.Path))
                throw new InvalidOperationException("同一媒体仍由普通文件组使用，不能切换文件包：" + file.Path);
            if (m_streamingPackages.TryGetValue(file.Path, out NativeStreamingPackage package))
            {
                if (package.Source != source || package.Hash != file.FilePackageHash)
                    throw new InvalidOperationException("同一媒体仍由其他版本文件包使用：" + file.Path);
            }
            else
            {
                string nativePath = source;
                if (source.StartsWith("jar:", StringComparison.OrdinalIgnoreCase))
                {
#if UNITY_ANDROID && !UNITY_EDITOR
                    const string ASSETSEPARATOR = "!/assets/";
                    int separator = source.IndexOf(ASSETSEPARATOR, StringComparison.Ordinal);
                    if (separator < 0) throw new InvalidOperationException("文件包 APK 地址无效。");
                    nativePath = Uri.UnescapeDataString(source.Substring(separator + ASSETSEPARATOR.Length));
#else
                    throw new PlatformNotSupportedException("当前平台不支持 APK 音频文件包。");
#endif
                }
                else nativePath = Path.GetFullPath(source);
                // SDK 只读取包头索引，媒体数据随后由自己的流管理器按需读取。
                Check(AkUnitySoundEngine.LoadFilePackage(nativePath, out uint packageID),
                    "加载视频音轨文件包 " + file.Path);
                package = new NativeStreamingPackage
                {
                    Source = source,
                    Hash = file.FilePackageHash,
                    ID = packageID
                };
                m_streamingPackages.Add(file.Path, package);
            }
            package.Users++;
            group.StreamingPackages.Add(file.Path);
        }

        private void ReleaseStreamingPackages(NativeGroup group)
        {
            // 事件媒体和 Bank 已卸载后才能释放文件包，避免流线程读到已关闭的文件描述符。
            for (int index = group.StreamingPackages.Count - 1; index >= 0; index--)
            {
                string path = group.StreamingPackages[index];
                NativeStreamingPackage package = m_streamingPackages[path];
                if (package.Users == 1)
                {
                    Check(AkUnitySoundEngine.UnloadFilePackage(package.ID), "卸载视频音轨文件包 " + path);
                    m_streamingPackages.Remove(path);
                }
                else package.Users--;
                group.StreamingPackages.RemoveAt(index);
            }
        }

        private sealed class NativeStreamingPackage
        {
            internal string Source; // 当前版本的 APK 地址或缓存物理路径。
            internal string Hash; // 防止并发组把同一媒体 ID 映射到不同版本。
            internal uint ID; // Wwise 返回的文件包标识。
            internal int Users; // 持有本包的已加载音频组数量。
        }
    }
}
