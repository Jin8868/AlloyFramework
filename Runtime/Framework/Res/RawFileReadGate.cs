using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>协调原始文件直读租约与框架发起的资源包缓存清理。</summary>
    internal static class RawFileReadGate
    {
        private static readonly object m_sync = new object(); // 保护读者计数和清理状态。
        private static readonly Dictionary<string, int> m_readers = new Dictionary<string, int>(); // 每包活跃读者。
        private static readonly HashSet<string> m_cleaning = new HashSet<string>(); // 正在清理缓存的资源包。

        internal static async UniTask<IDisposable> ReserveReadAsync(string packageName, CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                lock (m_sync)
                {
                    if (!m_cleaning.Contains(packageName))
                    {
                        m_readers.TryGetValue(packageName, out int count);
                        m_readers[packageName] = count + 1;
                        return new Reservation(packageName, false);
                    }
                }
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
        }

        internal static async UniTask<IDisposable> ReserveCleanupAsync(string packageName, CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                lock (m_sync)
                {
                    if (!m_readers.ContainsKey(packageName) && m_cleaning.Add(packageName))
                    { return new Reservation(packageName, true); }
                }
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
        }

        private sealed class Reservation : IDisposable
        {
            private readonly string m_packageName; // 当前预留覆盖的资源包。
            private readonly bool m_cleanup; // 清理预留与普通读取预留分开归还。
            private bool m_disposed; // 避免重复归还计数。

            internal Reservation(string packageName, bool cleanup)
            {
                m_packageName = packageName;
                m_cleanup = cleanup;
            }

            /// <summary>归还读取或清理预留，允许后续操作继续。</summary>
            public void Dispose()
            {
                lock (m_sync)
                {
                    if (m_disposed) { return; }
                    m_disposed = true;
                    if (m_cleanup) { m_cleaning.Remove(m_packageName); return; }
                    int count = m_readers[m_packageName] - 1;
                    if (count == 0) { m_readers.Remove(m_packageName); }
                    else { m_readers[m_packageName] = count; }
                }
            }
        }
    }
}
