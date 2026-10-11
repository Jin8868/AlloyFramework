using System;
using System.IO;
using UnityEditor.PackageManager;
using UnityEngine;

namespace AlloyFramework.Net.Editor
{
    [Serializable]
    internal sealed class NetworkGenerationSettings
    {
        /// <summary>业务协议根目录。</summary>
        public string ProtoRoot = "Protocol/Proto";
        /// <summary>固定版本生成器。</summary>
        public string ProtocPath = "Tools/Protobuf/Windows/34.1/bin/protoc.exe";
        /// <summary>标准协议目录。</summary>
        public string StandardInclude = "Tools/Protobuf/Windows/34.1/include";
        /// <summary>框架 option 根目录。</summary>
        public string OptionsRoot = "Packages/com.alloy.framework/Editor/Network/Protocol";
        /// <summary>工具拥有的生成目录。</summary>
        public string OutputRoot = "Assets/Scripts/Protocol/Generated";
        /// <summary>协议注册表的默认命名空间。</summary>
        public string GeneratedNamespace = "Game.Protocol";
        /// <summary>分部网络服务的业务命名空间。</summary>
        public string ServiceNamespace = "Game.Network";
        /// <summary>手写分部文件和生成服务共同所属的程序集。</summary>
        public string ServiceAssembly = "Game.Runtime";
        /// <summary>手写业务处理骨架目录，已有实现不整体覆盖。</summary>
        public string BusinessServiceRoot = "Assets/Scripts/Runtime/Network/Services";
        /// <summary>线上版本。</summary>
        public int ProtocolVersion = 1;
        /// <summary>勾选集合；搜索过滤不改变它。</summary>
        public string[] SelectedFiles = Array.Empty<string>();

        internal static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        internal static string Resolve(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains("\""))
                throw new InvalidOperationException("生成配置必须使用不含引号的项目或包相对路径。");
            const string packagePrefix = "Packages/com.alloy.framework/";
            if (path.Replace('\\', '/').StartsWith(packagePrefix, StringComparison.Ordinal))
            {
                var package = PackageInfo.FindForAssetPath("Packages/com.alloy.framework/package.json");
                if (package == null) throw new InvalidOperationException("找不到 AlloyFramework 包。");
                return ChildPath(package.resolvedPath, path.Substring(packagePrefix.Length));
            }
            return ChildPath(ProjectRoot, path);
        }

        internal static string ChildPath(string root, string relative)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative));
            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("路径超出所属目录。");
            return fullPath;
        }
    }

    [Serializable]
    internal sealed class NetworkOutputManifest
    {
        /// <summary>生成器规则版本。</summary>
        public string GeneratorVersion = "2";
        /// <summary>生成命名空间。</summary>
        public string GeneratedNamespace;
        /// <summary>线上协议版本。</summary>
        public int ProtocolVersion;
        /// <summary>成功生成过的协议。</summary>
        public NetworkFileRecord[] Files = Array.Empty<NetworkFileRecord>();
        /// <summary>注册表文件及哈希。</summary>
        public NetworkOwnedOutput[] RegistryOutputs = Array.Empty<NetworkOwnedOutput>();
        /// <summary>历史消息编号，即使协议删除也保留，防止编号复用。</summary>
        public NetworkIDRecord[] AssignedIDs = Array.Empty<NetworkIDRecord>();
    }

    [Serializable]
    internal sealed class NetworkIDRecord
    {
        /// <summary>已分配线上编号。</summary>
        public uint MessageID;
        /// <summary>原始消息全限定名称。</summary>
        public string ProtoName;
    }

    [Serializable]
    internal sealed class NetworkFileRecord
    {
        /// <summary>协议相对路径。</summary>
        public string Source;
        /// <summary>源文件及所有传递依赖的指纹。</summary>
        public string Fingerprint;
        /// <summary>所属生成文件。</summary>
        public NetworkOwnedOutput[] Outputs = Array.Empty<NetworkOwnedOutput>();
    }

    [Serializable]
    internal sealed class NetworkOwnedOutput
    {
        /// <summary>输出相对路径。</summary>
        public string Path;
        /// <summary>最后成功生成时的哈希。</summary>
        public string Hash;
    }
}
