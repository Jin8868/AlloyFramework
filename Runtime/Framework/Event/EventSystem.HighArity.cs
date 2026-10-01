using System;

namespace AlloyFramework
{

public sealed partial class EventSystem
{
    public void AddEventListener<T1, T2, T3, T4, T5>(string eventName, Action<T1, T2, T3, T4, T5> listener) => GetOrCreate<Action<T1, T2, T3, T4, T5>>(eventName).Add(listener);
    public void RemoveEventListener<T1, T2, T3, T4, T5>(string eventName, Action<T1, T2, T3, T4, T5> listener) => Remove(eventName, listener);
    public void SendEvent<T1, T2, T3, T4, T5>(string eventName, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5) { if (TryGet<Action<T1, T2, T3, T4, T5>>(eventName, out var bucket)) bucket.Invoke(value1, value2, value3, value4, value5); }

    public void AddEventListener<T1, T2, T3, T4, T5, T6>(string eventName, Action<T1, T2, T3, T4, T5, T6> listener) => GetOrCreate<Action<T1, T2, T3, T4, T5, T6>>(eventName).Add(listener);
    public void RemoveEventListener<T1, T2, T3, T4, T5, T6>(string eventName, Action<T1, T2, T3, T4, T5, T6> listener) => Remove(eventName, listener);
    public void SendEvent<T1, T2, T3, T4, T5, T6>(string eventName, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6) { if (TryGet<Action<T1, T2, T3, T4, T5, T6>>(eventName, out var bucket)) bucket.Invoke(value1, value2, value3, value4, value5, value6); }

    public void AddEventListener<T1, T2, T3, T4, T5, T6, T7>(string eventName, Action<T1, T2, T3, T4, T5, T6, T7> listener) => GetOrCreate<Action<T1, T2, T3, T4, T5, T6, T7>>(eventName).Add(listener);
    public void RemoveEventListener<T1, T2, T3, T4, T5, T6, T7>(string eventName, Action<T1, T2, T3, T4, T5, T6, T7> listener) => Remove(eventName, listener);
    public void SendEvent<T1, T2, T3, T4, T5, T6, T7>(string eventName, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7) { if (TryGet<Action<T1, T2, T3, T4, T5, T6, T7>>(eventName, out var bucket)) bucket.Invoke(value1, value2, value3, value4, value5, value6, value7); }

    public void AddEventListener<T1, T2, T3, T4, T5, T6, T7, T8>(string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8> listener) => GetOrCreate<Action<T1, T2, T3, T4, T5, T6, T7, T8>>(eventName).Add(listener);
    public void RemoveEventListener<T1, T2, T3, T4, T5, T6, T7, T8>(string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8> listener) => Remove(eventName, listener);
    public void SendEvent<T1, T2, T3, T4, T5, T6, T7, T8>(string eventName, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8) { if (TryGet<Action<T1, T2, T3, T4, T5, T6, T7, T8>>(eventName, out var bucket)) bucket.Invoke(value1, value2, value3, value4, value5, value6, value7, value8); }

    public void AddEventListener<T1, T2, T3, T4, T5, T6, T7, T8, T9>(string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> listener) => GetOrCreate<Action<T1, T2, T3, T4, T5, T6, T7, T8, T9>>(eventName).Add(listener);
    public void RemoveEventListener<T1, T2, T3, T4, T5, T6, T7, T8, T9>(string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> listener) => Remove(eventName, listener);
    public void SendEvent<T1, T2, T3, T4, T5, T6, T7, T8, T9>(string eventName, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9) { if (TryGet<Action<T1, T2, T3, T4, T5, T6, T7, T8, T9>>(eventName, out var bucket)) bucket.Invoke(value1, value2, value3, value4, value5, value6, value7, value8, value9); }

