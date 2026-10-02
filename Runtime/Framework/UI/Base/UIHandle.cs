using System;
using AlloyFramework.UI;
using Cysharp.Threading.Tasks;

namespace AlloyFramework.UI
{
    public abstract class UIHandle
    {
        private readonly Func<UIHandle, UniTask> m_close;
        private readonly Func<UIState> m_state;
        private bool m_valid = true;

        internal UIHandle(long instanceId, string uiName, Func<UIHandle, UniTask> close,
            Func<UIState> state)
        {
            InstanceId = instanceId;
            UIName = uiName;
            m_close = close;
            m_state = state;
        }

        public long InstanceId { get; }
        public string UIName { get; }
        public UIState State => m_valid ? m_state() : UIState.Disposed;
        public bool IsValid => m_valid;

        public UniTask CloseAsync() => m_valid ? m_close(this) : UniTask.CompletedTask;
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
            Func<UIHandle, UniTask> close, Func<UIState> state)
            : base(instanceId, uiName, close, state) => m_controller = controller;

        public TController Controller => IsValid ? m_controller :
            throw new InvalidOperationException($"UI handle {UIName}#{InstanceId} is no longer valid.");
    }
}
