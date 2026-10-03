using System;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>
    /// HybridCLR 业务入口加载流程骨架。安装 HybridCLR 后补充各步骤的具体实现。
    /// </summary>
    public sealed class HybridCLRGameEntryLoader : IGameEntryLoader
    {
        public async UniTask<IGameEntry> LoadAsync(
            FrameworkContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 第一步：通过资源系统确保本地基础 DLL 或远程最新 DLL 已经就绪。
            await PrepareCodeResourcesAsync(context, cancellationToken);

            // 第二步：读取裁剪后的 AOT DLL，并调用 HybridCLR 补充泛型元数据。
            await LoadAotMetadataAsync(context, cancellationToken);

            // 第三步：读取业务运行时 DLL 字节并动态加载程序集。
            var runtimeAssembly = await LoadRuntimeAssemblyAsync(context, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            return GameEntryActivator.Create(runtimeAssembly);
        }

        private static UniTask PrepareCodeResourcesAsync(
            FrameworkContext context,
            CancellationToken cancellationToken)
        {
            throw NotImplemented(nameof(PrepareCodeResourcesAsync));
        }

        private static UniTask LoadAotMetadataAsync(
            FrameworkContext context,
            CancellationToken cancellationToken)
        {
            throw NotImplemented(nameof(LoadAotMetadataAsync));
        }

        private static UniTask<Assembly> LoadRuntimeAssemblyAsync(
            FrameworkContext context,
            CancellationToken cancellationToken)
        {
            throw NotImplemented(nameof(LoadRuntimeAssemblyAsync));
        }

        private static NotSupportedException NotImplemented(string step)
        {
            return new NotSupportedException(
                $"HybridCLR 尚未安装，入口加载步骤 {step} 尚未实现。");
        }
    }
}