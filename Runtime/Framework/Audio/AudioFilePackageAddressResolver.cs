#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;

namespace AlloyFramework.Audio
{
    /// <summary>借用 YooAsset 当前清单的来源选择结果，解析可供原生播放器直读的 APK 音频文件。</summary>
    internal static class AudioFilePackageAddressResolver
    {
        private const BindingFlags INSTANCEFLAGS =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic; // 兼容现有 AOT 包的查询入口。
        private const int RAWBUNDLETYPE = 3; // YooAsset 2.3.19 的原始文件类型标识。
        private static readonly jvalue[] m_noArguments = new jvalue[0]; // JNI 无参调用复用空数组。

        internal static bool TryGetAddress(
            string location, string packageName, out string address, out string reason)
        {
            address = null;
            reason = null;
            try
            {
                // 复用实际加载时的来源判定，避免用旧内置清单或手拼路径绕过热更新版本。
                Type yooAssetsType = Type.GetType("YooAsset.YooAssets, YooAsset", false);
                MethodInfo getPackage = yooAssetsType?.GetMethod(
                    "TryGetPackage", BindingFlags.Public | BindingFlags.Static);
                object package = getPackage?.Invoke(null, new object[] { packageName });
                if (package == null)
                {
                    reason = "资源包未就绪";
                    return false;
                }
                Type packageType = package.GetType();
                MethodInfo getAssetInfo = packageType.GetMethod(
                    "GetAssetInfo", INSTANCEFLAGS, null, new[] { typeof(string) }, null);
                object assetInfo = getAssetInfo?.Invoke(package, new object[] { location });
                PropertyInfo isInvalid = assetInfo?.GetType().GetProperty("IsInvalid", INSTANCEFLAGS);
                if (assetInfo == null || isInvalid == null || (bool)isInvalid.GetValue(assetInfo))
                {
                    reason = "当前清单中没有音频文件地址";
                    return false;
                }
                FieldInfo queryField = packageType.GetField("_bundleQuery", INSTANCEFLAGS);
                object query = queryField?.GetValue(package);
                Type queryInterface = query?.GetType().GetInterface("YooAsset.IBundleQuery");
                MethodInfo getBundleInfo = queryInterface?.GetMethod("GetMainBundleInfo");
                object bundleInfo = getBundleInfo?.Invoke(query, new object[] { assetInfo });
                if (bundleInfo == null)
                {
                    reason = "安装包缺少 YooAsset 来源查询元数据";
                    return false;
                }

                Type bundleInfoType = bundleInfo.GetType();
                object fileSystem = bundleInfoType.GetField("_fileSystem", INSTANCEFLAGS)?.GetValue(bundleInfo);
                object bundle = bundleInfoType.GetField("Bundle", INSTANCEFLAGS)?.GetValue(bundleInfo);
                if (fileSystem?.GetType().FullName != "YooAsset.DefaultBuildinFileSystem" || bundle == null)
                {
                    reason = "当前版本由缓存、远端或其他文件系统提供";
                    return false;
                }
                Type bundleType = bundle.GetType();
                FieldInfo encryptedField = bundleType.GetField("Encrypted", INSTANCEFLAGS);
                FieldInfo sizeField = bundleType.GetField("FileSize", INSTANCEFLAGS);
                PropertyInfo typeProperty = bundleType.GetProperty("BundleType", INSTANCEFLAGS);
                if (encryptedField == null || sizeField == null || typeProperty == null)
                {
                    reason = "安装包缺少原始文件描述元数据";
                    return false;
                }
                if ((bool)encryptedField.GetValue(bundle) ||
                    (int)typeProperty.GetValue(bundle) != RAWBUNDLETYPE)
                {
                    reason = "音频文件不是未加密原始文件";
                    return false;
                }

                MethodInfo getBuildinPath = fileSystem.GetType().GetMethod("GetBuildinFileLoadPath", INSTANCEFLAGS);
                string candidate = getBuildinPath?.Invoke(fileSystem, new object[] { bundle }) as string;
                string root = Application.streamingAssetsPath.TrimEnd('/') + "/";
                if (candidate == null || !candidate.StartsWith("jar:", StringComparison.OrdinalIgnoreCase) ||
                    !candidate.StartsWith(root, StringComparison.Ordinal))
                {
                    reason = "音频文件不属于当前安装包的 StreamingAssets";
                    return false;
                }
                if (!CheckFileRange(candidate, (long)sizeField.GetValue(bundle), out reason)) return false;

                // APK 内容不会被资源缓存清理删除，不需要创建缓存文件或持有落盘租约。
                address = candidate;
                return true;
            }
            catch (Exception exception)
            {
                // 旧包裁剪或 SDK 差异仅关闭优化，不改变原有资源加载的成功与失败语义。
                reason = "直读检查失败：" + exception.GetType().Name;
                return false;
            }
        }

        private static bool CheckFileRange(string address, long expectedLength, out string reason)
        {
            reason = null;
            const string ASSETSEPARATOR = "!/assets/";
            int separator = address.IndexOf(ASSETSEPARATOR, StringComparison.Ordinal);
            if (separator < 0 || expectedLength <= 0)
            {
                reason = "APK 路径或清单长度无效";
                return false;
            }
            string assetPath = Uri.UnescapeDataString(address.Substring(separator + ASSETSEPARATOR.Length));

            // openFd 仅打开未压缩条目；长度查询不会读取整个音频文件，也不触发文件复制。
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaObject assets = activity.Call<AndroidJavaObject>("getAssets"))
            using (AndroidJavaObject descriptor = assets.Call<AndroidJavaObject>("openFd", assetPath))
            {
                try
                {
                    // 使用非泛型 JNI 长整型入口，避免热更引入新的 Call<long> AOT 泛型实例。
                    IntPtr getLength = AndroidJNI.GetMethodID(descriptor.GetRawClass(), "getLength", "()J");
                    long length = AndroidJNI.CallLongMethod(descriptor.GetRawObject(), getLength, m_noArguments);
                    if (length == expectedLength) return true;
                    reason = "APK 条目长度与当前清单不一致";
                    return false;
                }
                finally
                {
                    // AndroidJavaObject.Dispose 只释放 JNI 引用，文件描述符必须显式关闭。
                    descriptor.Call("close");
                }
            }
        }
    }
}
#endif
