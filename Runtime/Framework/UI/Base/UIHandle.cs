using System;
using AlloyFramework.UI;
using Cysharp.Threading.Tasks;

namespace AlloyFramework.UI
{
    public abstract class UIHandle
    {
        private readonly Func<UIHandle, UniTask> m_close;
        private readonly UIEntry m_entry; // 句柄所属的运行时 UI 记录。
        private bool m_valid = true;

        internal UIHandle(
            long instanceId,
            string uiName,
            Func<UIHandle, UniTask> close,
            UIEntry entry)
        {
            InstanceId = instanceId;
            UIName = uiName;
            m_close = close;
            m_entry = entry;
        }

        /// <summary>运行时 UI 实例的唯一标识。</summary>
        public long InstanceId { get; }
        /// <summary>界面定义的稳定名称。</summary>
        public string UIName { get; }
        /// <summary>界面当前的生命周期状态。</summary>
        public UIState State => m_valid ? m_entry.State : UIState.Disposed;
        /// <summary>句柄当前是否仍指向有效界面实例。</summary>
        public bool IsValid => m_valid;

        /// <summary>
        /// 异步关闭当前界面实例。
        /// </summary>
        /// <returns>表示关闭过程的任务。</returns>
        public UniTask CloseAsync() => m_valid ? m_close(this) : UniTask.CompletedTask;

        /// <summary>
        /// 关闭当前界面实例并通过回调报告错误。
        /// </summary>
        /// <param name="onCompleted">关闭完成后的错误回调。</param>
        public void Close(Action<Exception> onCompleted = null) => UICallbacks.Run(CloseAsync, onCompleted);
        internal void Invalidate() => m_valid = false;
    }
}

namespace AlloyFramework.UI
{
    public sealed class UIHandle<TController> : UIHandle where TController : UIController
    {
        private readonly TController m_controller;

        internal UIHandle(long instanceId, string uiName, TController controller,
            Func<UIHandle, UniTask> close, UIEntry entry)
            : base(instanceId, uiName, close, entry) => m_controller = controller;

        /// <summary>当前界面实例绑定的控制器。</summary>
        public TController Controller => IsValid ? m_controller :
            throw new InvalidOperationException($"UI handle {UIName}#{InstanceId} is no longer valid.");
    }
}
