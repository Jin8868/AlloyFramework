using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AlloyFramework.Audio
{
    public readonly struct AudioCacheInfo
    {
        /// <summary>没有业务使用者的已加载资源组数量，不含 Init。</summary>
        public int IdleGroupCount { get; }
        /// <summary>闲置组及其保留依赖的文件大小估算，同一组只计算一次。</summary>
        public long EstimatedRetainedFileBytes { get; }
        /// <summary>文件大小估算预算，不代表 Wwise 总内存硬上限。</summary>
        public long BudgetBytes { get; }
        /// <summary>引用归零后保留的真实秒数。</summary>
        public float KeepAliveSeconds { get; }

        internal AudioCacheInfo(int count, long bytes, long budget, float seconds)
        {
            IdleGroupCount = count;
            EstimatedRetainedFileBytes = bytes;
            BudgetBytes = budget;
            KeepAliveSeconds = seconds;
        }
    }

    public sealed partial class AudioManager
    {
        private readonly HashSet<string> m_cacheCountedGroups = new HashSet<string>(); // 依赖估算去重。
        private float m_idleCacheSeconds = 30; // 当前会话的闲置保留时间。
        private long m_idleCacheBudgetBytes = 32L * 1024 * 1024; // 文件大小估算预算。
        private double m_nextCacheCheck; // 限制常规淘汰检查频率。
        private bool m_lowMemoryPending; // 低内存回调只登记，卸载统一在主线程更新处理。
        private bool m_trimmingCache; // 防止依赖引用释放时重入淘汰。

        /// <summary>配置闲置缓存策略，立即按新预算和保留时间淘汰可释放的资源。</summary>
        /// <param name="keepAliveSeconds">非负真实秒数，零表示不保留。</param>
        /// <param name="budgetBytes">非负文件大小估算预算，零表示不保留。</param>
        /// <exception cref="ArgumentOutOfRangeException">参数不是有限非负值。</exception>
        public void ConfigureIdleCache(float keepAliveSeconds, long budgetBytes)
        {
            ValidateCacheSettings(keepAliveSeconds, budgetBytes);
            m_idleCacheSeconds = keepAliveSeconds;
            m_idleCacheBudgetBytes = budgetBytes;
            TrimIdleCache(keepAliveSeconds == 0 || budgetBytes == 0);
        }

        /// <summary>查询闲置缓存及其依赖的估算，不包含 SDK 保留内存或解码内存。</summary>
        /// <returns>当前缓存数量、估算预算和保留时间。</returns>
        public AudioCacheInfo GetCacheInfo()
        {
            int count = 0;
            long bytes = 0;
            m_cacheCountedGroups.Clear();
            foreach (GroupEntry entry in m_groups.Values)
            {
                if (!IsIdleGroup(entry)) { continue; }
                count++;
                bytes = AddEstimatedBytes(bytes, CountRetainedFiles(entry));
            }
            return new AudioCacheInfo(count, bytes, m_idleCacheBudgetBytes, m_idleCacheSeconds);
        }

        /// <summary>清理全部闲置缓存，活跃播放和显式预加载持有的资源保持有效。</summary>
        public void ClearIdleCache() { TrimIdleCache(true); }

        internal void HandleLowMemory() { m_lowMemoryPending = true; }

        private void MarkGroupUnused(GroupEntry entry)
        {
            if (entry.Released || entry.Users != 0) { return; }
            // 失败与关闭流程不进入缓存，避免重用失败任务或延迟退出释放。
            if (!entry.NativeLoaded || m_shuttingDown || m_idleCacheSeconds == 0 || m_idleCacheBudgetBytes == 0)
            { UnloadGroup(entry); return; }
            entry.IdleSince = Time.realtimeSinceStartupAsDouble;
            m_nextCacheCheck = 0;
        }

        private void UpdateIdleCache()
        {
            if (!IsReady) { return; }
            double now = Time.realtimeSinceStartupAsDouble;
            bool clearAll = m_lowMemoryPending;
            if (!clearAll && now < m_nextCacheCheck) { return; }
            m_lowMemoryPending = false;
            m_nextCacheCheck = now + 0.5;
            if (clearAll) { AlloyDebug.Log("[Audio/Cache] 收到低内存通知，清理闲置缓存并保留使用中的资源。"); }
            TrimIdleCache(clearAll);
        }

        private void TrimIdleCache(bool clearAll)
        {
            if (m_trimmingCache || m_shuttingDown) { return; }
            m_trimmingCache = true;
            try
            {
                double now = Time.realtimeSinceStartupAsDouble;
                // 每次卸载都重新选取，依赖组可能在父组退出后刚刚变为闲置。
                while (true)
                {
                    GroupEntry oldest = FindOldestIdleGroup();
                    if (oldest == null) { break; }
                    bool expired = now - oldest.IdleSince >= m_idleCacheSeconds;
                    if (!clearAll && !expired && GetCacheInfo().EstimatedRetainedFileBytes <= m_idleCacheBudgetBytes)
                    { break; }
                    UnloadGroup(oldest);
                }
            }
            finally { m_trimmingCache = false; }
        }

        private GroupEntry FindOldestIdleGroup()
        {
            GroupEntry oldest = null;
            foreach (GroupEntry entry in m_groups.Values)
            {
                if (IsIdleGroup(entry) && (oldest == null || entry.IdleSince < oldest.IdleSince))
                { oldest = entry; }
            }
            return oldest;
        }

        private static bool IsIdleGroup(GroupEntry entry)
        {
            return !entry.Released && entry.LoadFinished && entry.NativeLoaded
                && entry.Users == 0 && entry.IdleSince >= 0;
        }

        private long CountRetainedFiles(GroupEntry entry)
        {
            if (!m_cacheCountedGroups.Add(entry.Key)) { return 0; }
            long bytes = entry.EstimatedFileBytes;
            // 缓存父组必须保留依赖租约，共享依赖去重；仍在活跃使用的共享依赖也保守计入估算。
            foreach (AudioGroupLease lease in entry.Dependencies)
            {
                if (m_groups.TryGetValue(lease.GroupKey, out GroupEntry dependency))
                { bytes = AddEstimatedBytes(bytes, CountRetainedFiles(dependency)); }
            }
            return bytes;
        }

        private static long EstimateGroupFiles(AudioContentLease content)
        {
            long bytes = 0;
            foreach (AudioContentFile file in content.Group.Files)
            {
                long length = new FileInfo(Path.Combine(content.DirectoryPath, file.Path)).Length;
                bytes = AddEstimatedBytes(bytes, length);
            }
            return bytes;
        }

        private static long AddEstimatedBytes(long total, long addition)
        { return addition > long.MaxValue - total ? long.MaxValue : total + addition; }

        private static void ValidateCacheSettings(float seconds, long bytes)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0)
            { throw new ArgumentOutOfRangeException(nameof(seconds), "缓存保留时间必须是有限非负值。"); }
            if (bytes < 0) { throw new ArgumentOutOfRangeException(nameof(bytes), "缓存预算不能为负。"); }
        }

        private void ResetIdleCache()
        {
            m_cacheCountedGroups.Clear();
            m_lowMemoryPending = false;
            m_nextCacheCheck = 0;
        }
    }
}
