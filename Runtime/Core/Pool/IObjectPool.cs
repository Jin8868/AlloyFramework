using System;

namespace AlloyFramework
{
    public interface IObjectPool : IDisposable
    {
        Type ObjectType { get; }

        int CountAll { get; }

        int CountActive { get; }

        int CountInactive { get; }

        int MaxSize { get; }

        bool CollectionCheck { get; }

        bool IsDisposed { get; }

        void Clear();
    }

    /// <summary>
    /// Contract for a non-thread-safe pool of reference-type objects.
    /// </summary>
    public interface IObjectPool<T> : IObjectPool where T : class
    {
        T Rent();

        void Return(T item);

        void Prewarm(int count);
    }
}
