using System;

namespace AlloyFramework.HybridCLR
{
    /// <summary>
    /// 代码包中的发布清单。
    /// </summary>
    [Serializable]
    public sealed class HotUpdateCodeManifest
    {
        /// <summary>发布目标平台。</summary>
        public string Platform;
        /// <summary>对应主包基线。</summary>
        public string BaselineID;
        /// <summary>本次代码发布版本。</summary>
        public string ReleaseID;
        /// <summary>提供业务入口的程序集名称。</summary>
        public string RuntimeAssemblyName;
        /// <summary>显式业务安装入口的类型名称。</summary>
        public string InstallerTypeName;
        /// <summary>与主包匹配的补充元数据。</summary>
        public HotUpdateCodeFile[] Metadata;
        /// <summary>按依赖顺序排列的业务程序集。</summary>
        public HotUpdateCodeFile[] Assemblies;
    }

    /// <summary>
    /// 待校验的程序集文件。
    /// </summary>
    [Serializable]
    public sealed class HotUpdateCodeFile
    {
        /// <summary>不含扩展名的程序集名称。</summary>
        public string AssemblyName;
        /// <summary>原始文件的逻辑资源地址。</summary>
        public string Location;
        /// <summary>原始 DLL 的 SHA-256。</summary>
        public string SHA256;
    }

    /// <summary>
    /// 主包内嵌的代码基线身份。
    /// </summary>
    [Serializable]
    public sealed class HotUpdatePlayerIdentity
    {
        /// <summary>主包目标平台。</summary>
        public string Platform;
        /// <summary>主包内嵌的基线标识。</summary>
        public string BaselineID;
    }

}
