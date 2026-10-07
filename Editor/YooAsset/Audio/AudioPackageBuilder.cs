using System;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace AlloyFramework.Editor
{
    internal static class AudioPackageBuilder
    {
        [MenuItem("★AlloyFramework★/音频/打包音频资源包")]
        private static void BuildAudioPackage()
        {
            if (Application.isPlaying)
            { throw new InvalidOperationException("请先退出运行模式再打包音频资源。"); }

            // 原始文件独立构建，沿用当前目标平台并复制为内置包供 Offline 模式使用。
            var parameters = new RawFileBuildParameters
            {
                BuildOutputRoot = AssetBundleBuilderHelper.GetDefaultBuildOutputRoot(),
                BuildinFileRoot = AssetBundleBuilderHelper.GetStreamingAssetsRoot(),
                BuildPipeline = EBuildPipeline.RawFileBuildPipeline.ToString(),
                BuildBundleType = (int)EBuildBundleType.RawBundle,
                BuildTarget = EditorUserBuildSettings.activeBuildTarget,
                PackageName = "AudioPackage",
                PackageVersion = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"),
                BuildinFileCopyOption = EBuildinFileCopyOption.ClearAndCopyAll,
                BuildinFileCopyParams = string.Empty,
                FileNameStyle = EFileNameStyle.HashName,
                IncludePathInHash = true
            };
            BuildResult result = new RawFileBuildPipeline().Run(parameters, true);
            if (!result.Success) { throw new InvalidOperationException(result.ErrorInfo); }
            Debug.Log($"[AudioPackage] 构建完成：{result.OutputPackageDirectory}");
        }
    }
}
