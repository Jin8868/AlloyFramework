using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework.Audio
{
    public sealed partial class ResourceAudioContentProvider
    {
        private async UniTask PrepareFilePackageAsync(
            AudioContentFile file, AudioContentLease content, AudioSettings settings,
            CancellationToken cancellationToken)
        {
            string relativePath = ValidateRelativePath(file.Path);
            string packagePath = ValidateRelativePath(file.FilePackagePath);
            if (!relativePath.EndsWith(".wem", StringComparison.OrdinalIgnoreCase) ||
                !packagePath.EndsWith(".pck", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(file.FilePackageHash) || file.FilePackageSize <= 0)
                throw new InvalidOperationException("流媒体文件包描述无效：" + relativePath);
            string location = string.IsNullOrEmpty(file.Location)
                ? m_sourceRoot.TrimEnd('/', '\\') + "/" + Path.ChangeExtension(packagePath, null)
                : file.Location;
#if UNITY_ANDROID && !UNITY_EDITOR
            // APK 已由安装包保证完整性；显式要求额外摘要校验时仍走原始文件校验路径。
            if (!settings.VerifyContentHashes && AudioFilePackageAddressResolver.TryGetAddress(
                location, m_packageName, out string address, out string reason))
            {
                content.MarkFilePackage(relativePath, address);
                return;
            }
#endif
            RawFileLease raw = null;
            try
            {
                // 远端更新仍交给 YooAsset 下载与版本校验，原生包持有期间保持文件租约。
                raw = await ResourceManager.Instance.LoadRawFileAsync(location, m_packageName, cancellationToken);
                if (settings.VerifyContentHashes || !raw.IsPackageFile)
                    await CopyVerifiedFileAsync(
                        raw.FilePath, null, file.FilePackageHash, packagePath, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                content.RetainFile(relativePath, raw);
                content.MarkFilePackage(relativePath, null);
                raw = null;
            }
            finally
            {
                raw?.Dispose();
            }
        }
    }
}
