using UnityEngine.Scripting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using Cysharp.Threading.Tasks;
using HybridCLR;
using UnityEngine;

namespace AlloyFramework.HybridCLR
{
    /// <summary>
    /// 在业务配置初始化前校验代码、补充元数据并加载业务程序集。
    /// </summary>
    public sealed class HybridCLRCodePreparation : IGameCodePreparation
    {
        private const string CODEPACKAGE = "CodePackage";
        private const string MANIFESTLOCATION = "HotUpdateCode/manifest";
        private readonly GameCodePreparationResult m_result; // 与入口加载器共享的准备结果。
        private readonly string m_runtimeAssemblyName; // 项目配置的业务程序集。
        private readonly string m_installerTypeName; // 项目配置的显式安装类型。
        private bool m_started; // 防止在部分加载后再次执行。

        /// <summary>
        /// 创建业务代码准备服务。
        /// </summary>
        /// <param name="result">与入口加载器共享的结果。</param>
        /// <param name="runtimeAssemblyName">业务程序集名称。</param>
        /// <param name="installerTypeName">显式安装入口类型名称。</param>
        /// <exception cref="ArgumentNullException">参数为空。</exception>
        [Preserve]
        public HybridCLRCodePreparation(
            GameCodePreparationResult result,
            string runtimeAssemblyName,
            string installerTypeName)
        {
            m_result = result ?? throw new ArgumentNullException(nameof(result));
            m_runtimeAssemblyName = runtimeAssemblyName ?? throw new ArgumentNullException(nameof(runtimeAssemblyName));
            m_installerTypeName = installerTypeName ?? throw new ArgumentNullException(nameof(installerTypeName));
        }

        /// <summary>
        /// 校验并加载业务代码，在配置系统启动前调用业务安装入口。
        /// </summary>
        /// <param name="context">框架启动上下文。</param>
        /// <param name="cancellationToken">取消启动的令牌。</param>
        /// <returns>代码及配置安装完成的异步操作。</returns>
        /// <exception cref="InvalidOperationException">基线不匹配或代码安装失败。</exception>
        public UniTask PrepareAsync(FrameworkContext context, CancellationToken cancellationToken)
        {
#if UNITY_EDITOR
            // 编辑器仅复用已加载程序集，无需异步状态机；异常仍通过任务传给调用方。
            try
            {
                PrepareEditor(cancellationToken);
                return UniTask.CompletedTask;
            }
            catch (Exception exception)
            {
                return UniTask.FromException(exception);
            }
#else
            // Player 保留资源读取、文件校验和程序集加载的真实异步流程。
            return PreparePlayerAsync(cancellationToken);
#endif
        }

#if UNITY_EDITOR
        private void PrepareEditor(CancellationToken cancellationToken)
#else
        private async UniTask PreparePlayerAsync(CancellationToken cancellationToken)
#endif
        {
            if (m_started)
            {
                throw new InvalidOperationException("代码准备已执行；部分加载后的失败必须重启 Player。");
            }
            m_started = true;
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
#if UNITY_EDITOR
                // 编辑器复用 Unity 已加载的代码，不能用它证明下载的新 DLL 已执行。
                m_result.RuntimeAssembly = FindLoadedAssembly(m_runtimeAssemblyName);
                Debug.Log("[HybridCLR] 编辑器复用已加载业务程序集；真实热更新请使用 IL2CPP Player 验证。");
#else
                // 在任何不可逆的程序集加载前完成全部文件的哈希和身份校验。
                await ResourceManager.Instance.EnsurePackageAsync(CODEPACKAGE, cancellationToken);
                StartupLoading.ReportOperation(EStartupLoadingStage.PreparingGameCode, "校验游戏代码清单" );
                HotUpdateCodeManifest manifest;
                using (RawFileLease lease = await ResourceManager.Instance.LoadRawFileAsync(
                    MANIFESTLOCATION, CODEPACKAGE, cancellationToken))
                {
                    manifest = JsonUtility.FromJson<HotUpdateCodeManifest>(File.ReadAllText(lease.FilePath));
                }
                ValidateManifest(manifest);
                await ValidateFilesAsync(manifest.Metadata, cancellationToken);
                await ValidateFilesAsync(manifest.Assemblies, cancellationToken);
                m_result.ReleaseID = manifest.ReleaseID;
                m_result.BaselineID = manifest.BaselineID;

                // 元数据与 DLL 加载都不能回滚，失败后必须重启进程。
                StartupLoading.ReportOperation(EStartupLoadingStage.PreparingGameCode, "补充 AOT 元数据" );
                m_result.RequiresRestart = true;
                foreach (HotUpdateCodeFile metadata in manifest.Metadata)
                {
                    byte[] bytes = await ReadVerifiedBytesAsync(metadata, cancellationToken);
                    LoadImageErrorCode error = RuntimeApi.LoadMetadataForAOTAssembly(
                        bytes, HomologousImageMode.SuperSet);
                    if (error != LoadImageErrorCode.OK)
                    {
                        throw new InvalidOperationException(
                            $"补充元数据失败：{metadata.AssemblyName}，基线={manifest.BaselineID}，错误={error}");
                    }
                }
                StartupLoading.ReportOperation(EStartupLoadingStage.PreparingGameCode, "加载业务程序集" );
                foreach (HotUpdateCodeFile assemblyFile in manifest.Assemblies)
                {
                    if (TryFindLoadedAssembly(assemblyFile.AssemblyName) != null)
                    {
                        throw new InvalidOperationException($"业务程序集已加载，拒绝重复加载：{assemblyFile.AssemblyName}");
                    }
                    byte[] bytes = await ReadVerifiedBytesAsync(assemblyFile, cancellationToken);
                    Assembly assembly = Assembly.Load(bytes);
                    if (assembly.GetName().Name != assemblyFile.AssemblyName)
                    {
                        throw new InvalidOperationException("实际程序集名称与代码清单不一致。");
                    }
                    if (assemblyFile.AssemblyName == m_runtimeAssemblyName)
                    {
                        m_result.RuntimeAssembly = assembly;
                    }
                }
#endif
                cancellationToken.ThrowIfCancellationRequested();
                StartupLoading.ReportOperation(EStartupLoadingStage.PreparingGameCode, "安装业务代码" );
                InstallRuntime(m_result.RuntimeAssembly);
                m_result.IsCompleted = true;
                Debug.Log($"[HybridCLR] 业务代码准备完成：{m_runtimeAssemblyName}，版本={m_result.ReleaseID}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested &&
                !m_result.RequiresRestart)
            {
                // 未进入不可逆阶段时保留取消语义，由框架执行资源关闭。
                m_result.IsCompleted = false;
                throw;
            }
            catch (Exception exception)
            {
                m_result.IsCompleted = false;
                throw new InvalidOperationException(
                    m_result.RequiresRestart ? "业务代码准备失败，程序集无法卸载，请重启 Player。" : "业务代码准备失败。",
                    exception);
            }
        }

