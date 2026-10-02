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
        public object Data;
    }
}
