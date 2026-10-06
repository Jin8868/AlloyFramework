using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlloyFramework.UI
{
    internal sealed class UIScreenAdaptationSystem : IUpdateable, IDisposable
    {
        private readonly List<IUIScreenAdaptationTarget> m_targets =
            new List<IUIScreenAdaptationTarget>(); // 已注册的适配目标。
        private readonly Action<UIScreenAdaptationSnapshot> m_snapshotChanged; // 快照更新处理器。
        private UIScreenAdaptationSnapshot m_snapshot; // 当前屏幕适配快照。
        private bool m_disposed; // 是否已停止系统更新。
        private bool m_hasSnapshot; // 是否已建立首份有效快照。

        public UIScreenAdaptationSnapshot Snapshot
        {
            get
            {
                if (!m_hasSnapshot)
                {
                    throw new InvalidOperationException("屏幕适配系统尚未建立有效快照。");
                }

                return m_snapshot;
            }
        }

        public UIScreenAdaptationSystem(Action<UIScreenAdaptationSnapshot> snapshotChanged)
        {
            m_snapshotChanged = snapshotChanged;
        }

        public void Initialize()
        {
            ThrowIfDisposed();

            // 初始化阶段必须立即产生可用快照，保证新打开的界面无需等待下一帧。
            RefreshSnapshot();
            GameLoop.Register(this);
        }

        internal bool TryGetSnapshot(out UIScreenAdaptationSnapshot snapshot)
        {
            snapshot = m_snapshot;
            return !m_disposed && m_hasSnapshot;
        }

        public void Register(IUIScreenAdaptationTarget target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            ThrowIfDisposed();
            if (m_targets.Contains(target))
            {
                return;
            }

            m_targets.Add(target);

            // 目标注册后立刻同步当前快照，避免界面首帧布局错误。
            target.ApplyScreenAdaptation(Snapshot);
        }

        public void Unregister(IUIScreenAdaptationTarget target)
        {
            if (target == null || m_disposed)
            {
                return;
            }

            m_targets.Remove(target);
        }

        public void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (!m_disposed)
            {
                RefreshSnapshot();
            }
        }

        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;
            GameLoop.Unregister(this);
            m_targets.Clear();
        }

        private void RefreshSnapshot()
        {
            var screenWidth = Screen.width;
            var screenHeight = Screen.height;
            if (screenWidth <= 0 || screenHeight <= 0)
            {
                return;
            }

            var safeArea = Screen.safeArea;
            var orientation = Screen.orientation;
            var currentSnapshot = new UIScreenAdaptationSnapshot(
                screenWidth,
                screenHeight,
                safeArea,
                orientation,
                m_hasSnapshot ? m_snapshot.Version : 0UL);
            if (m_hasSnapshot && m_snapshot.Equals(currentSnapshot))
            {
                return;
            }

            // 仅在屏幕数据确实变化时提升版本并通知当前目标。
            m_snapshot = new UIScreenAdaptationSnapshot(
                screenWidth,
                screenHeight,
                safeArea,
                orientation,
                m_hasSnapshot ? m_snapshot.Version + 1UL : 1UL);
            m_hasSnapshot = true;
            m_snapshotChanged?.Invoke(m_snapshot);
            for (var index = m_targets.Count - 1; index >= 0; index--)
            {
                var target = m_targets[index];
                if (target == null)
                {
                    m_targets.RemoveAt(index);
                    continue;
                }

                target.ApplyScreenAdaptation(m_snapshot);
            }
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(UIScreenAdaptationSystem));
            }
        }
    }
}