        private void ValidateManifest(HotUpdateCodeManifest manifest)
        {
            TextAsset identityAsset = Resources.Load<TextAsset>("AlloyHotUpdateIdentity");
            if (identityAsset == null)
            {
                throw new InvalidOperationException("主包身份缺失，请先执行热更新主包资源准备。");
            }
            HotUpdatePlayerIdentity identity = JsonUtility.FromJson<HotUpdatePlayerIdentity>(identityAsset.text);
            if (manifest == null || identity == null || string.IsNullOrEmpty(identity.BaselineID) ||
                manifest.BaselineID != identity.BaselineID || manifest.Platform != identity.Platform ||
                manifest.Platform != GetPlatform() || manifest.RuntimeAssemblyName != m_runtimeAssemblyName ||
                manifest.InstallerTypeName != m_installerTypeName || string.IsNullOrEmpty(manifest.ReleaseID) ||
                manifest.Metadata == null || manifest.Assemblies == null || manifest.Assemblies.Length == 0)
            {
                throw new InvalidOperationException("代码发布清单与主包平台、基线或业务入口不匹配。");
            }

            // 拒绝重名和不可信地址，保证清单只读取代码包中的程序集文件。
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (HotUpdateCodeFile file in manifest.Metadata)
            {
                ValidateFile(file, names);
            }
            foreach (HotUpdateCodeFile file in manifest.Assemblies)
            {
                ValidateFile(file, names);
            }
            if (!names.Contains(m_runtimeAssemblyName))
            {
                throw new InvalidOperationException("代码清单缺少业务程序集。");
            }
        }

        private static void ValidateFile(HotUpdateCodeFile file, HashSet<string> names)
        {
            if (file == null || string.IsNullOrEmpty(file.AssemblyName) || !names.Add(file.AssemblyName) ||
                string.IsNullOrEmpty(file.Location) || !file.Location.StartsWith("HotUpdateCode/", StringComparison.Ordinal) ||
                file.Location.Contains("..") || file.Location.Contains(":") || file.Location.Contains("\\") ||
                file.SHA256 == null || file.SHA256.Length != 64)
            {
                throw new InvalidOperationException("代码清单包含重复名称或无效文件描述。");
            }
        }

        private static async UniTask ValidateFilesAsync(
            HotUpdateCodeFile[] files,
            CancellationToken cancellationToken)
        {
            foreach (HotUpdateCodeFile file in files)
            {
                await ReadVerifiedBytesAsync(file, cancellationToken);
            }
        }

        private static async UniTask<byte[]> ReadVerifiedBytesAsync(
            HotUpdateCodeFile file,
            CancellationToken cancellationToken)
        {
            using (RawFileLease lease = await ResourceManager.Instance.LoadRawFileAsync(
                file.Location, CODEPACKAGE, cancellationToken))
            {
                byte[] bytes = File.ReadAllBytes(lease.FilePath);
                using (SHA256 algorithm = SHA256.Create())
                {
                    string hash = BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", string.Empty);
                    if (!string.Equals(hash, file.SHA256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"代码文件校验失败：{file.AssemblyName}");
                    }
                }
                return bytes;
            }
        }

        private void InstallRuntime(Assembly assembly)
        {
            if (assembly == null)
            {
                throw new InvalidOperationException("业务程序集未加载。");
            }
            Type installerType = assembly.GetType(m_installerTypeName, true);
            MethodInfo method = installerType.GetMethod(
                "Install", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (method == null || method.ReturnType != typeof(void) || method.ContainsGenericParameters)
            {
                throw new InvalidOperationException("业务安装入口必须是 public static void Install()。");
            }
            method.Invoke(null, null);
        }

        private static Assembly FindLoadedAssembly(string assemblyName)
        {
            return TryFindLoadedAssembly(assemblyName) ??
                throw new InvalidOperationException($"找不到编辑器业务程序集：{assemblyName}");
        }

        private static Assembly TryFindLoadedAssembly(string assemblyName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == assemblyName)
                {
                    return assembly;
                }
            }
            return null;
        }

        private static string GetPlatform()
        {
#if UNITY_STANDALONE_WIN
            return "StandaloneWindows64";
#elif UNITY_ANDROID
            return "Android";
#else
            throw new NotSupportedException("首期代码加载仅支持 Windows x64 和 Android。");
#endif
        }
    }
}
