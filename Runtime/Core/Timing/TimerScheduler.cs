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
        public const int Infinite = -1;
        public const long InvalidId = 0;

        private static long s_nextTimerId;

        private readonly List<TimerEntry> _timers = new List<TimerEntry>();
        private readonly List<TimerEntry> _pendingAdds = new List<TimerEntry>();
        private readonly Dictionary<long, TimerEntry> _timerMap = new Dictionary<long, TimerEntry>();
        private readonly Action<Exception> _onException;
        private bool _isUpdating;

        /// <param name="onException">回调异常报告入口；该入口自身不应抛出异常。</param>
        public TimerScheduler(Action<Exception> onException)
        {
            _onException = onException ?? throw new ArgumentNullException(nameof(onException));
        }

        /// <summary>添加无参计时器，返回用于移除计时器的唯一 ID。</summary>
        /// <param name="seconds">首次及后续回调的间隔秒数。</param>
        /// <param name="repeatCount">回调次数：正数为指定次数，-1 表示无限循环。</param>
        /// <param name="callback">到期时执行的回调。</param>
        /// <param name="useTimeScale">true 使用缩放时间；false 使用非缩放时间。</param>
        public long Add(double seconds, int repeatCount, Action callback, bool useTimeScale = true)
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
        public long Add<T>(double seconds, int repeatCount, Action<T> callback, T argument,
            bool useTimeScale = true)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            ValidateSettings(seconds, repeatCount);
            return AddEntry(new ActionTimerEntry<T>(
                NextTimerId(), seconds, repeatCount, useTimeScale, callback, argument));
        }

        /// <summary>根据 ID 移除计时器。不存在、已完成或已移除时返回 false。</summary>
        public bool Remove(long timerId)
        {
            if (timerId == InvalidId || !_timerMap.TryGetValue(timerId, out var timer))
                return false;
            Deactivate(timer);
            if (!_isUpdating) CompactTimers();
            return true;
        }

        public void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (_isUpdating) throw new InvalidOperationException("TimerScheduler cannot update recursively.");
            ValidateDelta(deltaTime, nameof(deltaTime));
            ValidateDelta(unscaledDeltaTime, nameof(unscaledDeltaTime));
            _isUpdating = true;
            try
            {
                for (var index = 0; index < _timers.Count; index++)
                {
                    var timer = _timers[index];
                    if (!timer.IsActive) continue;

                    timer.Remaining -= timer.UseTimeScale ? deltaTime : unscaledDeltaTime;
                    if (timer.Remaining > 0) continue;

                    if (timer.RemainingCount > 0 && --timer.RemainingCount == 0)
                        Deactivate(timer);
                    else
                        timer.Remaining = timer.Interval - (-timer.Remaining % timer.Interval);

                    try
                    {
                        timer.Invoke();
                    }
                    catch (Exception exception)
                    {
                        _onException(exception);
                    }
                }
            }
            finally
            {
                CompactTimers();
                for (var index = 0; index < _pendingAdds.Count; index++)
                {
                    var timer = _pendingAdds[index];
                    if (timer.IsActive) _timers.Add(timer);
                }
                _pendingAdds.Clear();
                _isUpdating = false;
            }
        }

        /// <summary>移除全部计时器，包括当前回调中新添加且尚未开始计时的计时器。</summary>
        public void RemoveAll()
        {
            for (var index = 0; index < _timers.Count; index++)
                _timers[index].IsActive = false;
            for (var index = 0; index < _pendingAdds.Count; index++)
                _pendingAdds[index].IsActive = false;
            _timerMap.Clear();
            _pendingAdds.Clear();
            if (!_isUpdating) _timers.Clear();
        }

        private long AddEntry(TimerEntry timer)
        {
            _timerMap.Add(timer.Id, timer);
            (_isUpdating ? _pendingAdds : _timers).Add(timer);
            return timer.Id;
        }

        private void Deactivate(TimerEntry timer)
        {
            if (!timer.IsActive) return;
            timer.IsActive = false;
            _timerMap.Remove(timer.Id);
        }

        private void CompactTimers()
        {
            var writeIndex = 0;
            for (var readIndex = 0; readIndex < _timers.Count; readIndex++)
            {
                var timer = _timers[readIndex];
                if (timer.IsActive) _timers[writeIndex++] = timer;
            }
            if (writeIndex < _timers.Count)
                _timers.RemoveRange(writeIndex, _timers.Count - writeIndex);
        }

        private static long NextTimerId()
        {
            if (s_nextTimerId == long.MaxValue)
                throw new InvalidOperationException("Timer ID has reached its maximum value.");
            return ++s_nextTimerId;
        }

        private static void ValidateSettings(double seconds, int repeatCount)
        {
            if (repeatCount != Infinite && repeatCount <= 0)
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
            protected TimerEntry(long id, double interval, int repeatCount, bool useTimeScale)
            {
                Id = id;
                Interval = interval;
                Remaining = interval;
                RemainingCount = repeatCount;
                UseTimeScale = useTimeScale;
                IsActive = true;
            }

            internal readonly long Id;
            internal readonly double Interval;
            internal readonly bool UseTimeScale;
            internal double Remaining;
            internal int RemainingCount;
            internal bool IsActive;

            internal abstract void Invoke();
        }

        private sealed class ActionTimerEntry : TimerEntry
        {
            private readonly Action _callback;

            internal ActionTimerEntry(long id, double interval, int repeatCount, bool useTimeScale,
                Action callback) : base(id, interval, repeatCount, useTimeScale)
            {
                _callback = callback;
            }

            internal override void Invoke() => _callback();
        }

        private sealed class ActionTimerEntry<T> : TimerEntry
        {
            private readonly Action<T> _callback;
            private readonly T _argument;

            internal ActionTimerEntry(long id, double interval, int repeatCount, bool useTimeScale,
                Action<T> callback, T argument) : base(id, interval, repeatCount, useTimeScale)
            {
                _callback = callback;
                _argument = argument;
            }

            internal override void Invoke() => _callback(_argument);
        }
    }
}
