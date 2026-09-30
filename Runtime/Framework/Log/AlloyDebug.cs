using UnityEngine;
using Conditional = System.Diagnostics.ConditionalAttribute;

namespace AlloyFramework
{
    /// <summary>
    /// Development-only logging facade for the Alloy framework.
    /// Calls are removed from non-development player builds.
    /// </summary>
    public static class AlloyDebug
    {
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Log(object message)
        {
            Debug.Log(message);
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Warning(object message)
        {
            Debug.LogWarning(message);
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Error(object message)
        {
            Debug.LogError(message);
        }
    }
}
