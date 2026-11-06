using System;
using System.Collections.Generic;

namespace AlloyFramework.Timing
{
    /// <summary>
    /// 由游戏循环驱动的计时器调度器，仅限在同一线程使用。
    /// 每个计时器每帧最多触发一次，卡顿期间错过的次数不会补发。
    /// </summary>
    internal sealed class TimerScheduler : IUpdateable
    {
        /// <summary>无限重复次数。</summary>
        public const int INFINITE = -1;
        /// <summary>无效计时器 ID。</summary>
        public const long INVALIDID = 0;

        private static long m_nextTimerID; // 跨调度器递增的计时器 ID。

        private readonly List<TimerEntry> m_timers = new List<TimerEntry>(); // 活跃计时器。
        private readonly List<TimerEntry> m_pendingAdds = new List<TimerEntry>(); // 回调中新增的计时器。
        private readonly Dictionary<long, TimerEntry> m_timerMap = new Dictionary<long, TimerEntry>(); // ID 索引。
        private readonly Action<Exception> m_onException; // 回调异常报告入口。
        private bool m_isUpdating; // 是否正在分发回调。
        private readonly Func<int> m_getFrameCount; // 获取宿主当前帧号。

        /// <summary>创建计时器调度器。</summary>
        /// <param name="getFrameCount">获取宿主当前帧号的函数。</param>
        /// <exception cref="ArgumentNullException">异常处理函数或帧号函数为空。</exception>
        /// <param name="onException">回调异常报告入口；该入口自身不应抛出异常。</param>
        public TimerScheduler(Action<Exception> onException, Func<int> getFrameCount)
        {
            m_onException = onException ?? throw new ArgumentNullException(nameof(onException));
            m_getFrameCount = getFrameCount ?? throw new ArgumentNullException(nameof(getFrameCount));
        }

        /// <summary>添加无参计时器，返回用于移除计时器的唯一 ID。</summary>
        /// <param name="seconds">首次及后续回调的间隔秒数。</param>
        /// <param name="repeatCount">回调次数：正数为指定次数，-1 表示无限循环。</param>
        /// <param name="callback">到期时执行的回调。</param>
        /// <param name="useTimeScale">true 使用缩放时间；false 使用非缩放时间。</param>
        /// <returns>可用于取消的计时器 ID。</returns>
        /// <exception cref="ArgumentNullException">回调为空。</exception>
        /// <exception cref="ArgumentOutOfRangeException">时间间隔或执行次数无效。</exception>
        /// <exception cref="InvalidOperationException">调度器不可用或 ID 已耗尽。</exception>
        public long Schedule(double seconds, int repeatCount, Action callback, bool useTimeScale = true)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            ValidateSettings(seconds, repeatCount);
            return AddEntry(new ActionTimerEntry(
                NextTimerId(), seconds, repeatCount, useTimeScale, callback));
        }