    public void AddEventListener<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> listener) => GetOrCreate<Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>>(eventName).Add(listener);
    public void RemoveEventListener<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> listener) => Remove(eventName, listener);
    public void SendEvent<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(string eventName, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10) { if (TryGet<Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>>(eventName, out var bucket)) bucket.Invoke(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10); }

    public void AddEventListener<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> listener) => GetOrCreate<Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>>(eventName).Add(listener);
    public void RemoveEventListener<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> listener) => Remove(eventName, listener);
    public void SendEvent<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(string eventName, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11) { if (TryGet<Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>>(eventName, out var bucket)) bucket.Invoke(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11); }

    public void AddEventListener<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> listener) => GetOrCreate<Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>>(eventName).Add(listener);
    public void RemoveEventListener<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> listener) => Remove(eventName, listener);
    public void SendEvent<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(string eventName, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11, T12 value12) { if (TryGet<Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>>(eventName, out var bucket)) bucket.Invoke(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12); }

    public void AddEventListener<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> listener) => GetOrCreate<Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>>(eventName).Add(listener);
    public void RemoveEventListener<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> listener) => Remove(eventName, listener);
    public void SendEvent<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(string eventName, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11, T12 value12, T13 value13) { if (TryGet<Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>>(eventName, out var bucket)) bucket.Invoke(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13); }

    public void AddEventListener<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> listener) => GetOrCreate<Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>>(eventName).Add(listener);
    public void RemoveEventListener<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> listener) => Remove(eventName, listener);
    public void SendEvent<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(string eventName, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11, T12 value12, T13 value13, T14 value14) { if (TryGet<Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>>(eventName, out var bucket)) bucket.Invoke(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14); }

    public void AddEventListener<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> listener) => GetOrCreate<Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>>(eventName).Add(listener);
    public void RemoveEventListener<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> listener) => Remove(eventName, listener);
    public void SendEvent<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(string eventName, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11, T12 value12, T13 value13, T14 value14, T15 value15) { if (TryGet<Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>>(eventName, out var bucket)) bucket.Invoke(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15); }

    public void AddEventListener<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16> listener) => GetOrCreate<Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>>(eventName).Add(listener);
    public void RemoveEventListener<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16> listener) => Remove(eventName, listener);
    public void SendEvent<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(string eventName, T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11, T12 value12, T13 value13, T14 value14, T15 value15, T16 value16) { if (TryGet<Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>>(eventName, out var bucket)) bucket.Invoke(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15, value16); }

    private sealed partial class EventBucket<TDelegate> where TDelegate : Delegate
    {
        public void Invoke<T1, T2, T3, T4, T5>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5)
        {
#if UNITY_EDITOR
            if (IsDiagnosticsEnabled) SetParameterSummary(string.Join(", ", new object[] { value1, value2, value3, value4, value5 }));
#endif
            BeginDispatch();
            try
            {
                for (var i = 0; i < Listeners.Count; i++)
                {
                    var listener = Listeners[i];
                    if (IsDestroyedUnityTarget(listener)) continue;
                    try { ((Action<T1, T2, T3, T4, T5>)listener)(value1, value2, value3, value4, value5); }
                    catch (Exception exception) { RecordListenerException(listener, exception); }
                }
            }
            finally { EndDispatch(); }
        }

        public void Invoke<T1, T2, T3, T4, T5, T6>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6)
        {
#if UNITY_EDITOR
            if (IsDiagnosticsEnabled) SetParameterSummary(string.Join(", ", new object[] { value1, value2, value3, value4, value5, value6 }));
#endif
            BeginDispatch();
            try
            {
                for (var i = 0; i < Listeners.Count; i++)
                {
                    var listener = Listeners[i];
                    if (IsDestroyedUnityTarget(listener)) continue;
                    try { ((Action<T1, T2, T3, T4, T5, T6>)listener)(value1, value2, value3, value4, value5, value6); }
                    catch (Exception exception) { RecordListenerException(listener, exception); }
                }
            }
            finally { EndDispatch(); }
        }

        public void Invoke<T1, T2, T3, T4, T5, T6, T7>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7)
        {
#if UNITY_EDITOR
            if (IsDiagnosticsEnabled) SetParameterSummary(string.Join(", ", new object[] { value1, value2, value3, value4, value5, value6, value7 }));
#endif
            BeginDispatch();
            try
            {
                for (var i = 0; i < Listeners.Count; i++)
                {
                    var listener = Listeners[i];
                    if (IsDestroyedUnityTarget(listener)) continue;
                    try { ((Action<T1, T2, T3, T4, T5, T6, T7>)listener)(value1, value2, value3, value4, value5, value6, value7); }
                    catch (Exception exception) { RecordListenerException(listener, exception); }
                }
            }
            finally { EndDispatch(); }
        }

        public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8)
        {
#if UNITY_EDITOR
            if (IsDiagnosticsEnabled) SetParameterSummary(string.Join(", ", new object[] { value1, value2, value3, value4, value5, value6, value7, value8 }));
#endif
            BeginDispatch();
            try
            {
                for (var i = 0; i < Listeners.Count; i++)
                {
                    var listener = Listeners[i];
                    if (IsDestroyedUnityTarget(listener)) continue;
                    try { ((Action<T1, T2, T3, T4, T5, T6, T7, T8>)listener)(value1, value2, value3, value4, value5, value6, value7, value8); }
                    catch (Exception exception) { RecordListenerException(listener, exception); }
                }
            }
            finally { EndDispatch(); }
        }

        public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9)
        {
#if UNITY_EDITOR
            if (IsDiagnosticsEnabled) SetParameterSummary(string.Join(", ", new object[] { value1, value2, value3, value4, value5, value6, value7, value8, value9 }));
#endif
            BeginDispatch();
            try
            {
                for (var i = 0; i < Listeners.Count; i++)
                {
                    var listener = Listeners[i];
                    if (IsDestroyedUnityTarget(listener)) continue;
                    try { ((Action<T1, T2, T3, T4, T5, T6, T7, T8, T9>)listener)(value1, value2, value3, value4, value5, value6, value7, value8, value9); }
                    catch (Exception exception) { RecordListenerException(listener, exception); }
                }
            }
            finally { EndDispatch(); }
        }

        public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10)
        {
#if UNITY_EDITOR
            if (IsDiagnosticsEnabled) SetParameterSummary(string.Join(", ", new object[] { value1, value2, value3, value4, value5, value6, value7, value8, value9, value10 }));
#endif
            BeginDispatch();
            try
            {
                for (var i = 0; i < Listeners.Count; i++)
                {
                    var listener = Listeners[i];
                    if (IsDestroyedUnityTarget(listener)) continue;
                    try { ((Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>)listener)(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10); }
                    catch (Exception exception) { RecordListenerException(listener, exception); }
                }
            }
            finally { EndDispatch(); }
        }

        public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11)
        {
#if UNITY_EDITOR
            if (IsDiagnosticsEnabled) SetParameterSummary(string.Join(", ", new object[] { value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11 }));
#endif
            BeginDispatch();
            try
            {
                for (var i = 0; i < Listeners.Count; i++)
                {
                    var listener = Listeners[i];
                    if (IsDestroyedUnityTarget(listener)) continue;
                    try { ((Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>)listener)(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11); }
                    catch (Exception exception) { RecordListenerException(listener, exception); }
                }
            }
            finally { EndDispatch(); }
        }

        public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11, T12 value12)
        {
#if UNITY_EDITOR
            if (IsDiagnosticsEnabled) SetParameterSummary(string.Join(", ", new object[] { value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12 }));
#endif
            BeginDispatch();
            try
            {
                for (var i = 0; i < Listeners.Count; i++)
                {
                    var listener = Listeners[i];
                    if (IsDestroyedUnityTarget(listener)) continue;
                    try { ((Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>)listener)(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12); }
                    catch (Exception exception) { RecordListenerException(listener, exception); }
                }
            }
            finally { EndDispatch(); }
        }

        public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11, T12 value12, T13 value13)
        {
#if UNITY_EDITOR
            if (IsDiagnosticsEnabled) SetParameterSummary(string.Join(", ", new object[] { value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13 }));
#endif
            BeginDispatch();
            try
            {
                for (var i = 0; i < Listeners.Count; i++)
                {
                    var listener = Listeners[i];
                    if (IsDestroyedUnityTarget(listener)) continue;
                    try { ((Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>)listener)(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13); }
                    catch (Exception exception) { RecordListenerException(listener, exception); }
                }
            }
            finally { EndDispatch(); }
        }

        public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11, T12 value12, T13 value13, T14 value14)
        {
#if UNITY_EDITOR
            if (IsDiagnosticsEnabled) SetParameterSummary(string.Join(", ", new object[] { value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14 }));
#endif
            BeginDispatch();
            try
            {
                for (var i = 0; i < Listeners.Count; i++)
                {
                    var listener = Listeners[i];
                    if (IsDestroyedUnityTarget(listener)) continue;
                    try { ((Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>)listener)(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14); }
                    catch (Exception exception) { RecordListenerException(listener, exception); }
                }
            }
            finally { EndDispatch(); }
        }

        public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11, T12 value12, T13 value13, T14 value14, T15 value15)
        {
#if UNITY_EDITOR
            if (IsDiagnosticsEnabled) SetParameterSummary(string.Join(", ", new object[] { value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15 }));
#endif
            BeginDispatch();
            try
            {
                for (var i = 0; i < Listeners.Count; i++)
                {
                    var listener = Listeners[i];
                    if (IsDestroyedUnityTarget(listener)) continue;
                    try { ((Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>)listener)(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15); }
                    catch (Exception exception) { RecordListenerException(listener, exception); }
                }
            }
            finally { EndDispatch(); }
        }

        public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8, T9 value9, T10 value10, T11 value11, T12 value12, T13 value13, T14 value14, T15 value15, T16 value16)
        {
#if UNITY_EDITOR
            if (IsDiagnosticsEnabled) SetParameterSummary(string.Join(", ", new object[] { value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15, value16 }));
#endif
            BeginDispatch();
            try
            {
                for (var i = 0; i < Listeners.Count; i++)
                {
                    var listener = Listeners[i];
                    if (IsDestroyedUnityTarget(listener)) continue;
                    try { ((Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>)listener)(value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15, value16); }
                    catch (Exception exception) { RecordListenerException(listener, exception); }
                }
            }
            finally { EndDispatch(); }
        }
    }
}
}

