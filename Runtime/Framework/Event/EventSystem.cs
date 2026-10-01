using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AlloyFramework
{

/// <summary>
/// 全局事件中心。发送阶段不创建 object[]、不装箱值类型，也不复制监听器列表。
/// </summary>
public sealed partial class EventSystem
{
    private static readonly EventSystem _instance = new EventSystem();
    private readonly Dictionary<string, EventBucket> _events = new Dictionary<string, EventBucket>(64);
#if UNITY_EDITOR
    private DispatchDebugInfo[] _dispatchHistory;
    private int _nextDispatchIndex;
    private int _dispatchHistoryCount;
    private ListenerExceptionDebugInfo[] _exceptionHistory;
    private int _nextExceptionIndex;
    private int _exceptionHistoryCount;
    private CleanupDebugInfo[] _cleanupHistory;
    private int _nextCleanupIndex;
    private int _cleanupHistoryCount;
#endif

    public static EventSystem Instance => _instance;

    public int EventCount => _events.Count;

    /// <summary>开启后记录最近派发历史；默认关闭，不影响正式环境的派发路径。</summary>
#if UNITY_EDITOR
    public bool DiagnosticsEnabled { get; set; }

    /// <summary>仅建议在编辑器排查时开启。之后新增的监听器会记录订阅调用栈。</summary>
    public bool CaptureRegistrationStackTrace { get; set; }

    /// <summary>开发期发现已销毁 Unity 对象仍被派发时，记录一次警告。</summary>
    public bool WarnOnDestroyedListener { get; set; }

    public bool CaptureDispatchStackTrace { get; set; }
#endif

    private EventSystem() { }

    public void AddEventListener(string eventName, Action listener) => GetOrCreate<Action>(eventName).Add(listener);
    public void AddEventListener<T>(string eventName, Action<T> listener) => GetOrCreate<Action<T>>(eventName).Add(listener);
    public void AddEventListener<T1, T2>(string eventName, Action<T1, T2> listener) => GetOrCreate<Action<T1, T2>>(eventName).Add(listener);
    public void AddEventListener<T1, T2, T3>(string eventName, Action<T1, T2, T3> listener) => GetOrCreate<Action<T1, T2, T3>>(eventName).Add(listener);
    public void AddEventListener<T1, T2, T3, T4>(string eventName, Action<T1, T2, T3, T4> listener) => GetOrCreate<Action<T1, T2, T3, T4>>(eventName).Add(listener);

    /// <summary>
    /// 为监听器关联任意生命周期所有者。系统仅弱引用 owner；owner 被销毁或被 GC 回收后，
    /// 调试器会标记该监听器为泄漏，并在下一次派发后自动移除它。
    /// </summary>
    public void AddEventListener(object owner, string eventName, Action listener) => GetOrCreate<Action>(eventName).Add(listener, owner);
    public void AddEventListener<T>(object owner, string eventName, Action<T> listener) => GetOrCreate<Action<T>>(eventName).Add(listener, owner);
    public void AddEventListener<T1, T2>(object owner, string eventName, Action<T1, T2> listener) => GetOrCreate<Action<T1, T2>>(eventName).Add(listener, owner);
    public void AddEventListener<T1, T2, T3>(object owner, string eventName, Action<T1, T2, T3> listener) => GetOrCreate<Action<T1, T2, T3>>(eventName).Add(listener, owner);
    public void AddEventListener<T1, T2, T3, T4>(object owner, string eventName, Action<T1, T2, T3, T4> listener) => GetOrCreate<Action<T1, T2, T3, T4>>(eventName).Add(listener, owner);

    public void RemoveEventListener(string eventName, Action listener) => Remove(eventName, listener);
    public void RemoveEventListener<T>(string eventName, Action<T> listener) => Remove(eventName, listener);
    public void RemoveEventListener<T1, T2>(string eventName, Action<T1, T2> listener) => Remove(eventName, listener);
    public void RemoveEventListener<T1, T2, T3>(string eventName, Action<T1, T2, T3> listener) => Remove(eventName, listener);
    public void RemoveEventListener<T1, T2, T3, T4>(string eventName, Action<T1, T2, T3, T4> listener) => Remove(eventName, listener);

    /// <summary>以下泛型重载均为 0 GC 的正常发送路径。</summary>
    public void SendEvent(string eventName) { if (TryGet<Action>(eventName, out var bucket)) bucket.Invoke(); }
    public void SendEvent<T>(string eventName, T value) { if (TryGet<Action<T>>(eventName, out var bucket)) bucket.Invoke(value); }
    public void SendEvent<T1, T2>(string eventName, T1 value1, T2 value2) { if (TryGet<Action<T1, T2>>(eventName, out var bucket)) bucket.Invoke(value1, value2); }
    public void SendEvent<T1, T2, T3>(string eventName, T1 value1, T2 value2, T3 value3) { if (TryGet<Action<T1, T2, T3>>(eventName, out var bucket)) bucket.Invoke(value1, value2, value3); }
    public void SendEvent<T1, T2, T3, T4>(string eventName, T1 value1, T2 value2, T3 value3, T4 value4) { if (TryGet<Action<T1, T2, T3, T4>>(eventName, out var bucket)) bucket.Invoke(value1, value2, value3, value4); }

    /// <summary>将状态写入调用方复用的 List，适用于自定义调试 UI 且不会产生 GC。</summary>
#if UNITY_EDITOR
    public void GetEventDebugInfo(List<EventDebugInfo> results)
    {
        if (results == null) throw new ArgumentNullException(nameof(results));
        results.Clear();
        foreach (var pair in _events) results.Add(pair.Value.GetDebugInfo(pair.Key));
    }

    public void GetListenerDebugInfo(string eventName, List<EventListenerDebugInfo> results)
    {
        if (results == null) throw new ArgumentNullException(nameof(results));
        results.Clear();
        if (!string.IsNullOrEmpty(eventName) && _events.TryGetValue(eventName, out var bucket)) bucket.AppendListenerDebugInfo(results);
    }

    /// <summary>按从新到旧的顺序取得最近派发记录；调用方应复用 results。</summary>
    public void GetDispatchDebugInfo(List<DispatchDebugInfo> results)
    {
        if (results == null) throw new ArgumentNullException(nameof(results));
        results.Clear();
        if (_dispatchHistory == null) return;
        for (var i = 0; i < _dispatchHistoryCount; i++)
        {
            var index = (_nextDispatchIndex - 1 - i + _dispatchHistory.Length) % _dispatchHistory.Length;
            results.Add(_dispatchHistory[index]);
        }
    }

    public void ClearDispatchHistory()
    {
        _nextDispatchIndex = 0;
        _dispatchHistoryCount = 0;
    }

    public void GetListenerExceptionDebugInfo(List<ListenerExceptionDebugInfo> results) => CopyNewest(_exceptionHistory, _nextExceptionIndex, _exceptionHistoryCount, results);
    public void GetCleanupDebugInfo(List<CleanupDebugInfo> results) => CopyNewest(_cleanupHistory, _nextCleanupIndex, _cleanupHistoryCount, results);
    public void ClearDiagnosticHistory()
    {
        if (_dispatchHistory != null) Array.Clear(_dispatchHistory, 0, _dispatchHistory.Length);
        if (_exceptionHistory != null) Array.Clear(_exceptionHistory, 0, _exceptionHistory.Length);
        if (_cleanupHistory != null) Array.Clear(_cleanupHistory, 0, _cleanupHistory.Length);
        _nextDispatchIndex = _dispatchHistoryCount = 0;
        _nextExceptionIndex = _exceptionHistoryCount = 0;
        _nextCleanupIndex = _cleanupHistoryCount = 0;
    }

    private static void CopyNewest<T>(T[] source, int nextIndex, int count, List<T> results)
    {
        if (results == null) throw new ArgumentNullException(nameof(results));
        results.Clear();
        if (source == null) return;
        for (var i = 0; i < count; i++) results.Add(source[(nextIndex - 1 - i + source.Length) % source.Length]);
    }
#endif

    /// <summary>移除 Target 已销毁的 Unity 对象监听器，返回移除数量。</summary>
    public int PurgeDestroyedListeners()
    {
        var removedCount = 0;
        foreach (var pair in _events) removedCount += pair.Value.PurgeDestroyedListeners();
        return removedCount;
    }

    public void ClearAllListeners() => _events.Clear();

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnSubsystemRegistration()
    {
        _instance.ClearAllListeners();
#if UNITY_EDITOR
        _instance.DiagnosticsEnabled = false;
        _instance.CaptureRegistrationStackTrace = false;
        _instance.CaptureDispatchStackTrace = false;
        _instance.WarnOnDestroyedListener = false;
        _instance.ClearDiagnosticHistory();
#endif
    }

    private EventBucket<TDelegate> GetOrCreate<TDelegate>(string eventName) where TDelegate : Delegate
    {
        ValidateEventName(eventName);
        if (_events.TryGetValue(eventName, out var existing))
        {
            if (existing is EventBucket<TDelegate> typedBucket) return typedBucket;
            ThrowSignatureMismatch(eventName, existing.Signature, typeof(TDelegate).Name);
        }
        var newBucket = new EventBucket<TDelegate>(this, eventName, typeof(TDelegate).Name);
        _events.Add(eventName, newBucket);
        return newBucket;
    }

    private bool TryGet<TDelegate>(string eventName, out EventBucket<TDelegate> bucket) where TDelegate : Delegate
    {
        bucket = null;
        if (string.IsNullOrEmpty(eventName) || !_events.TryGetValue(eventName, out var existing)) return false;
        if (existing is EventBucket<TDelegate> typedBucket) { bucket = typedBucket; return true; }
        ThrowSignatureMismatch(eventName, existing.Signature, typeof(TDelegate).Name);
        return false;
    }

    private void Remove<TDelegate>(string eventName, TDelegate listener) where TDelegate : Delegate
    {
        if (listener != null && !string.IsNullOrEmpty(eventName) && TryGet<TDelegate>(eventName, out var bucket)) bucket.Remove(listener);
    }

    private static void ValidateEventName(string eventName)
    {
        if (string.IsNullOrEmpty(eventName)) throw new ArgumentException("Event name cannot be null or empty.", nameof(eventName));
    }

    private static void ThrowSignatureMismatch(string eventName, string actual, string requested)
    {
        throw new InvalidOperationException($"Event '{eventName}' is registered as {actual}, but was used as {requested}.");
    }

#if UNITY_EDITOR
    public readonly struct EventDebugInfo
    {
        public readonly string Name;
        public readonly string Signature;
        public readonly int ListenerCount;
        public readonly int InvalidListenerCount;
        public readonly int UnownedManagedTargetCount;
        public readonly int DispatchCount;
        public readonly long LastDispatchUtcTicks;
        public readonly double LastDispatchMilliseconds;
        public readonly double TotalDispatchMilliseconds;
        public readonly double MaxDispatchMilliseconds;
        public readonly int ExceptionCount;
        internal EventDebugInfo(string name, string signature, int listenerCount, int invalidListenerCount, int unownedManagedTargetCount, int dispatchCount, long lastDispatchUtcTicks, double lastDispatchMilliseconds, double totalDispatchMilliseconds, double maxDispatchMilliseconds, int exceptionCount)
        { Name = name; Signature = signature; ListenerCount = listenerCount; InvalidListenerCount = invalidListenerCount; UnownedManagedTargetCount = unownedManagedTargetCount; DispatchCount = dispatchCount; LastDispatchUtcTicks = lastDispatchUtcTicks; LastDispatchMilliseconds = lastDispatchMilliseconds; TotalDispatchMilliseconds = totalDispatchMilliseconds; MaxDispatchMilliseconds = maxDispatchMilliseconds; ExceptionCount = exceptionCount; }
    }

    public readonly struct EventListenerDebugInfo
    {
        public readonly string EventName;
        public readonly string MethodName;
        public readonly string DeclaringType;
        public readonly string TargetName;
        public readonly UnityEngine.Object Target;
        public readonly UnityEngine.Object Owner;
        public readonly string OwnerName;
        public readonly bool IsDestroyed;
        public readonly bool IsOwnerDestroyed;
        public readonly bool IsOwnerCollected;
        public readonly bool IsUnownedManagedTarget;
        public readonly bool IsInactiveBehaviour;
        public readonly bool HasIssue;
        public readonly string IssueDescription;
        public readonly string SuggestedFix;
        public readonly int RegisteredFrame;
        public readonly long RegisteredUtcTicks;
        public readonly string RegistrationStackTrace;

        internal EventListenerDebugInfo(string eventName, Delegate listener, int registeredFrame, long registeredUtcTicks, string registrationStackTrace, object owner, bool hasOwner, string ownerTypeName)
        {
            EventName = eventName;
            MethodName = listener.Method.Name;
            DeclaringType = listener.Method.DeclaringType == null ? string.Empty : listener.Method.DeclaringType.FullName;
            Target = listener.Target as UnityEngine.Object;
            IsDestroyed = listener.Target is UnityEngine.Object unityTarget && unityTarget == null;
            Owner = owner as UnityEngine.Object;
            IsOwnerCollected = hasOwner && owner == null;
            IsOwnerDestroyed = owner is UnityEngine.Object unityOwner && unityOwner == null;
            IsUnownedManagedTarget = !hasOwner && listener.Target != null && !(listener.Target is UnityEngine.Object);
            OwnerName = !hasOwner ? "<none>" : IsOwnerCollected ? $"<owner GC collected: {ownerTypeName}>" : IsOwnerDestroyed ? $"<destroyed Unity owner: {ownerTypeName}>" : owner.ToString();
            IsInactiveBehaviour = listener.Target is UnityEngine.Behaviour behaviour && !IsDestroyed && !behaviour.isActiveAndEnabled;
            TargetName = listener.Target == null ? "<static>" : IsDestroyed ? "<destroyed Unity object>" : listener.Target.ToString();
            if (IsDestroyed)
            {
                HasIssue = true;
                IssueDescription = "监听目标已经被 Unity 销毁，但事件系统里仍保留订阅。";
                SuggestedFix = "在 OnDisable 或 OnDestroy 中调用 RemoveEventListener。";
            }
            else if (IsOwnerDestroyed || IsOwnerCollected)
            {
                HasIssue = true;
                IssueDescription = "监听 owner 已失效，说明订阅生命周期已经结束。";
                SuggestedFix = "检查 owner 对象释放时是否调用 Dispose 或 RemoveEventListener。";
            }
            else if (IsUnownedManagedTarget)
            {
                HasIssue = true;
                IssueDescription = "未传 owner 的普通 C# 对象监听器会被事件系统强引用，无法自动判断是否已经该释放。";
                SuggestedFix = "改用 owner 重载，或保证在 Dispose 中成对移除。";
            }
            else if (IsInactiveBehaviour)
            {
                HasIssue = true;
                IssueDescription = "监听目标是未启用的 Behaviour，可能仍会收到事件。";
                SuggestedFix = "确认禁用状态下是否仍应监听；否则在 OnDisable 中移除。";
            }
            else
            {
                HasIssue = false;
                IssueDescription = string.Empty;
                SuggestedFix = string.Empty;
            }
            RegisteredFrame = registeredFrame;
            RegisteredUtcTicks = registeredUtcTicks;
            RegistrationStackTrace = registrationStackTrace;
        }
    }

    public readonly struct DispatchDebugInfo
    {
        public readonly string EventName;
        public readonly string Signature;
        public readonly int ListenerCount;
        public readonly long UtcTicks;
        public readonly double DurationMilliseconds;
        public readonly string ParameterSummary;
        public readonly string SourceStackTrace;
        internal DispatchDebugInfo(string eventName, string signature, int listenerCount, long utcTicks, double durationMilliseconds, string parameterSummary, string sourceStackTrace)
        { EventName = eventName; Signature = signature; ListenerCount = listenerCount; UtcTicks = utcTicks; DurationMilliseconds = durationMilliseconds; ParameterSummary = parameterSummary; SourceStackTrace = sourceStackTrace; }
    }

    public readonly struct ListenerExceptionDebugInfo
    {
        public readonly string EventName, Listener, ExceptionType, Message, StackTrace;
        public readonly long UtcTicks;
        internal ListenerExceptionDebugInfo(string eventName, Delegate listener, Exception exception) { EventName = eventName; Listener = $"{listener.Method.DeclaringType?.FullName}.{listener.Method.Name}"; ExceptionType = exception.GetType().FullName; Message = exception.Message; StackTrace = exception.StackTrace; UtcTicks = DateTime.UtcNow.Ticks; }
    }

    public readonly struct CleanupDebugInfo
    {
        public readonly string EventName, Listener, Reason;
        public readonly long UtcTicks;
        internal CleanupDebugInfo(string eventName, Delegate listener, string reason) { EventName = eventName; Listener = $"{listener.Method.DeclaringType?.FullName}.{listener.Method.Name}"; Reason = reason; UtcTicks = DateTime.UtcNow.Ticks; }
    }

    private void RecordDispatch(string eventName, string signature, int listenerCount, double durationMilliseconds, long utcTicks, string parameterSummary, string sourceStackTrace)
    {
        if (!DiagnosticsEnabled) return;
        if (_dispatchHistory == null) _dispatchHistory = new DispatchDebugInfo[200];
        _dispatchHistory[_nextDispatchIndex] = new DispatchDebugInfo(eventName, signature, listenerCount, utcTicks, durationMilliseconds, parameterSummary, sourceStackTrace);
        _nextDispatchIndex = (_nextDispatchIndex + 1) % _dispatchHistory.Length;
        if (_dispatchHistoryCount < _dispatchHistory.Length) _dispatchHistoryCount++;
    }

    private void RecordListenerException(string eventName, Delegate listener, Exception exception)
    {
        if (!DiagnosticsEnabled) return;
        if (_exceptionHistory == null) _exceptionHistory = new ListenerExceptionDebugInfo[100];
        _exceptionHistory[_nextExceptionIndex] = new ListenerExceptionDebugInfo(eventName, listener, exception);
        _nextExceptionIndex = (_nextExceptionIndex + 1) % _exceptionHistory.Length;
        if (_exceptionHistoryCount < _exceptionHistory.Length) _exceptionHistoryCount++;
    }

    private void RecordCleanup(string eventName, Delegate listener, string reason)
    {
        if (!DiagnosticsEnabled) return;
        if (_cleanupHistory == null) _cleanupHistory = new CleanupDebugInfo[100];
        _cleanupHistory[_nextCleanupIndex] = new CleanupDebugInfo(eventName, listener, reason);
        _nextCleanupIndex = (_nextCleanupIndex + 1) % _cleanupHistory.Length;
        if (_cleanupHistoryCount < _cleanupHistory.Length) _cleanupHistoryCount++;
    }
#endif

    private abstract class EventBucket
    {
        private readonly EventSystem _owner;
        private readonly string _eventName;
        public readonly string Signature;
        protected readonly List<Delegate> Listeners = new List<Delegate>(4);
        private readonly Dictionary<Delegate, ListenerRecord> _listenerRecords = new Dictionary<Delegate, ListenerRecord>(4);
        private readonly List<PendingOperation> _pendingOperations = new List<PendingOperation>(2);
        private HashSet<Delegate> _warnedDestroyedListeners;
        private int _dispatchDepth;
        private long _dispatchStartTimestamp;
        private int _dispatchCount;
        private long _lastDispatchUtcTicks;
        private double _lastDispatchMilliseconds;
        private double _totalDispatchMilliseconds;
        private double _maxDispatchMilliseconds;
        private int _exceptionCount;
        private string _parameterSummary;
        private string _dispatchSourceStackTrace;

        protected EventBucket(EventSystem owner, string eventName, string signature) { _owner = owner; _eventName = eventName; Signature = signature; }
        public void Add(Delegate listener, object owner = null)
        {
            if (listener == null) return;
            if (_dispatchDepth > 0) { _pendingOperations.Add(new PendingOperation(listener, true, owner)); return; }
            AddNow(listener, owner);
        }
        public void Remove(Delegate listener)
        {
            if (listener == null) return;
            if (_dispatchDepth > 0) { _pendingOperations.Add(new PendingOperation(listener, false)); return; }
            RemoveNow(listener);
        }
        protected void BeginDispatch()
        {
            if (_dispatchDepth == 0) _dispatchStartTimestamp = Stopwatch.GetTimestamp();
#if UNITY_EDITOR
            if (_dispatchDepth == 0 && _owner.DiagnosticsEnabled && _owner.CaptureDispatchStackTrace) _dispatchSourceStackTrace = new StackTrace(3, true).ToString();
#endif
            _dispatchDepth++;
        }
#if UNITY_EDITOR
        protected bool IsDiagnosticsEnabled => _owner.DiagnosticsEnabled;
        protected void SetParameterSummary(string parameterSummary) { if (_owner.DiagnosticsEnabled) _parameterSummary = parameterSummary; }
#endif
        protected void EndDispatch()
        {
            _dispatchDepth--;
            if (_dispatchDepth != 0) return;
            for (var i = 0; i < _pendingOperations.Count; i++)
            {
                var operation = _pendingOperations[i];
                if (operation.IsAdd) AddNow(operation.Listener, operation.Owner); else RemoveNow(operation.Listener);
            }
            _pendingOperations.Clear();
            PurgeDestroyedListeners();
            CompleteDispatch();
        }
        public int PurgeDestroyedListeners()
        {
            if (_dispatchDepth > 0) return 0;
            var removed = 0;
            for (var i = Listeners.Count - 1; i >= 0; i--)
            {
                if (!IsDestroyedUnityTarget(Listeners[i])) continue;
                var listener = Listeners[i];
#if UNITY_EDITOR
                _owner.RecordCleanup(_eventName, listener, GetInvalidReason(listener));
#endif
                _warnedDestroyedListeners?.Remove(listener);
                _listenerRecords.Remove(listener);
                Listeners.RemoveAt(i); removed++;
            }
            return removed;
        }
#if UNITY_EDITOR
        public EventDebugInfo GetDebugInfo(string name)
        {
            var invalidListenerCount = 0;
            var unownedManagedTargetCount = 0;
            for (var i = 0; i < Listeners.Count; i++)
            {
                if (IsDestroyedUnityTarget(Listeners[i])) invalidListenerCount++;
                var record = _listenerRecords[Listeners[i]];
                if (!record.HasOwner && Listeners[i].Target != null && !(Listeners[i].Target is UnityEngine.Object)) unownedManagedTargetCount++;
            }
            return new EventDebugInfo(name, Signature, Listeners.Count, invalidListenerCount, unownedManagedTargetCount, _dispatchCount, _lastDispatchUtcTicks, _lastDispatchMilliseconds, _totalDispatchMilliseconds, _maxDispatchMilliseconds, _exceptionCount);
        }
        public void AppendListenerDebugInfo(List<EventListenerDebugInfo> results)
        {
            for (var i = 0; i < Listeners.Count; i++)
            {
                var listener = Listeners[i];
                var record = _listenerRecords[listener];
                var owner = record.OwnerReference == null ? null : record.OwnerReference.Target;
                results.Add(new EventListenerDebugInfo(_eventName, listener, record.RegisteredFrame, record.RegisteredUtcTicks, record.RegistrationStackTrace, owner, record.HasOwner, record.OwnerTypeName));
            }
        }
#endif
        private void AddNow(Delegate listener, object owner)
        {
            if (Listeners.Contains(listener)) return;
            Listeners.Add(listener);
            _listenerRecords.Add(listener, new ListenerRecord(owner));
        }
        private void RemoveNow(Delegate listener)
        {
            if (Listeners.Remove(listener))
            {
                _listenerRecords.Remove(listener);
                _warnedDestroyedListeners?.Remove(listener);
            }
        }
        private void CompleteDispatch()
        {
            _dispatchCount++;
            _lastDispatchUtcTicks = DateTime.UtcNow.Ticks;
            _lastDispatchMilliseconds = (Stopwatch.GetTimestamp() - _dispatchStartTimestamp) * 1000d / Stopwatch.Frequency;
            _totalDispatchMilliseconds += _lastDispatchMilliseconds;
            if (_lastDispatchMilliseconds > _maxDispatchMilliseconds) _maxDispatchMilliseconds = _lastDispatchMilliseconds;
#if UNITY_EDITOR
            _owner.RecordDispatch(_eventName, Signature, Listeners.Count, _lastDispatchMilliseconds, _lastDispatchUtcTicks, _parameterSummary, _dispatchSourceStackTrace);
            _parameterSummary = null;
            _dispatchSourceStackTrace = null;
#endif
        }
        protected void RecordListenerException(Delegate listener, Exception exception)
        {
            _exceptionCount++;
#if UNITY_EDITOR
            _owner.RecordListenerException(_eventName, listener, exception);
#endif
            UnityEngine.Debug.LogException(exception);
        }
        private string GetInvalidReason(Delegate listener)
        {
            var record = _listenerRecords[listener];
            var owner = record.OwnerReference == null ? null : record.OwnerReference.Target;
            if (listener.Target is UnityEngine.Object target && target == null) return "listener Unity target destroyed";
            if (record.HasOwner && owner == null) return "owner GC collected";
            if (owner is UnityEngine.Object unityOwner && unityOwner == null) return "owner Unity object destroyed";
            return "invalid listener";
        }
        protected bool IsDestroyedUnityTarget(Delegate listener)
        {
            var targetDestroyed = listener.Target is UnityEngine.Object unityTarget && unityTarget == null;
            var record = _listenerRecords[listener];
            var owner = record.OwnerReference == null ? null : record.OwnerReference.Target;
            var ownerCollected = record.HasOwner && owner == null;
            var ownerDestroyed = owner is UnityEngine.Object unityOwner && unityOwner == null;
            if (!targetDestroyed && !ownerCollected && !ownerDestroyed) return false;
#if UNITY_EDITOR
            if (_dispatchDepth > 0 && _owner.WarnOnDestroyedListener)
            {
                if (_warnedDestroyedListeners == null) _warnedDestroyedListeners = new HashSet<Delegate>();
                if (_warnedDestroyedListeners.Add(listener))
                {
                    var reason = ownerCollected ? "subscription owner was GC collected" : ownerDestroyed ? "subscription Unity owner was destroyed" : "listener target was destroyed";
                    UnityEngine.Debug.LogWarning($"[EventSystem] {reason}: {_eventName} -> {listener.Method.DeclaringType?.FullName}.{listener.Method.Name}");
                }
            }
#endif
            return true;
        }
        internal readonly struct ListenerRecord
        {
            public readonly int RegisteredFrame;
            public readonly long RegisteredUtcTicks;
            public readonly string RegistrationStackTrace;
            public readonly WeakReference OwnerReference;
            public readonly bool HasOwner;
            public readonly string OwnerTypeName;
            public ListenerRecord(object owner)
            {
                HasOwner = owner != null;
                OwnerReference = owner == null ? null : new WeakReference(owner);
                OwnerTypeName = owner == null ? string.Empty : owner.GetType().FullName;
                RegisteredFrame = UnityEngine.Time.frameCount;
                RegisteredUtcTicks = DateTime.UtcNow.Ticks;
#if UNITY_EDITOR
                RegistrationStackTrace = EventSystem.Instance.CaptureRegistrationStackTrace ? new StackTrace(4, true).ToString() : null;
#else
                RegistrationStackTrace = null;
#endif
            }
        }
        private readonly struct PendingOperation
        {
            public readonly Delegate Listener;
            public readonly bool IsAdd;
            public readonly object Owner;
            public PendingOperation(Delegate listener, bool isAdd, object owner = null) { Listener = listener; IsAdd = isAdd; Owner = owner; }
        }
    }

    private sealed partial class EventBucket<TDelegate> : EventBucket where TDelegate : Delegate
    {
        public EventBucket(EventSystem owner, string eventName, string signature) : base(owner, eventName, signature) { }
        public void Invoke()
        {
#if UNITY_EDITOR
            if (IsDiagnosticsEnabled) SetParameterSummary("()");
#endif
            BeginDispatch();
            try
            {
                for (var i = 0; i < Listeners.Count; i++)
                {
                    var listener = Listeners[i];
                    if (IsDestroyedUnityTarget(listener)) continue;
                    try { ((Action)listener)(); }
                    catch (Exception exception) { RecordListenerException(listener, exception); }
                }
            }
            finally { EndDispatch(); }
        }
        public void Invoke<T>(T value)
        {
#if UNITY_EDITOR
            if (IsDiagnosticsEnabled) SetParameterSummary(string.Join(", ", new object[] { value }));
#endif
            BeginDispatch();
            try
            {
                for (var i = 0; i < Listeners.Count; i++)
                {
                    var listener = Listeners[i];
                    if (IsDestroyedUnityTarget(listener)) continue;
                    try { ((Action<T>)listener)(value); }
                    catch (Exception exception) { RecordListenerException(listener, exception); }
                }
            }
            finally { EndDispatch(); }
        }
        public void Invoke<T1, T2>(T1 value1, T2 value2)
        {
#if UNITY_EDITOR
            if (IsDiagnosticsEnabled) SetParameterSummary(string.Join(", ", new object[] { value1, value2 }));
#endif
            BeginDispatch();
            try
            {
                for (var i = 0; i < Listeners.Count; i++)
                {
                    var listener = Listeners[i];
                    if (IsDestroyedUnityTarget(listener)) continue;
                    try { ((Action<T1, T2>)listener)(value1, value2); }
                    catch (Exception exception) { RecordListenerException(listener, exception); }
                }
            }
            finally { EndDispatch(); }
        }
        public void Invoke<T1, T2, T3>(T1 value1, T2 value2, T3 value3)
        {
#if UNITY_EDITOR
            if (IsDiagnosticsEnabled) SetParameterSummary(string.Join(", ", new object[] { value1, value2, value3 }));
#endif
            BeginDispatch();
            try
            {
                for (var i = 0; i < Listeners.Count; i++)
                {
                    var listener = Listeners[i];
                    if (IsDestroyedUnityTarget(listener)) continue;
                    try { ((Action<T1, T2, T3>)listener)(value1, value2, value3); }
                    catch (Exception exception) { RecordListenerException(listener, exception); }
                }
            }
            finally { EndDispatch(); }
        }
        public void Invoke<T1, T2, T3, T4>(T1 value1, T2 value2, T3 value3, T4 value4)
        {
#if UNITY_EDITOR
            if (IsDiagnosticsEnabled) SetParameterSummary(string.Join(", ", new object[] { value1, value2, value3, value4 }));
#endif
            BeginDispatch();
            try
            {
                for (var i = 0; i < Listeners.Count; i++)
                {
                    var listener = Listeners[i];
                    if (IsDestroyedUnityTarget(listener)) continue;
                    try { ((Action<T1, T2, T3, T4>)listener)(value1, value2, value3, value4); }
                    catch (Exception exception) { RecordListenerException(listener, exception); }
                }
            }
            finally { EndDispatch(); }
        }
    }
}
}

