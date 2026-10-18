using UnityEngine;

namespace AlloyFramework
{
    /// <summary>统一记录框架启动阶段和步骤耗时。</summary>
    internal static class FrameworkStartupLog
    {
        private const string PREFIX = "[AlloyFramewrokStartup]"; // 保留已有日志前缀，兼容现有筛选。

        internal static double Now => Time.realtimeSinceStartupAsDouble;

        internal static double ElapsedMilliseconds(double startedAt) => (Now - startedAt) * 1000d;

        internal static void Info(string message)
        {
            AlloyDebug.Log($"{PREFIX} {message}");
        }

        internal static void Step(string name, double startedAt)
        {
            Info($"{name} 完成，耗时 {ElapsedMilliseconds(startedAt):F1} ms");
        }

        internal static void Failure(string name, double startedAt)
        {
            AlloyDebug.Error($"{PREFIX} {name} 失败，耗时 {ElapsedMilliseconds(startedAt):F1} ms");
        }
    }
}