        /// <summary>添加带参数计时器，参数保持强类型，不需要转换 object。</summary>
        /// <param name="seconds">首次及后续回调的间隔秒数。</param>
        /// <param name="repeatCount">回调次数：正数为指定次数，-1 表示无限循环。</param>
        /// <param name="callback">到期时执行的回调。</param>
        /// <param name="argument">每次回调时传入的参数。</param>
        /// <param name="useTimeScale">true 使用缩放时间；false 使用非缩放时间。</param>
        /// <returns>可用于取消的计时器 ID。</returns>
        /// <exception cref="ArgumentNullException">回调为空。</exception>
        /// <exception cref="ArgumentOutOfRangeException">时间间隔或执行次数无效。</exception>
        /// <exception cref="InvalidOperationException">调度器不可用或 ID 已耗尽。</exception>
        public long Schedule<T>(double seconds, int repeatCount, Action<T> callback, T argument,
            bool useTimeScale = true)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            ValidateSettings(seconds, repeatCount);
            return AddEntry(new ActionTimerEntry<T>(
                NextTimerId(), seconds, repeatCount, useTimeScale, callback, argument));
        }

        /// <summary>按帧间隔执行指定次数，不受时间缩放影响。</summary>
        /// <param name="repeatCount">总执行次数，正整数或 INFINITE。</param>
        /// <param name="frameCount">延迟帧数，必须大于 0；1 表示最早在下一帧执行。</param>
        /// <param name="callback">到期后执行的回调。</param>
        /// <returns>可用于取消的计时器 ID。</returns>
        /// <exception cref="ArgumentNullException">回调为空。</exception>
        /// <exception cref="ArgumentOutOfRangeException">帧数或执行次数无效。</exception>
        /// <exception cref="InvalidOperationException">计时器 ID 已耗尽。</exception>
        public long ScheduleFrames(int frameCount, int repeatCount, Action callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (frameCount < 1) throw new ArgumentOutOfRangeException(nameof(frameCount));
            if (repeatCount != INFINITE && repeatCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(repeatCount));

            // 共用计时器注册和取消机制，帧延迟只改变到期判断。
            var timer = new ActionTimerEntry(NextTimerId(), 0, repeatCount, false, callback);
            timer.StartFrame = m_getFrameCount();
            timer.FrameDelay = frameCount;
            return AddEntry(timer);
        }

        /// <summary>按帧间隔执行指定次数，不受时间缩放影响。</summary>
        /// <param name="repeatCount">总执行次数，正整数或 INFINITE。</param>
        /// <param name="frameCount">延迟帧数，必须大于 0；1 表示最早在下一帧执行。</param>
        /// <param name="callback">到期后执行的回调。</param>
        /// <param name="argument">传给回调的参数。</param>
        /// <returns>可用于取消的计时器 ID。</returns>
        /// <exception cref="ArgumentNullException">回调为空。</exception>
        /// <exception cref="ArgumentOutOfRangeException">帧数或执行次数无效。</exception>
        /// <exception cref="InvalidOperationException">计时器 ID 已耗尽。</exception>
        public long ScheduleFrames<T>(int frameCount, int repeatCount, Action<T> callback, T argument)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (frameCount < 1) throw new ArgumentOutOfRangeException(nameof(frameCount));
            if (repeatCount != INFINITE && repeatCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(repeatCount));

            // 共用计时器注册和取消机制，帧延迟只改变到期判断。
            var timer = new ActionTimerEntry<T>(NextTimerId(), 0, repeatCount, false, callback, argument);
            timer.StartFrame = m_getFrameCount();
            timer.FrameDelay = frameCount;
            return AddEntry(timer);
        }

        /// <summary>根据 ID 移除计时器。不存在、已完成或已移除时返回 false。</summary>
        /// <param name="timerId">需要取消的计时器 ID。</param>
        /// <returns>是否成功取消仍在等待的计时器。</returns>
        public bool Remove(long timerId)
        {
            if (timerId == INVALIDID || !m_timerMap.TryGetValue(timerId, out var timer))
                return false;
            Deactivate(timer);
            if (!m_isUpdating) CompactTimers();
            return true;
        }

        /// <summary>推进计时器并调用到期回调。</summary>
        /// <param name="deltaTime">缩放时间增量。</param>
        /// <param name="unscaledDeltaTime">非缩放时间增量。</param>
        /// <exception cref="InvalidOperationException">发生递归更新。</exception>
        /// <exception cref="ArgumentOutOfRangeException">时间增量无效。</exception>
        public void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (m_isUpdating) throw new InvalidOperationException("TimerScheduler cannot update recursively.");
            ValidateDelta(deltaTime, nameof(deltaTime));
            ValidateDelta(unscaledDeltaTime, nameof(unscaledDeltaTime));
            m_isUpdating = true;
            try
            {
                for (var index = 0; index < m_timers.Count; index++)
                {
                    var timer = m_timers[index];
                    if (!timer.IsActive) continue;

                    // 帧计时使用宿主帧号，避免注册当帧被提前计入延迟。
                    if (timer.FrameDelay > 0)
                    {
                        if (unchecked((uint)(m_getFrameCount() - timer.StartFrame)) < (uint)timer.FrameDelay)
                            continue;
                    }
                    else
                    {
                        timer.Remaining -= timer.UseTimeScale ? deltaTime : unscaledDeltaTime;
                        if (timer.Remaining > 0) continue;
                    }

                    if (timer.RemainingCount > 0 && --timer.RemainingCount == 0)
                        Deactivate(timer);
                    else if (timer.FrameDelay > 0)
                    {
                        // 从本次触发帧开始等待下一个间隔，不补发错过的回调。
                        timer.StartFrame = m_getFrameCount();
                    }
                    else
                        timer.Remaining = timer.Interval - (-timer.Remaining % timer.Interval);

                    try
                    {
                        timer.Invoke();
                    }
                    catch (Exception exception)
                    {
                        m_onException(exception);
                    }
                }
            }
            finally
            {
                CompactTimers();
                for (var index = 0; index < m_pendingAdds.Count; index++)
                {
                    var timer = m_pendingAdds[index];
                    if (timer.IsActive) m_timers.Add(timer);
                }
                m_pendingAdds.Clear();
                m_isUpdating = false;
            }
        }

        /// <summary>移除全部计时器，包括当前回调中新添加且尚未开始计时的计时器。</summary>
        public void RemoveAll()
        {
            for (var index = 0; index < m_timers.Count; index++)
                m_timers[index].IsActive = false;
            for (var index = 0; index < m_pendingAdds.Count; index++)
                m_pendingAdds[index].IsActive = false;
            m_timerMap.Clear();
            m_pendingAdds.Clear();
            if (!m_isUpdating) m_timers.Clear();
        }

        private long AddEntry(TimerEntry timer)
        {
            m_timerMap.Add(timer.Id, timer);
            (m_isUpdating ? m_pendingAdds : m_timers).Add(timer);
            return timer.Id;
        }

        private void Deactivate(TimerEntry timer)
        {
            if (!timer.IsActive) return;
            timer.IsActive = false;
            m_timerMap.Remove(timer.Id);
        }

        private void CompactTimers()
        {
            var writeIndex = 0;
            for (var readIndex = 0; readIndex < m_timers.Count; readIndex++)
            {
                var timer = m_timers[readIndex];
                if (timer.IsActive) m_timers[writeIndex++] = timer;
            }
            if (writeIndex < m_timers.Count)
                m_timers.RemoveRange(writeIndex, m_timers.Count - writeIndex);
        }

        private static long NextTimerId()
        {
            if (m_nextTimerID == long.MaxValue)
                throw new InvalidOperationException("Timer ID has reached its maximum value.");
            return ++m_nextTimerID;
        }

        private static void ValidateSettings(double seconds, int repeatCount)
        {
            if (repeatCount != INFINITE && repeatCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(repeatCount),
                    "回调次数必须为正数，或使用 -1 表示无限循环。");
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 ||
                (repeatCount != 1 && seconds == 0))
                throw new ArgumentOutOfRangeException(nameof(seconds),
                    "单次计时允许 0 秒；重复计时的间隔必须大于 0 秒。");
        }

        private static void ValidateDelta(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(name);
        }

        private abstract class TimerEntry
        {
            internal int StartFrame;
            internal int FrameDelay;
            internal readonly long Id;
            internal readonly double Interval;
            internal readonly bool UseTimeScale;
            internal double Remaining;
            internal int RemainingCount;
            internal bool IsActive;

            protected TimerEntry(long id, double interval, int repeatCount, bool useTimeScale)
            {
                Id = id;
                Interval = interval;
                Remaining = interval;
                RemainingCount = repeatCount;
                UseTimeScale = useTimeScale;
                IsActive = true;
            }

            internal abstract void Invoke();
        }

        private sealed class ActionTimerEntry : TimerEntry
        {
            private readonly Action m_callback; // 无参回调。

            internal ActionTimerEntry(long id, double interval, int repeatCount, bool useTimeScale,
                Action callback) : base(id, interval, repeatCount, useTimeScale)
            {
                m_callback = callback;
            }

            internal override void Invoke() => m_callback();
        }

        private sealed class ActionTimerEntry<T> : TimerEntry
        {
            private readonly Action<T> m_callback; // 带参回调。
            private readonly T m_argument; // 回调参数。

            internal ActionTimerEntry(long id, double interval, int repeatCount, bool useTimeScale,
                Action<T> callback, T argument) : base(id, interval, repeatCount, useTimeScale)
            {
                m_callback = callback;
                m_argument = argument;
            }

            internal override void Invoke() => m_callback(m_argument);
        }
    }
}
