using UnityEngine;

namespace AlloyFramework.UI
{
    /// <summary>无限列表的可复用显示项。</summary>
    [RequireComponent(typeof(RectTransform))]
    public abstract class UIListItem : MonoBehaviour
    {
        private bool m_initialized; // 实例初始化只执行一次。
        private bool m_disposed; // 防止重复释放。
        private RectTransform m_rectTransform; // 缓存布局节点。

        /// <summary>当前绑定的数据，解绑后为空。</summary>
        public object Data { get; private set; }

        /// <summary>绑定版本；异步任务开始时保存，用于拒绝复用后的旧结果。</summary>
        public uint BindingVersion { get; private set; }

        /// <summary>当前数据索引，解绑后为负一。</summary>
        public int Index { get; private set; } = -1;

        /// <summary>当前实例的布局节点。</summary>
        public RectTransform RectTransform
        {
            get
            {
                if (m_rectTransform == null) m_rectTransform = (RectTransform)transform;
                return m_rectTransform;
            }
        }

        internal void InitializeItem()
        {
            if (m_initialized) return;
            m_initialized = true;
            OnInitialize();
        }

        internal void BindItem(object data, int index)
        {
            UnbindItem();
            BindingVersion++;
            Data = data;
            Index = index;
            OnBind();
        }

        internal void BeforeDisplayItem()
        {
            OnBeforeDisplay();
        }

        internal void UnbindItem()
        {
            if (Index < 0) return;
            try
            {
                // 解绑钩子仍可读取旧数据，之后无论是否异常都清除引用。
                OnUnbind();
            }
            finally
            {
                BindingVersion++;
                Data = null;
                Index = -1;
            }
        }

        internal void DisposeItem()
        {
            if (m_disposed) return;
            m_disposed = true;
            try
            {
                UnbindItem();
            }
            finally
            {
                OnDispose();
            }
        }

        /// <summary>创建后调用一次，此时尚未绑定数据。</summary>
        protected virtual void OnInitialize() { }

        /// <summary>根据当前数据完整更新显示。</summary>
        protected abstract void OnBind();

        /// <summary>本批次绑定与布局完成后、提交显示前调用，不代表 GPU 渲染回调。</summary>
        protected virtual void OnBeforeDisplay() { }

        /// <summary>在旧数据清空前取消任务、移除订阅并清理显示状态。</summary>
        protected virtual void OnUnbind() { }

        /// <summary>实例最终释放时调用一次。</summary>
        protected virtual void OnDispose() { }

        /// <summary>外部销毁实例时同样释放业务资源。</summary>
        protected virtual void OnDestroy()
        {
            DisposeItem();
        }
    }
}