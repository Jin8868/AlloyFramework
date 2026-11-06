using System;
using UnityEngine;

namespace AlloyFramework.Timing
{
    /// <summary>主线程计时器入口，由框架自动初始化并通过 IUpdateable 统一调度。</summary>
    public static class AlloyTimer
    {
        /// <summary>无效计时器 ID，值为 0。</summary>
        public const long INVALIDID = TimerScheduler.INVALIDID;

        /// <summary>无限循环次数，值为 -1。</summary>
        public const int INFINITE = TimerScheduler.INFINITE;

        private static TimerScheduler m_scheduler; // 当前计时器调度器。

        public static bool IsInitialized => m_scheduler != null;

        /// <summary>按秒间隔重复执行。首次执行等待一个间隔，每帧最多回调一次，不补发错过的次数。</summary>
        /// <param name="seconds">首次及后续回调的间隔秒数；仅执行一次时允许为 0。</param>
        /// <param name="repeatCount">回调次数：正数为指定次数，-1 表示无限循环。</param>
        /// <param name="callback">到期后在主线程执行的回调。</param>
        /// <param name="useTimeScale">true 使用缩放时间；false 使用非缩放时间。</param>
        /// <returns>计时器唯一 ID，传给 Remove 可提前移除。</returns>
        /// <exception cref="ArgumentNullException">回调为空。</exception>
        /// <exception cref="ArgumentOutOfRangeException">时间间隔或执行次数无效。</exception>
        /// <exception cref="InvalidOperationException">调度器不可用或 ID 已耗尽。</exception>
        public static long RepeatSeconds(double seconds, int repeatCount, Action callback,
            bool useTimeScale = true) =>
            Required.Schedule(seconds, repeatCount, callback, useTimeScale);

        /// <summary>按秒间隔重复执行带参回调。首次执行等待一个间隔，每帧最多回调一次。</summary>
        /// <param name="seconds">首次及后续回调的间隔秒数；仅执行一次时允许为 0。</param>
        /// <param name="repeatCount">回调次数：正数为指定次数，-1 表示无限循环。</param>
        /// <param name="callback">到期后在主线程执行的回调。</param>
        /// <param name="argument">每次回调时传入的参数。</param>
        /// <param name="useTimeScale">true 使用缩放时间；false 使用非缩放时间。</param>
        /// <returns>计时器唯一 ID，传给 Remove 可提前移除。</returns>
        /// <exception cref="ArgumentNullException">回调为空。</exception>
        /// <exception cref="ArgumentOutOfRangeException">时间间隔或执行次数无效。</exception>
        /// <exception cref="InvalidOperationException">调度器不可用或 ID 已耗尽。</exception>
        public static long RepeatSeconds<T>(double seconds, int repeatCount, Action<T> callback, T argument,
            bool useTimeScale = true) =>
            Required.Schedule(seconds, repeatCount, callback, argument, useTimeScale);

        /// <summary>延迟指定秒数执行一次，在后续调度更新中触发。</summary>
        /// <param name="seconds">延迟秒数，必须为有限的非负数。</param>
        /// <param name="callback">到期后在主线程执行的回调。</param>
        /// <param name="useTimeScale">是否使用缩放时间。</param>
        /// <returns>计时器 ID，可通过 Remove 取消。</returns>
        /// <exception cref="ArgumentNullException">回调为空。</exception>
        /// <exception cref="ArgumentOutOfRangeException">延迟参数无效。</exception>
        /// <exception cref="InvalidOperationException">计时器未初始化或 ID 已耗尽。</exception>
        public static long DelaySeconds(
            double seconds,
            Action callback,
            bool useTimeScale = true) =>
            Required.Schedule(seconds, 1, callback, useTimeScale);

        /// <summary>延迟指定秒数执行一次，在后续调度更新中触发。</summary>
        /// <param name="seconds">延迟秒数，必须为有限的非负数。</param>
        /// <param name="callback">到期后在主线程执行的回调。</param>
        /// <param name="argument">传给回调的参数。</param>
        /// <param name="useTimeScale">是否使用缩放时间。</param>
        /// <returns>计时器 ID，可通过 Remove 取消。</returns>
        /// <exception cref="ArgumentNullException">回调为空。</exception>
        /// <exception cref="ArgumentOutOfRangeException">延迟参数无效。</exception>
        /// <exception cref="InvalidOperationException">计时器未初始化或 ID 已耗尽。</exception>
        public static long DelaySeconds<T>(
            double seconds,
            Action<T> callback,
            T argument,
            bool useTimeScale = true) =>
            Required.Schedule(seconds, 1, callback, argument, useTimeScale);

