using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework.UI
{
    internal sealed class UINavigator
    {
        private readonly UIManager m_manager; // 导航器所属的 UI 管理器。
        private readonly List<UINavigationRecord> m_records = new List<UINavigationRecord>(); // 已提交的导航记录。
        private IUIJumpConfigProvider m_provider; // 当前业务安装的跳转配置提供器。
        private bool m_isTransitioning; // 当前是否正在执行跳转或返回事务。

        internal UINavigator(UIManager manager)
        {
            m_manager = manager;
        }

        internal void Configure(IUIJumpConfigProvider provider)
        {
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            ValidateAll();
        }

        internal async UniTask<UIHandle> JumpAsync(
            int jumpID,
            UIJumpPrepareHandler prepareHandler,
            object data,
            CancellationToken cancellationToken)
        {
            if (m_isTransitioning)
            {
                throw new InvalidOperationException("已有 UI 跳转事务正在执行。");
            }

            m_isTransitioning = true;
            try
            {
                return await ExecuteJumpAsync(jumpID, prepareHandler, data, cancellationToken);
            }
            finally
            {
                m_isTransitioning = false;
            }
        }

        internal async UniTask<bool> BackAsync(CancellationToken cancellationToken)
        {
            if (m_isTransitioning)
            {
                throw new InvalidOperationException("已有 UI 导航事务正在执行。");
            }

            m_isTransitioning = true;
            try
            {
                var record = FindLatestRecord();
                if (record == null || record.BackMode == EUIBackMode.Disabled)
                {
                    return false;
                }

                m_records.Remove(record);
                await CloseIfValidAsync(record.TargetHandle);

                if (record.BackMode == EUIBackMode.InheritSource)
                {
                    await CloseIfValidAsync(record.SourceHandle);
                    m_manager.Resume(record.ReturnHandle);
                    return true;
                }

                m_manager.Resume(record.SourceHandle);
                if (record.BackMode == EUIBackMode.Explicit)
                {
                    await ExecuteJumpAsync(record.BackJumpID, null, null, cancellationToken);
                }

                return true;
            }
            finally
            {
                m_isTransitioning = false;
            }
        }

        internal void OnClosed(UIHandle handle)
        {
            if (handle == null)
            {
                return;
            }

            for (var index = m_records.Count - 1; index >= 0; index--)
            {
                var record = m_records[index];
                if (ReferenceEquals(record.TargetHandle, handle))
                {
                    m_records.RemoveAt(index);
                }
            }
        }

        internal void Clear()
        {
            m_records.Clear();
            m_provider = null;
            m_isTransitioning = false;
        }

        internal void ClearRecords()
        {
            m_records.Clear();
        }

        private async UniTask<UIHandle> ExecuteJumpAsync(
            int jumpID,
            UIJumpPrepareHandler prepareHandler,
            object data,
            CancellationToken cancellationToken)
        {
            EnsureConfigured();
            if (!m_provider.TryGet(jumpID, out var config))
            {
                throw new KeyNotFoundException($"未找到 UI 跳转配置：JumpID={jumpID}。");
            }

            if (!m_manager.TryGetDefinition(config.TargetUIName, out var targetDefinition))
            {
                throw new InvalidOperationException(
                    $"UI 跳转目标未注册：JumpID={jumpID}，TargetUIName={config.TargetUIName}。");
            }

            // Prepare 完成前不改变任何界面和导航记录。
            var sourceHandle = m_manager.CurrentHandle;
            var context = new UIJumpContext
            {
                JumpID = jumpID,
                Config = config,
                InputData = data,
                TargetData = data
            };
            if (prepareHandler != null)
            {
                await prepareHandler(context, cancellationToken);
            }

            var targetHandle = await m_manager.OpenDefinitionAsync(
                targetDefinition,
                context.TargetData,
                jumpID,
                cancellationToken);

            if (ReferenceEquals(sourceHandle, targetHandle))
            {
                return targetHandle;
            }

            // 目标完整打开后再暂停来源并提交记录。
            if (config.JumpMode == EUIJumpMode.Push)
            {
                m_manager.Pause(sourceHandle);
            }

            var sourceRecord = FindRecordByTarget(sourceHandle);
            var returnHandle = config.BackMode == EUIBackMode.InheritSource
                ? sourceRecord?.ReturnHandle ?? sourceRecord?.SourceHandle
                : sourceHandle;
            m_records.Add(new UINavigationRecord
            {
                JumpID = jumpID,
                JumpMode = config.JumpMode,
                BackMode = config.BackMode,
                BackJumpID = config.BackJumpID,
                SourceHandle = sourceHandle,
                ReturnHandle = returnHandle,
                TargetHandle = targetHandle
            });
            return targetHandle;
        }

        private void ValidateAll()
        {
            foreach (var config in m_provider.GetAll())
            {
                if (config == null || config.JumpID <= 0)
                {
                    throw new InvalidOperationException("UI 跳转配置包含无效的 JumpID。");
                }

                if (string.IsNullOrWhiteSpace(config.TargetUIName) ||
                    !m_manager.ContainsDefinition(config.TargetUIName))
                {
                    throw new InvalidOperationException(
                        $"UI 跳转配置目标未注册：JumpID={config.JumpID}，TargetUIName={config.TargetUIName}。");
                }

                var requiresBackJump = config.BackMode == EUIBackMode.Explicit;
                if (requiresBackJump != (config.BackJumpID > 0))
                {
                    throw new InvalidOperationException(
                        $"UI 跳转配置的 BackJumpID 无效：JumpID={config.JumpID}，BackJumpID={config.BackJumpID}。");
                }

                if (requiresBackJump && !m_provider.TryGet(config.BackJumpID, out _))
                {
                    throw new InvalidOperationException(
                        $"UI 跳转配置引用不存在：JumpID={config.JumpID}，BackJumpID={config.BackJumpID}。");
                }

                if (requiresBackJump)
                {
                    ValidateExplicitBackPath(config);
                }
            }
        }

        private void ValidateExplicitBackPath(UIJumpConfig startConfig)
        {
            var visitedJumpIDs = new HashSet<int>();
            var currentConfig = startConfig;
            while (currentConfig.BackMode == EUIBackMode.Explicit)
            {
                if (!visitedJumpIDs.Add(currentConfig.JumpID))
                {
                    throw new InvalidOperationException(
                        $"UI 跳转配置形成显式返回循环：JumpID={startConfig.JumpID}。");
                }

                if (!m_provider.TryGet(currentConfig.BackJumpID, out currentConfig))
                {
                    return;
                }
            }
        }

        private UINavigationRecord FindLatestRecord()
        {
            for (var index = m_records.Count - 1; index >= 0; index--)
            {
                var record = m_records[index];
                if (record.TargetHandle != null && record.TargetHandle.IsValid)
                {
                    return record;
                }

                m_records.RemoveAt(index);
            }

            return null;
        }

        private UINavigationRecord FindRecordByTarget(UIHandle handle)
        {
            for (var index = m_records.Count - 1; index >= 0; index--)
            {
                if (ReferenceEquals(m_records[index].TargetHandle, handle))
                {
                    return m_records[index];
                }
            }

            return null;
        }

        private static UniTask CloseIfValidAsync(UIHandle handle)
        {
            return handle != null && handle.IsValid ? handle.CloseAsync() : UniTask.CompletedTask;
        }

        private void EnsureConfigured()
        {
            if (m_provider == null)
            {
                throw new InvalidOperationException("UI 导航尚未配置跳转数据提供器。");
            }
        }
    }
}
