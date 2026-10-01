using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace AlloyFramework
{
    /// <summary>
    /// Non-thread-safe generic object pool intended for use on the game thread.
    /// </summary>
    public sealed class ObjectPool<T> : IObjectPool<T> where T : class
    {
        private readonly Func<T> _create;
        private readonly Action<T> _onRent;
        private readonly Action<T> _onReturn;
        private readonly Action<T> _onDestroy;
        private readonly Stack<T> _inactive;
        private readonly HashSet<T> _inactiveSet;
        private readonly HashSet<T> _owned;

        public ObjectPool(
            Func<T> create,
            Action<T> onRent = null,
            Action<T> onReturn = null,
            Action<T> onDestroy = null,
            bool collectionCheck = true,
            int initialCapacity = 0,
            int maxSize = 10000)
        {
            _create = create ?? throw new ArgumentNullException(nameof(create));

            if (initialCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialCapacity),
                    initialCapacity,
                    "Initial capacity cannot be negative.");
            }

            if (maxSize <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxSize),
                    maxSize,
                    "Maximum size must be greater than zero.");
            }

            if (initialCapacity > maxSize)
            {
                throw new ArgumentException(
                    "Initial capacity cannot be greater than maximum size.",
                    nameof(initialCapacity));
            }

            _onRent = onRent;
            _onReturn = onReturn;
            _onDestroy = onDestroy;
            CollectionCheck = collectionCheck;
            MaxSize = maxSize;

            var comparer = ReferenceComparer.Instance;
            _inactive = new Stack<T>(initialCapacity);
            _inactiveSet = new HashSet<T>(comparer);
            _owned = new HashSet<T>(comparer);
        }

        public int CountAll => _owned.Count;

        public Type ObjectType => typeof(T);

        public int CountActive => _owned.Count - _inactive.Count;

        public int CountInactive => _inactive.Count;

        public int MaxSize { get; }

        public bool CollectionCheck { get; }

        public bool IsDisposed { get; private set; }

        public T Rent()
        {
            ThrowIfDisposed();

            T item;
            if (_inactive.Count > 0)
            {
                item = _inactive.Pop();
                _inactiveSet.Remove(item);
            }
            else
            {
                item = CreateItem();
            }

            try
            {
                _onRent?.Invoke(item);
                return item;
            }
            catch
            {
                DestroyOwnedItem(item);
                throw;
            }
        }

        public void Return(T item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            if (!_owned.Contains(item))
            {
                throw new InvalidOperationException(
                    $"The returned object is not owned by this {GetType().Name}.");
            }

            if (_inactiveSet.Contains(item))
            {
                if (CollectionCheck)
                {
                    throw new InvalidOperationException(
                        "The object has already been returned to this pool.");
                }

                return;
            }

            if (IsDisposed)
            {
                DestroyOwnedItem(item);
                return;
            }

            _onReturn?.Invoke(item);

            if (_inactive.Count < MaxSize)
            {
                _inactive.Push(item);
                _inactiveSet.Add(item);
                return;
            }

            DestroyOwnedItem(item);
        }

        public void Prewarm(int count)
        {
            ThrowIfDisposed();

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count),
                    count,
                    "Prewarm count cannot be negative.");
            }

            int createCount = Math.Min(count, MaxSize - _inactive.Count);
            for (int i = 0; i < createCount; i++)
            {
                T item = CreateItem();
                try
                {
                    _onReturn?.Invoke(item);
                    _inactive.Push(item);
                    _inactiveSet.Add(item);
                }
                catch
                {
                    DestroyOwnedItem(item);
                    throw;
                }
            }
        }

        public void Clear()
        {
            ThrowIfDisposed();
            ClearInactiveItems();
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            ClearInactiveItems();
        }

        private T CreateItem()
        {
            T item = _create();
            if (item == null)
            {
                throw new InvalidOperationException(
                    "The object pool factory returned null.");
            }

            if (!_owned.Add(item))
            {
                throw new InvalidOperationException(
                    "The object pool factory returned an object already owned by this pool.");
            }

            return item;
        }

        private void ClearInactiveItems()
        {
            Exception firstException = null;

            while (_inactive.Count > 0)
            {
                T item = _inactive.Pop();
                _inactiveSet.Remove(item);
                _owned.Remove(item);

                try
                {
                    _onDestroy?.Invoke(item);
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
                throw firstException;
            }
        }

        private void DestroyOwnedItem(T item)
        {
            _inactiveSet.Remove(item);
            _owned.Remove(item);
            _onDestroy?.Invoke(item);
        }

        private void ThrowIfDisposed()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        private sealed class ReferenceComparer : IEqualityComparer<T>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();

            public bool Equals(T x, T y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(T item)
            {
                return RuntimeHelpers.GetHashCode(item);
            }
        }
    }
}
