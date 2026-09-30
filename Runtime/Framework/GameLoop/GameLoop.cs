using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlloyFramework
{
    public static class GameLoop
    {
        private const string FacadeName = "[AlloyFramework] MonoFacade";

        private static readonly Registry<IUpdateable> UpdateRegistry = new Registry<IUpdateable>();
        private static readonly Registry<ILateUpdateable> LateUpdateRegistry = new Registry<ILateUpdateable>();
        private static readonly Registry<IFixedUpdateable> FixedUpdateRegistry = new Registry<IFixedUpdateable>();

        private static readonly Action<IUpdateable, float, float> UpdateInvoker = InvokeUpdate;
        private static readonly Action<ILateUpdateable, float, float> LateUpdateInvoker = InvokeLateUpdate;
        private static readonly Action<IFixedUpdateable, float, float> FixedUpdateInvoker = InvokeFixedUpdate;

        private static MonoFacade _facade;
        private static bool _isQuitting;
        private static bool _quittingHookRegistered;

        public static bool IsInitialized => _facade != null;

        public static void Initialize()
        {
            if (_facade != null || _isQuitting)
            {
                return;
            }

            RegisterQuittingHook();

            var facadeObject = new GameObject(FacadeName);
            facadeObject.AddComponent<MonoFacade>();
        }

        public static bool Register(object target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var registered = false;

            if (target is IUpdateable updateable)
            {
                registered |= UpdateRegistry.Register(updateable);
            }

            if (target is ILateUpdateable lateUpdateable)
            {
                registered |= LateUpdateRegistry.Register(lateUpdateable);
            }

            if (target is IFixedUpdateable fixedUpdateable)
            {
                registered |= FixedUpdateRegistry.Register(fixedUpdateable);
            }

            return registered;
        }

        public static bool Unregister(object target)
        {
            if (target == null)
            {
                return false;
            }

            var unregistered = false;

            if (target is IUpdateable updateable)
            {
                unregistered |= UpdateRegistry.Unregister(updateable);
            }

            if (target is ILateUpdateable lateUpdateable)
            {
                unregistered |= LateUpdateRegistry.Unregister(lateUpdateable);
            }

            if (target is IFixedUpdateable fixedUpdateable)
            {
                unregistered |= FixedUpdateRegistry.Unregister(fixedUpdateable);
            }

            return unregistered;
        }

        public static void Shutdown()
        {
            ClearRegistrations();
            UnregisterQuittingHook();

            if (_facade == null)
            {
                return;
            }

            var facadeObject = _facade.gameObject;
            _facade = null;

            if (!_isQuitting)
            {
                UnityEngine.Object.Destroy(facadeObject);
            }
        }

        internal static bool AttachFacade(MonoFacade facade)
        {
            if (_isQuitting)
            {
                return false;
            }

            if (_facade != null && _facade != facade)
            {
                return false;
            }

            _facade = facade;
            RegisterQuittingHook();
            return true;
        }

        internal static void DetachFacade(MonoFacade facade)
        {
            if (_facade == facade)
            {
                _facade = null;
            }
        }

        internal static void DispatchUpdate(float deltaTime, float unscaledDeltaTime)
        {
            UpdateRegistry.Dispatch(deltaTime, unscaledDeltaTime, UpdateInvoker);
        }

        internal static void DispatchLateUpdate(float deltaTime, float unscaledDeltaTime)
        {
            LateUpdateRegistry.Dispatch(deltaTime, unscaledDeltaTime, LateUpdateInvoker);
        }

        internal static void DispatchFixedUpdate(float fixedDeltaTime)
        {
            FixedUpdateRegistry.Dispatch(fixedDeltaTime, fixedDeltaTime, FixedUpdateInvoker);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Application.quitting -= HandleApplicationQuitting;
            _quittingHookRegistered = false;
            _isQuitting = false;
            _facade = null;
            ClearRegistrations();
        }

        private static void RegisterQuittingHook()
        {
            if (_quittingHookRegistered)
            {
                return;
            }

            Application.quitting += HandleApplicationQuitting;
            _quittingHookRegistered = true;
        }

        private static void UnregisterQuittingHook()
        {
            if (!_quittingHookRegistered)
            {
                return;
            }

            Application.quitting -= HandleApplicationQuitting;
            _quittingHookRegistered = false;
        }

        private static void HandleApplicationQuitting()
        {
            _isQuitting = true;
            ClearRegistrations();
            UnregisterQuittingHook();
            _facade = null;
        }

        private static void ClearRegistrations()
        {
            UpdateRegistry.Clear();
            LateUpdateRegistry.Clear();
            FixedUpdateRegistry.Clear();
        }

        private static void InvokeUpdate(IUpdateable updateable, float deltaTime, float unscaledDeltaTime)
        {
            updateable.OnUpdate(deltaTime, unscaledDeltaTime);
        }

        private static void InvokeLateUpdate(ILateUpdateable updateable, float deltaTime, float unscaledDeltaTime)
        {
            updateable.OnLateUpdate(deltaTime, unscaledDeltaTime);
        }

        private static void InvokeFixedUpdate(IFixedUpdateable updateable, float fixedDeltaTime, float unused)
        {
            updateable.OnFixedUpdate(fixedDeltaTime);
        }

        private sealed class Registry<T> where T : class
        {
            private readonly List<T> _items = new List<T>();
            private readonly HashSet<T> _members = new HashSet<T>();
            private readonly HashSet<T> _pendingAdds = new HashSet<T>();
            private readonly HashSet<T> _pendingRemoves = new HashSet<T>();

            private bool _isDispatching;

            public bool Register(T item)
            {
                if (_isDispatching)
                {
                    if (_pendingRemoves.Remove(item))
                    {
                        return true;
                    }

                    if (_members.Contains(item))
                    {
                        return false;
                    }

                    return _pendingAdds.Add(item);
                }

                if (!_members.Add(item))
                {
                    return false;
                }

                _items.Add(item);
                return true;
            }

            public bool Unregister(T item)
            {
                if (_isDispatching)
                {
                    if (_pendingAdds.Remove(item))
                    {
                        return true;
                    }

                    return _members.Contains(item) && _pendingRemoves.Add(item);
                }

                if (!_members.Remove(item))
                {
                    return false;
                }

                _items.Remove(item);
                return true;
            }

            public void Dispatch(float deltaTime, float unscaledDeltaTime, Action<T, float, float> invoker)
            {
                _isDispatching = true;

                try
                {
                    for (var index = 0; index < _items.Count; index++)
                    {
                        var item = _items[index];
                        if (!_pendingRemoves.Contains(item))
                        {
                            invoker(item, deltaTime, unscaledDeltaTime);
                        }
                    }
                }
                finally
                {
                    _isDispatching = false;
                    ApplyPendingChanges();
                }
            }

            public void Clear()
            {
                _pendingAdds.Clear();

                if (_isDispatching)
                {
                    for (var index = 0; index < _items.Count; index++)
                    {
                        _pendingRemoves.Add(_items[index]);
                    }

                    return;
                }

                _pendingRemoves.Clear();
                _members.Clear();
                _items.Clear();
            }

            private void ApplyPendingChanges()
            {
                if (_pendingRemoves.Count > 0)
                {
                    foreach (var item in _pendingRemoves)
                    {
                        _members.Remove(item);
                        _items.Remove(item);
                    }

                    _pendingRemoves.Clear();
                }

                if (_pendingAdds.Count <= 0)
                {
                    return;
                }

                foreach (var item in _pendingAdds)
                {
                    if (_members.Add(item))
                    {
                        _items.Add(item);
                    }
                }

                _pendingAdds.Clear();
            }
        }
    }
}
