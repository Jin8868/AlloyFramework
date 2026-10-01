using System;

namespace AlloyFramework
{

public sealed partial class EventSystem
{
    public void AddEventListener<T1, T2, T3, T4, T5>(object owner, string eventName, Action<T1, T2, T3, T4, T5> listener) => GetOrCreate<Action<T1, T2, T3, T4, T5>>(eventName).Add(listener, owner);
    public void AddEventListener<T1, T2, T3, T4, T5, T6>(object owner, string eventName, Action<T1, T2, T3, T4, T5, T6> listener) => GetOrCreate<Action<T1, T2, T3, T4, T5, T6>>(eventName).Add(listener, owner);
    public void AddEventListener<T1, T2, T3, T4, T5, T6, T7>(object owner, string eventName, Action<T1, T2, T3, T4, T5, T6, T7> listener) => GetOrCreate<Action<T1, T2, T3, T4, T5, T6, T7>>(eventName).Add(listener, owner);
    public void AddEventListener<T1, T2, T3, T4, T5, T6, T7, T8>(object owner, string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8> listener) => GetOrCreate<Action<T1, T2, T3, T4, T5, T6, T7, T8>>(eventName).Add(listener, owner);
    public void AddEventListener<T1, T2, T3, T4, T5, T6, T7, T8, T9>(object owner, string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> listener) => GetOrCreate<Action<T1, T2, T3, T4, T5, T6, T7, T8, T9>>(eventName).Add(listener, owner);
    public void AddEventListener<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(object owner, string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> listener) => GetOrCreate<Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>>(eventName).Add(listener, owner);
    public void AddEventListener<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(object owner, string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> listener) => GetOrCreate<Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>>(eventName).Add(listener, owner);
    public void AddEventListener<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(object owner, string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> listener) => GetOrCreate<Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>>(eventName).Add(listener, owner);
    public void AddEventListener<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(object owner, string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> listener) => GetOrCreate<Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>>(eventName).Add(listener, owner);
    public void AddEventListener<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(object owner, string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> listener) => GetOrCreate<Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>>(eventName).Add(listener, owner);
    public void AddEventListener<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(object owner, string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> listener) => GetOrCreate<Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>>(eventName).Add(listener, owner);
    public void AddEventListener<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(object owner, string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16> listener) => GetOrCreate<Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>>(eventName).Add(listener, owner);
}
}

