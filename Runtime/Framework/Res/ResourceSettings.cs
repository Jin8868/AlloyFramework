using System;

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

        public static ResourcePlayMode PlayMode => PlayModeOverride ??
#if UNITY_EDITOR
            ResourcePlayMode.EditorSimulate;
#else
            ResourcePlayMode.Offline;
#endif
    }
}
