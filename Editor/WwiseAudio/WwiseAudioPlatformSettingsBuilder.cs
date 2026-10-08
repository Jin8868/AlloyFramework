using System;
using UnityEditor;
using UnityEngine;

namespace AlloyFramework.Editor
{
    internal static class WwiseAudioPlatformSettingsBuilder
    {
        [MenuItem("★AlloyFramework★/音频/准备移动平台初始化设置")]
        private static void PrepareSettings()
        {
            if (Application.isPlaying)
            { throw new InvalidOperationException("请先退出运行模式再准备平台初始化设置。"); }
            AkWwiseInitializationSettings settings = AkWwiseInitializationSettings.Instance;
            if (!settings.IsValid)
            { throw new InvalidOperationException("Wwise 平台设置名称与引用数量不一致，请先修复官方初始化设置。"); }

            // 通过官方资产入口创建平台默认值，保留已有平台设置与用户选择的音频会话策略。
            EnsurePlatform<AkAndroidSettings>(settings, "Android");
            EnsurePlatform<AkiOSSettings>(settings, "iOS");
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log("[Audio/Wwise] Android/iOS 初始化设置已准备，随 DefaultPackage 收集。"
                + "请在官方 Wwise 项目设置中检查平台参数；本工具不生成或打包 SoundBank。");
        }

        private static void EnsurePlatform<T>(AkWwiseInitializationSettings settings, string platform)
            where T : AkWwiseInitializationSettings.PlatformSettings
        {
            for (int index = 0; index < settings.PlatformSettingsNameList.Count; index++)
            {
                if (!string.Equals(settings.PlatformSettingsNameList[index], platform,
                    StringComparison.OrdinalIgnoreCase)) { continue; }
                if (settings.PlatformSettingsList[index] is T) { return; }
                if (settings.PlatformSettingsList[index])
                { throw new InvalidOperationException($"{platform} 已绑定其他设置类型，请先检查官方平台配置。"); }
                settings.PlatformSettingsList[index] =
                    AkWwiseInitializationSettings.GetOrCreateAsset<T>(typeof(T).Name, platform);
                return;
            }

            T asset = AkWwiseInitializationSettings.GetOrCreateAsset<T>(typeof(T).Name, platform);
            if (!asset) { throw new InvalidOperationException($"无法创建 {platform} 的 Wwise 平台设置。"); }
            settings.PlatformSettingsNameList.Add(platform);
            settings.PlatformSettingsList.Add(asset);
        }
    }
}
