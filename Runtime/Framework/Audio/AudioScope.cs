using System;
using System.Collections.Generic;
using System.Threading;

namespace AlloyFramework.Audio
{
    public sealed class AudioScope : IDisposable
    {
        private readonly AudioManager m_manager; // 作用域所属管理器。
        private readonly List<AudioGroupLease> m_leases = new List<AudioGroupLease>(); // 预加载资源租约。
        private readonly CancellationTokenSource m_cancellation = new CancellationTokenSource(); // 取消准备阶段。
        public long ID { get; }
        public string Name { get; }
        internal long Session { get; }
        internal CancellationToken Token => m_cancellation.Token;
        public bool IsDisposed { get; private set; }

        internal AudioScope(AudioManager manager, long id, long session, string name)
        {
            m_manager = manager;
            ID = id;
            Session = session;
            Name = name;
        }

        /// <summary>取消未开始请求，停止本作用域声音并释放预加载引用。</summary>
        public void Dispose()
        {
            if (IsDisposed) { return; }
            IsDisposed = true;
            m_manager.StopScope(this);
            m_cancellation.Cancel();
            foreach (AudioGroupLease lease in m_leases.ToArray()) { lease.Dispose(); }
            m_leases.Clear();
            m_cancellation.Dispose();
        }

        internal void Track(AudioGroupLease lease)
        {
            if (IsDisposed) { throw new ObjectDisposedException(nameof(AudioScope)); }
            m_leases.Add(lease);
        }

        internal void Untrack(AudioGroupLease lease) { m_leases.Remove(lease); }
    }

    public sealed class AudioGroupLease : IDisposable
    {
        private AudioManager m_manager; // 非空表示尚未释放。
        private readonly object m_entry; // 本会话独占的协调器条目，避免旧租约释放新组。
        private readonly AudioScope m_scope; // 可选作用域归属。
        public string GroupKey { get; }
        public bool IsValid => m_manager != null;

        internal AudioGroupLease(AudioManager manager, string key, object entry, AudioScope scope)
        {
            m_manager = manager;
            m_entry = entry;
            m_scope = scope;
            GroupKey = key;
        }

        /// <summary>释放本持有者引用；正在播放的声音仍保留自己的引用。</summary>
        public void Dispose()
        {
            AudioManager manager = m_manager;
            m_manager = null;
            if (manager == null) { return; }
            m_scope?.Untrack(this);
            manager.ReleaseGroup(m_entry);
        }
    }
}
