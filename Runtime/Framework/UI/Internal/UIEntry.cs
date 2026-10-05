using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework.UI
{
    internal sealed class UIEntry
    {
        public UIDefinition Definition;
        public IInstanceHandle Instance;
        public UIView View;
        public UIController Controller;
        public UIHandle Handle;
        public UIState State;
        public CancellationTokenSource Lifetime;
        public UniTaskCompletionSource<UIHandle> Opening;
        public UniTaskCompletionSource Closing;
        public bool Created;
        public bool Opened;
        public bool OpenLifecycleStarted; // 是否已完成数据初始化并进入打开生命周期。
        public bool CloseRequested; // 是否已请求打断尚未提交的打开流程。
        public object Data;
    }
}
