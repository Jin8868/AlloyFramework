using System;
using System.Collections.Generic;
using System.IO;
using AlloyFramework.Audio;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace AlloyFramework.Editor
{
    [DisplayName("定位地址: Wwise音频相对路径")]
    public sealed class AddressWwiseAudio : IAddressRule
    {
        private const string RESROOT = "Assets/Res/"; // 默认音频内容导出根目录。

        /// <summary>生成与框架安装器一致的不含扩展名的音频资源地址。</summary>
        /// <param name="data">音频资源信息。</param>
        /// <returns>相对于默认资源根目录的地址。</returns>
        /// <exception cref="InvalidOperationException">资源不在默认导出根目录内。</exception>
        public string GetAssetAddress(AddressRuleData data)
        {
            // 独立于项目的地址规则，确保框架工具不依赖业务程序集。
            string assetPath = data.AssetPath.Replace('\\', '/');
            if (!assetPath.StartsWith(RESROOT, StringComparison.Ordinal))
            { throw new InvalidOperationException($"音频资源不在默认导出根目录内：{assetPath}"); }
            return Path.ChangeExtension(assetPath.Substring(RESROOT.Length), null);
        }
    }

    [DisplayName("收集音频: 当前平台清单文件")]
    public sealed class CollectWwiseAudio : IFilterRule
    {
        private string m_manifestPath; // 当前缓存清单路径。
        private DateTime m_writeTime; // 清单修改后刷新白名单。
        private readonly HashSet<string> m_paths = new HashSet<string>(StringComparer.Ordinal); // 清单引用文件。
        public string FindAssetType => EAssetSearchType.All.ToString();

        /// <summary>仅收集运行平台清单及清单引用的原始文件，排除已删除事件的旧输出。</summary>
        /// <param name="data">Collector 中的资源信息。</param>
        /// <returns>文件是否属于本次运行或构建的平台。</returns>
        /// <exception cref="InvalidOperationException">清单缺失或不完整。</exception>
        public bool IsCollectAsset(FilterRuleData data)
        {
            string root = "Assets/Res/WwiseAudio/" + GetPlatform() + "/";
            string manifestPath = root + "AlloyAudioManifest.json";
            if (!File.Exists(manifestPath))
            { throw new InvalidOperationException($"缺少音频清单，请先生成和导出：{manifestPath}"); }
            DateTime writeTime = File.GetLastWriteTimeUtc(manifestPath);
            if (m_manifestPath != manifestPath || m_writeTime != writeTime)
            {
                m_paths.Clear();
                AudioContentManifest manifest = JsonUtility.FromJson<AudioContentManifest>(
                    File.ReadAllText(manifestPath));
                if (manifest?.Init == null || manifest.Groups == null)
                { throw new InvalidOperationException("音频清单不完整。"); }
                m_paths.Add(manifestPath);
                AddFiles(manifest.Init, root);
                foreach (AudioContentGroup group in manifest.Groups) { AddFiles(group, root); }
                m_manifestPath = manifestPath;
                m_writeTime = writeTime;
            }
            return m_paths.Contains(data.AssetPath.Replace('\\', '/'));
        }

        private void AddFiles(AudioContentGroup group, string root)
        {
            foreach (AudioContentFile file in group.Files) { m_paths.Add(root + file.Path.Replace('\\', '/')); }
        }

        private static string GetPlatform()
        {
            // 模拟运行使用编辑器主机原生插件，实际打包使用构建目标平台。
            if (Application.isPlaying)
            {
#if UNITY_EDITOR_WIN
                return "Windows";
#elif UNITY_EDITOR_OSX
                return "Mac";
#else
                throw new PlatformNotSupportedException("编辑器主机尚未配置 Wwise 平台。");
#endif
            }
            switch (EditorUserBuildSettings.activeBuildTarget)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64: return "Windows";
                case BuildTarget.StandaloneOSX: return "Mac";
                case BuildTarget.Android: return "Android";
                case BuildTarget.iOS: return "iOS";
                default: throw new PlatformNotSupportedException("构建目标尚未配置 Wwise 内容。");
            }
        }
    }

    [DisplayName("定位地址: Wwise初始化设置")]
    public sealed class AddressWwiseInitializationSettings : IAddressRule
    {
        /// <summary>让 SDK 设置资产由现有 Unity 资源包收集，无需复制为第二份设置。</summary>
        /// <param name="data">设置资产信息。</param>
        /// <returns>框架安装器约定的资源地址。</returns>
        public string GetAssetAddress(AddressRuleData data) { return "Config/WwiseInitializationSettings"; }
    }
}
