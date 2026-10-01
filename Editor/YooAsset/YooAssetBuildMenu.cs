using System;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace AlloyFramework.Editor
{
    public static class YooAssetBuildMenu
    {
        private const string MenuRoot = "★AlloyFramework★/YooAsset/";
        private static readonly string PipelineName = EBuildPipeline.ScriptableBuildPipeline.ToString();

        [MenuItem(MenuRoot + "打开官方资源构建窗口", false, 100)]
        private static void OpenBuildWindow()
        {
            AssetBundleBuilderWindow.OpenWindow();
        }

        [MenuItem(MenuRoot + "构建 DefaultPackage", false, 101)]
        private static void BuildDefaultPackage()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("请先退出运行模式，再构建 YooAsset 资源。");
                return;
            }

            const string packageName = ResourceSettings.DefaultPackageName;
            string packageVersion = GetDefaultPackageVersion();
            BuildTarget buildTarget = EditorUserBuildSettings.activeBuildTarget;

            if (!EditorUtility.DisplayDialog(
                    "构建 YooAsset 资源",
                    $"资源包：{packageName}\n平台：{buildTarget}\n版本：{packageVersion}\n\n构建参数取自 YooAsset 官方构建窗口当前保存的设置。",
                    "构建",
                    "取消"))
            {
                return;
            }

            EditorApplication.delayCall += () => ExecuteBuild(packageName, packageVersion, buildTarget);
        }

        private static void ExecuteBuild(string packageName, string packageVersion, BuildTarget buildTarget)
        {
            try
            {
                AssetDatabase.SaveAssets();
                AssetBundleCollectorSettingData.Setting.CheckPackageConfigError(packageName);

                var buildParameters = new ScriptableBuildParameters
                {
                    BuildOutputRoot = AssetBundleBuilderHelper.GetDefaultBuildOutputRoot(),
                    BuildinFileRoot = AssetBundleBuilderHelper.GetStreamingAssetsRoot(),
                    BuildPipeline = PipelineName,
                    BuildBundleType = (int)EBuildBundleType.AssetBundle,
                    BuildTarget = buildTarget,
                    PackageName = packageName,
                    PackageVersion = packageVersion,
                    EnableSharePackRule = true,
                    VerifyBuildingResult = true,
                    FileNameStyle = AssetBundleBuilderSetting.GetPackageFileNameStyle(packageName, PipelineName),
                    BuildinFileCopyOption = AssetBundleBuilderSetting.GetPackageBuildinFileCopyOption(packageName, PipelineName),
                    BuildinFileCopyParams = AssetBundleBuilderSetting.GetPackageBuildinFileCopyParams(packageName, PipelineName),
                    CompressOption = AssetBundleBuilderSetting.GetPackageCompressOption(packageName, PipelineName),
                    ClearBuildCacheFiles = AssetBundleBuilderSetting.GetPackageClearBuildCache(packageName, PipelineName),
                    UseAssetDependencyDB = AssetBundleBuilderSetting.GetPackageUseAssetDependencyDB(packageName, PipelineName),
                    EncryptionServices = CreateService<IEncryptionServices>(
                        AssetBundleBuilderSetting.GetPackageEncyptionServicesClassName(packageName, PipelineName)),
                    ManifestProcessServices = CreateService<IManifestProcessServices>(
                        AssetBundleBuilderSetting.GetPackageManifestProcessServicesClassName(packageName, PipelineName)),
                    ManifestRestoreServices = CreateService<IManifestRestoreServices>(
                        AssetBundleBuilderSetting.GetPackageManifestRestoreServicesClassName(packageName, PipelineName)),
                    BuiltinShadersBundleName = GetBuiltinShadersBundleName(packageName)
                };

                var pipeline = new ScriptableBuildPipeline();
                BuildResult buildResult = pipeline.Run(buildParameters, true);
                if (!buildResult.Success)
                {
                    Debug.LogError($"YooAsset 资源构建失败：{buildResult.ErrorInfo}");
                    return;
                }

                AssetDatabase.Refresh();
                Debug.Log($"YooAsset 资源构建完成：{buildResult.OutputPackageDirectory}");
                EditorUtility.RevealInFinder(buildResult.OutputPackageDirectory);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static T CreateService<T>(string className) where T : class
        {
            var classTypes = EditorTools.GetAssignableTypes(typeof(T));
            Type classType = classTypes.Find(type => type.FullName == className);
            return classType == null ? null : Activator.CreateInstance(classType) as T;
        }

        private static string GetBuiltinShadersBundleName(string packageName)
        {
            bool uniqueBundleName = AssetBundleCollectorSettingData.Setting.UniqueBundleName;
            PackRuleResult packRuleResult = DefaultPackRule.CreateShadersPackRuleResult();
            return packRuleResult.GetBundleName(packageName, uniqueBundleName);
        }

        private static string GetDefaultPackageVersion()
        {
            int totalMinutes = DateTime.Now.Hour * 60 + DateTime.Now.Minute;
            return DateTime.Now.ToString("yyyy-MM-dd") + "-" + totalMinutes;
        }
    }
}