        /// <summary>延迟指定帧数执行一次，不受时间缩放影响。</summary>
        /// <param name="frameCount">延迟帧数，必须大于 0；1 表示最早在下一帧执行。</param>
        /// <param name="callback">到期后在主线程执行的回调。</param>
        /// <returns>计时器 ID，可通过 Remove 取消。</returns>
        /// <exception cref="ArgumentNullException">回调为空。</exception>
        /// <exception cref="ArgumentOutOfRangeException">延迟参数无效。</exception>
        /// <exception cref="InvalidOperationException">计时器未初始化或 ID 已耗尽。</exception>
        public static long DelayFrames(
            int frameCount,
            Action callback) =>
            Required.ScheduleFrames(frameCount, 1, callback);

        /// <summary>延迟指定帧数执行一次，不受时间缩放影响。</summary>
        /// <param name="frameCount">延迟帧数，必须大于 0；1 表示最早在下一帧执行。</param>
        /// <param name="callback">到期后在主线程执行的回调。</param>
        /// <param name="argument">传给回调的参数。</param>
        /// <returns>计时器 ID，可通过 Remove 取消。</returns>
        /// <exception cref="ArgumentNullException">回调为空。</exception>
        /// <exception cref="ArgumentOutOfRangeException">延迟参数无效。</exception>
        /// <exception cref="InvalidOperationException">计时器未初始化或 ID 已耗尽。</exception>
        public static long DelayFrames<T>(
            int frameCount,
            Action<T> callback,
            T argument) =>
            Required.ScheduleFrames(frameCount, 1, callback, argument);

        /// <summary>每隔指定帧数执行回调，不受时间缩放影响，不补发错过的次数。</summary>
        /// <param name="frameCount">首次及后续执行的帧间隔，必须大于 0；1 表示每帧执行。</param>
        /// <param name="repeatCount">总执行次数，正整数或 INFINITE；首次执行也计入次数。</param>
        /// <param name="callback">到期后在主线程执行的回调。</param>
        /// <returns>计时器 ID，可通过 Remove 取消。</returns>
        /// <exception cref="ArgumentNullException">回调为空。</exception>
        /// <exception cref="ArgumentOutOfRangeException">帧间隔或执行次数无效。</exception>
        /// <exception cref="InvalidOperationException">计时器未初始化或 ID 已耗尽。</exception>
        public static long RepeatFrames(
            int frameCount,
            int repeatCount,
            Action callback) =>
            Required.ScheduleFrames(frameCount, repeatCount, callback);

        /// <summary>每隔指定帧数执行回调，不受时间缩放影响，不补发错过的次数。</summary>
        /// <param name="frameCount">首次及后续执行的帧间隔，必须大于 0；1 表示每帧执行。</param>
        /// <param name="repeatCount">总执行次数，正整数或 INFINITE；首次执行也计入次数。</param>
        /// <param name="callback">到期后在主线程执行的回调。</param>
        /// <param name="argument">传给回调的参数。</param>
        /// <returns>计时器 ID，可通过 Remove 取消。</returns>
        /// <exception cref="ArgumentNullException">回调为空。</exception>
        /// <exception cref="ArgumentOutOfRangeException">帧间隔或执行次数无效。</exception>
        /// <exception cref="InvalidOperationException">计时器未初始化或 ID 已耗尽。</exception>
        public static long RepeatFrames<T>(
            int frameCount,
            int repeatCount,
            Action<T> callback,
            T argument) =>
            Required.ScheduleFrames(frameCount, repeatCount, callback, argument);

        /// <summary>根据 ID 移除计时器。不存在、已完成或已移除时返回 false。</summary>
        /// <param name="timerId">需要取消的计时器 ID。</param>
        /// <returns>是否成功取消仍在等待的计时器。</returns>
        public static bool Remove(long timerId) => m_scheduler != null && m_scheduler.Remove(timerId);

        /// <summary>移除全部计时器。业务层通常应只移除自己保存的计时器 ID。</summary>
        public static void RemoveAll() => m_scheduler?.RemoveAll();

        private static TimerScheduler Required => m_scheduler ??
            throw new InvalidOperationException("Timer 尚未初始化，请等待框架启动完成。");

        private static int GetFrameCount() => Time.frameCount;

        private static void ReportException(Exception exception)
        {
            AlloyDebug.Error(exception);
        }

        internal static void Initialize()
        {
            if (m_scheduler != null) return;
            m_scheduler = new TimerScheduler(ReportException, GetFrameCount);
            GameLoop.Register(m_scheduler);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void Shutdown()
        {
            if (m_scheduler == null) return;
            GameLoop.Unregister(m_scheduler);
            m_scheduler.RemoveAll();
            m_scheduler = null;
        }
    }
}
