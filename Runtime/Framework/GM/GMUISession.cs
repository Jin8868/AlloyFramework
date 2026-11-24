using System;
using System.Collections.Generic;
using System.Threading;
using AlloyFramework.UI;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlloyFramework.GM
{
    /// <summary>持有本轮 GM 界面实例并协调在途打开与退出。</summary>
    internal sealed class GMUISession
    {
        private readonly UIManager m_manager; // 创建两种 GM 界面的框架管理器。
        private readonly IReadOnlyList<GMCommand> m_commands; // 跨面板开关复用的命令状态。
        private readonly CancellationTokenSource m_lifetime = new CancellationTokenSource(); // 面板打开的取消源。
        private UIHandle m_entry; // 当前唯一的 GM 入口。
        private UIHandle m_panel; // 当前面板，外部关闭时句柄自动失效。
        private UniTaskCompletionSource m_openingPanel; // 等待同一次面板打开完成的信号。
        private bool m_isStopping; // 结束后拒绝入口提交的打开请求。

        internal GMUISession(UIManager manager, IReadOnlyList<GMCommand> commands)
        {
            m_manager = manager;
            m_commands = commands;
        }

        internal async UniTask ShowEntryAsync(CancellationToken cancellationToken)
        {
            m_entry = await m_manager.OpenAsync(GMUIDefinitions.Entry, this, cancellationToken);
        }

        internal void OpenPanel()
        {
            if (m_isStopping || m_openingPanel != null ||
                (m_panel != null && m_panel.IsValid && m_panel.State != UIState.Closing))
            {
                return;
            }

            // 预制体异步加载期间合并重复点击，关闭期间的重开由 UIManager 等待旧实例释放。
            m_openingPanel = new UniTaskCompletionSource();
            OpenPanelAsync(m_openingPanel).Forget(Debug.LogException);
        }

        internal async UniTask ShutdownAsync()
        {
            if (m_isStopping)
            {
                return;
            }

            // 取消并等待在途加载，保证业务退出后没有迟到的面板回调。
            m_isStopping = true;
            try
            {
                m_lifetime.Cancel();
                var openingPanel = m_openingPanel;
                if (openingPanel != null)
                {
                    await openingPanel.Task;
                }
                if (m_panel != null)
                {
                    await m_panel.CloseAsync();
                }
            }
            finally
            {
                m_lifetime.Dispose();
                if (m_entry != null)
                {
                    await m_entry.CloseAsync();
                }
            }
        }

        private async UniTask OpenPanelAsync(UniTaskCompletionSource completion)
        {
            try
            {
                m_panel = await m_manager.OpenAsync(GMUIDefinitions.Panel, m_commands, m_lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                // 关闭或结束框架会话时，控制权交还给界面的关闭流程。
            }
            finally
            {
                m_openingPanel = null;
                completion.TrySetResult();
            }
        }
    }
}
