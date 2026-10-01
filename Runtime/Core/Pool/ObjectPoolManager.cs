using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace AlloyFramework
{
    /// <summary>
    /// Owns and provides centralized access to object pools.
    /// Pools are identified by object type and an optional name.
    /// </summary>
    public sealed class ObjectPoolManager : IDisposable
    {
        private static ObjectPoolManager _instance = new ObjectPoolManager();

        private readonly Dictionary<PoolKey, IObjectPool> _pools =
            new Dictionary<PoolKey, IObjectPool>();

        private ObjectPoolManager()
        {
        }

        public static ObjectPoolManager Instance => _instance;

        public int PoolCount => _pools.Count;

        public int CountAll => Sum(pool => pool.CountAll);

        public int CountActive => Sum(pool => pool.CountActive);

        public int CountInactive => Sum(pool => pool.CountInactive);

        public bool IsDisposed { get; private set; }

        internal static void ResetInstance()
        {
            ObjectPoolManager previousInstance = _instance;
            _instance = new ObjectPoolManager();
            previousInstance.Dispose();
        }

        public ObjectPool<T> Register<T>(
            Func<T> create,
            Action<T> onRent = null,
            Action<T> onReturn = null,
            Action<T> onDestroy = null,
            bool collectionCheck = true,
            int initialCapacity = 0,
            int maxSize = 10000,
            string name = null) where T : class
        {
            var pool = new ObjectPool<T>(
                create,
                onRent,
                onReturn,
                onDestroy,
                collectionCheck,
                initialCapacity,
                maxSize);

            try
            {
                Register(pool, name);
                return pool;
            }
            catch
            {
                pool.Dispose();
                throw;
            }
        }

        public void Register<T>(IObjectPool<T> pool, string name = null)
            where T : class
        {
            ThrowIfDisposed();

            if (pool == null)
            {
                throw new ArgumentNullException(nameof(pool));
            }

            if (pool.IsDisposed)
            {
                throw new ObjectDisposedException(pool.GetType().FullName);
            }

            if (pool.ObjectType != typeof(T))
            {
                throw new ArgumentException(
                    $"Pool object type is '{pool.ObjectType.FullName}', expected '{typeof(T).FullName}'.",
                    nameof(pool));
            }

            var key = new PoolKey(typeof(T), NormalizeName(name));
            if (_pools.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    $"An object pool is already registered for {key}.");
            }

            _pools.Add(key, pool);
        }

        public bool HasPool<T>(string name = null) where T : class
        {
            ThrowIfDisposed();
            return _pools.ContainsKey(new PoolKey(typeof(T), NormalizeName(name)));
        }

        public IObjectPool<T> GetPool<T>(string name = null) where T : class
        {
            ThrowIfDisposed();

            if (TryGetPool(out IObjectPool<T> pool, name))
            {
                return pool;
            }

            throw new KeyNotFoundException(
                $"No object pool is registered for {new PoolKey(typeof(T), NormalizeName(name))}.");
        }

        public bool TryGetPool<T>(out IObjectPool<T> pool, string name = null)
            where T : class
        {
            ThrowIfDisposed();

            var key = new PoolKey(typeof(T), NormalizeName(name));
            if (_pools.TryGetValue(key, out IObjectPool value))
            {
                pool = (IObjectPool<T>)value;
                return true;
            }

            pool = null;
            return false;
        }

        public T Rent<T>(string name = null) where T : class
        {
            return GetPool<T>(name).Rent();
        }

        public void Return<T>(T item, string name = null) where T : class
        {
            GetPool<T>(name).Return(item);
        }

        public void Prewarm<T>(int count, string name = null) where T : class
        {
            GetPool<T>(name).Prewarm(count);
        }

        public void ClearPool<T>(string name = null) where T : class
        {
            GetPool<T>(name).Clear();
        }

        public bool Unregister<T>(string name = null) where T : class
        {
            ThrowIfDisposed();

            var key = new PoolKey(typeof(T), NormalizeName(name));
            if (!_pools.TryGetValue(key, out IObjectPool pool))
            {
                return false;
            }

            if (pool.CountActive > 0)
            {
                throw new InvalidOperationException(
                    $"Cannot unregister {key} while {pool.CountActive} object(s) are still rented.");
            }

            _pools.Remove(key);
            pool.Dispose();
            return true;
        }

        public void ClearAll()
        {
            ThrowIfDisposed();
            InvokeAll(pool => pool.Clear());
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            try
            {
                InvokeAll(pool => pool.Dispose());
            }
            finally
            {
                _pools.Clear();
            }
        }

        private int Sum(Func<IObjectPool, int> selector)
        {
            var total = 0;
            foreach (IObjectPool pool in _pools.Values)
            {
                total += selector(pool);
            }

            return total;
        }

        private void InvokeAll(Action<IObjectPool> action)
        {
            Exception firstException = null;

            foreach (IObjectPool pool in _pools.Values)
            {
                try
                {
                    action(pool);
                }
                catch (Exception exception)
                {
                    if (firstException == null)
                    {
                        firstException = exception;
                    }
                }
            }

            if (firstException != null)
            {
                ExceptionDispatchInfo.Capture(firstException).Throw();
            }
        }

        private static string NormalizeName(string name)
        {
            if (name == null)
            {
                return string.Empty;
            }

            if (name.Length == 0)
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Pool name cannot contain only whitespace.", nameof(name));
            }

            return name;
        }

        private void ThrowIfDisposed()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        private readonly struct PoolKey : IEquatable<PoolKey>
        {
            public PoolKey(Type objectType, string name)
            {
                ObjectType = objectType;
                Name = name;
            }

            public Type ObjectType { get; }

            public string Name { get; }

            public bool Equals(PoolKey other)
            {
                return ObjectType == other.ObjectType &&
                       string.Equals(Name, other.Name, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is PoolKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (ObjectType.GetHashCode() * 397) ^
                           StringComparer.Ordinal.GetHashCode(Name);
                }
            }

            public override string ToString()
            {
                return Name.Length == 0
                    ? $"type '{ObjectType.FullName}'"
                    : $"type '{ObjectType.FullName}' with name '{Name}'";
            }
        }
    }
}
