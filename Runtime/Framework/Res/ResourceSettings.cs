using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    public enum ResourcePlayMode
    {
        EditorSimulate,
        Offline,
        Host
    }

    public static class ResourceSettings
    {
        public const string DefaultPackageName = "DefaultPackage";

        public static ResourcePlayMode? PlayModeOverride { get; set; }
        public static Func<IAssetService> ServiceFactory { get; set; }
        public static Func<string, string, string> RemoteUrlProvider { get; set; }
        public static int DownloadConcurrency { get; set; } = 8;
        public static int DownloadRetryCount { get; set; } = 3;

        /// <summary>资源初始化前解析并固定本次发布版本。</summary>
        public static Func<CancellationToken, UniTask> StartupPreparation { get; set; }

        /// <summary>返回本次发布指定的包版本，避免各包分别读取可变版本指针。</summary>
        public static Func<string, string> PackageVersionProvider { get; set; }

        /// <summary>接收包下载进度、已下载字节及总字节。</summary>
        public static Action<string, float, long, long> PackageDownloadProgress { get; set; }

        /// <summary>启动操作无进度变化的最大等待秒数。</summary>
        public static int OperationTimeoutSeconds { get; set; } = 120;

        public static ResourcePlayMode PlayMode => PlayModeOverride ??
#if UNITY_EDITOR
            ResourcePlayMode.EditorSimulate;
#else
            ResourcePlayMode.Offline;
#endif
    }
}
