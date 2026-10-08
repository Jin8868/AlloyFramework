using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlloyFramework.Audio.Wwise
{
    public static class WwiseAudioInstaller
    {
        /// <summary>通过框架资源系统安装 Wwise 后端及当前平台的音频内容。</summary>
        /// <param name="audioPackageName">原始音频文件所在的资源包。</param>
        /// <param name="contentRoot">平台目录之前的资源地址根目录。</param>
        /// <param name="initializationSettingsLocation">SDK 初始化设置资源地址。</param>
        /// <param name="initializationSettingsPackageName">初始化设置所在的 Unity 资产包。</param>
        /// <param name="cancellationToken">取消安装的令牌。</param>
        /// <returns>安装任务，后续由 AudioManager 初始化音频引擎。</returns>
        /// <exception cref="ArgumentException">资源包名称或资源地址为空。</exception>
        /// <exception cref="PlatformNotSupportedException">当前平台尚未接入。</exception>
        /// <exception cref="InvalidOperationException">音频清单缺失或平台不匹配。</exception>
        public static async UniTask InstallAsync(
            string audioPackageName,
            string contentRoot,
            string initializationSettingsLocation,
            string initializationSettingsPackageName,
            CancellationToken cancellationToken = default)
        {
            // 提前验证项目配置，避免不完整配置触发资源加载。
            ValidateAddress(audioPackageName, nameof(audioPackageName));
            ValidateAddress(contentRoot, nameof(contentRoot));
            ValidateAddress(initializationSettingsLocation, nameof(initializationSettingsLocation));
            ValidateAddress(initializationSettingsPackageName, nameof(initializationSettingsPackageName));

            // Editor 与 Player 均通过框架资源包交付，不直接读取制作目录。
            string platform;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            platform = "Windows";
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            platform = "Mac";
#elif UNITY_ANDROID
            platform = "Android";
#elif UNITY_IOS
            platform = "iOS";
#else
            throw new PlatformNotSupportedException("当前平台尚未配置 Wwise 音频内容。");
#endif
            await ResourceManager.Instance.EnsurePackageAsync(audioPackageName, cancellationToken);
            await ResourceManager.Instance.EnsurePackageAsync(initializationSettingsPackageName, cancellationToken);
            string sourceRoot = contentRoot.TrimEnd('/') + "/" + platform;
            string manifestLocation = sourceRoot + "/AlloyAudioManifest";
            using (RawFileLease raw = await ResourceManager.Instance.LoadRawFileAsync(
                manifestLocation, audioPackageName, cancellationToken))
            {
                AudioContentManifest manifest = JsonUtility.FromJson<AudioContentManifest>(
                    File.ReadAllText(raw.FilePath));
                if (manifest == null || manifest.Platform != platform ||
                    manifest.Init == null || manifest.Groups == null)
                {
                    throw new InvalidOperationException(
                        $"音频清单缺失或平台错误：{manifestLocation}，请重新导出音频内容。");
                }

                // 安装器与框架音频寻址规则均剥离文件扩展名。
                SetPackageLocations(manifest.Init, sourceRoot);
                foreach (AudioContentGroup group in manifest.Groups) { SetPackageLocations(group, sourceRoot); }
                IAssetHandle<ScriptableObject> settingsLease = null;
                try
                {
                    // 初始化设置是 Unity 资产，使用现有 Config Collector；Bank 与媒体才是 RawFile。
                    settingsLease = await ResourceManager.Instance.LoadAssetAsync<ScriptableObject>(
                        initializationSettingsLocation, initializationSettingsPackageName,
                        cancellationToken: cancellationToken);
                    AudioManager.Instance.Install(new WwiseAudioBackend(settingsLease),
                        new ResourceAudioContentProvider(manifest, sourceRoot, audioPackageName),
                        new AlloyFramework.Audio.AudioSettings
                        { Platform = platform, Language = manifest.Language });
                }
                catch { settingsLease?.Dispose(); throw; }
            }
        }

        private static void ValidateAddress(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            { throw new ArgumentException("资源包名称或资源地址不能为空。", parameterName); }
        }

        private static void SetPackageLocations(AudioContentGroup group, string sourceRoot)
        {
            foreach (AudioContentFile file in group.Files)
            { file.Location = sourceRoot + "/" + Path.ChangeExtension(file.Path, null).Replace('\\', '/'); }
        }
    }
}
