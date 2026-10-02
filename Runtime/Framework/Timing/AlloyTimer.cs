using System;
using UnityEngine;

namespace AlloyFramework.Timing
{
    /// <summary>主线程计时器入口，由框架自动初始化并通过 IUpdateable 统一调度。</summary>
    public static class AlloyTimer
    {
        /// <summary>无效计时器 ID，值为 0。</summary>
        public const long InvalidId = TimerScheduler.InvalidId;

        /// <summary>无限循环次数，值为 -1。</summary>
        public const int Infinite = TimerScheduler.Infinite;

        private static TimerScheduler _scheduler;

        public static bool IsInitialized => _scheduler != null;

        /// <summary>添加无参计时器。每帧最多回调一次，卡顿错过的次数不会补发。</summary>
        /// <param name="seconds">首次及后续回调的间隔秒数；仅执行一次时允许为 0。</param>
        /// <param name="repeatCount">回调次数：正数为指定次数，-1 表示无限循环。</param>
        /// <param name="callback">到期后在主线程执行的回调。</param>
        /// <param name="useTimeScale">true 使用缩放时间；false 使用非缩放时间。</param>
        /// <returns>计时器唯一 ID，传给 Remove 可提前移除。</returns>
        public static long Add(double seconds, int repeatCount, Action callback,
            bool useTimeScale = true) =>
            Required.Add(seconds, repeatCount, callback, useTimeScale);

        /// <summary>添加带参数计时器。参数保持强类型，不需要通过 object 传递和转换。</summary>
        /// <param name="seconds">首次及后续回调的间隔秒数；仅执行一次时允许为 0。</param>
        /// <param name="repeatCount">回调次数：正数为指定次数，-1 表示无限循环。</param>
        /// <param name="callback">到期后在主线程执行的回调。</param>
        /// <param name="argument">每次回调时传入的参数。</param>
        /// <param name="useTimeScale">true 使用缩放时间；false 使用非缩放时间。</param>
        /// <returns>计时器唯一 ID，传给 Remove 可提前移除。</returns>
        public static long Add<T>(double seconds, int repeatCount, Action<T> callback, T argument,
            bool useTimeScale = true) =>
            Required.Add(seconds, repeatCount, callback, argument, useTimeScale);

        /// <summary>根据 ID 移除计时器。不存在、已完成或已移除时返回 false。</summary>
        public static bool Remove(long timerId) => _scheduler != null && _scheduler.Remove(timerId);

        /// <summary>移除全部计时器。业务层通常应只移除自己保存的计时器 ID。</summary>
        public static void RemoveAll() => _scheduler?.RemoveAll();

        private static TimerScheduler Required => _scheduler ??
            throw new InvalidOperationException("Timer 尚未初始化，请等待框架启动完成。");

        internal static void Initialize()
        {
            if (_scheduler != null) return;
            _scheduler = new TimerScheduler(exception => AlloyDebug.Error(exception));
            GameLoop.Register(_scheduler);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void Shutdown()
        {
            if (_scheduler == null) return;
            GameLoop.Unregister(_scheduler);
            _scheduler.RemoveAll();
            _scheduler = null;
        }
    }
}
