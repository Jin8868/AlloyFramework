using System;
using System.IO;
using YooAsset.Editor;

namespace AlloyFramework.Editor
{
    /// <summary>
    /// 为 AlloyFramework 包内的资源生成与包安装位置无关的地址。
    /// </summary>
    [DisplayName("定位地址: AlloyFramework Res 相对路径")]
    public sealed class AddressByAlloyFrameworkResPath : IAddressRule
    {
        private const string RESROOT = "Packages/com.alloy.framework/Runtime/Res/"; // 框架资源在 Unity 中的路径。

        /// <summary>
        /// 取框架 Res 目录下不含扩展名的相对路径作为资源地址。
        /// </summary>
        /// <param name="data">YooAsset 提供的资源及收集配置。</param>
        /// <returns>使用正斜杠分隔的资源地址。</returns>
        /// <exception cref="InvalidOperationException">资源未位于框架 Res 目录。</exception>
        public string GetAssetAddress(AddressRuleData data)
        {
            // 拒绝误配到其他目录的资源，避免生成可与业务资源冲突的地址。
            if (!data.AssetPath.StartsWith(RESROOT, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"框架资源必须位于 {RESROOT}，当前路径为 {data.AssetPath}。");
            }

            var relativePath = data.AssetPath.Substring(RESROOT.Length);
            return Path.ChangeExtension(relativePath, null).Replace('\\', '/');
        }
    }
}
