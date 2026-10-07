using UnityEngine;

namespace AlloyFramework.UI
{
    [DisallowMultipleComponent]
    public sealed class UILayerRoot : MonoBehaviour
    {
        [SerializeField] private UILayer m_layer; // 当前容器所属的逻辑层级。
        [SerializeField] private RectTransform m_content; // 可选的界面挂载容器。

        public UILayer Layer => m_layer;
        public RectTransform Content => m_content != null ? m_content : (RectTransform)transform;

        internal void Configure()
        {
            // 兼容尚未迁移的层 Canvas，普通容器不再承担渲染排序。
            var canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                return;
            }

            canvas.overrideSorting = true;
            canvas.sortingOrder = (int)m_layer;
        }
    }
}
