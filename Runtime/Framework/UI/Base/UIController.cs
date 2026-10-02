using System;
using System.Threading;
using AlloyFramework.UI;
using Cysharp.Threading.Tasks;

namespace AlloyFramework.UI
{
    public abstract class UIController
    {
        private UIHandle m_handle;

        public string UIName { get; internal set; }
        public UIState State { get; internal set; }
        internal abstract UIView UntypedView { get; }

        public UniTask CloseAsync() => m_handle?.CloseAsync() ?? UniTask.CompletedTask;

        public void Close(Action<Exception> onCompleted = null) =>
            UICallbacks.Run(CloseAsync, onCompleted);

        internal void SetHandle(UIHandle handle) => m_handle = handle;
        internal abstract void AttachView(UIView view);
        internal abstract UniTask PrepareAsync(object data, CancellationToken cancellationToken);
        internal abstract void InitData(object data);
        internal abstract void Refresh(object data);

        internal void Create() => OnCreate();
        internal void StartOpenAnimation() => OnStartOpenAnimation();
        internal void EndOpenAnimation() => OnEndOpenAnimation();
        internal void Opened() => OnOpen();
        internal void Pause() => OnPause();
        internal void Resume() => OnResume();
        internal void Closed() => OnClose();
        internal void StartCloseAnimation() => OnStartCloseAnimation();
        internal void EndCloseAnimation() => OnEndCloseAnimation();
        internal void DisposeController() { OnDispose(); m_handle = null; }

        protected virtual void OnCreate() { }
        protected virtual void OnStartOpenAnimation() { }
        protected virtual void OnEndOpenAnimation() { }
        protected virtual void OnOpen() { }
        protected virtual void OnPause() { }
        protected virtual void OnResume() { }
        protected virtual void OnClose() { }
        protected virtual void OnStartCloseAnimation() { }
        protected virtual void OnEndCloseAnimation() { }
        protected virtual void OnDispose() { }
    }
}

namespace AlloyFramework.UI
{
    public abstract class UIController<TView, TData> : UIController where TView : UIView
    {
        private TView m_view;

        protected TView View => State != UIState.Disposed && m_view != null
            ? m_view
            : throw new InvalidOperationException($"UI controller {UIName} no longer has a valid View.");
        protected TData Data { get; private set; }
        internal override UIView UntypedView => View;

        internal override void AttachView(UIView view) => m_view = (TView)view;

        internal override async UniTask PrepareAsync(object data, CancellationToken cancellationToken)
        {
            Data = (TData)data;
            await OnPrepareAsync(Data, cancellationToken);
        }

        internal override void InitData(object data)
        {
            Data = (TData)data;
            OnInitData(Data);
        }

        internal override void Refresh(object data)
        {
            Data = (TData)data;
            OnRefresh(Data);
        }

        protected virtual UniTask OnPrepareAsync(TData data, CancellationToken cancellationToken) => UniTask.CompletedTask;
        protected virtual void OnInitData(TData data) { }
        protected virtual void OnRefresh(TData data) { }
    }

    public abstract class UIController<TView> : UIController<TView, UIEmptyData>
        where TView : UIView { }
}
