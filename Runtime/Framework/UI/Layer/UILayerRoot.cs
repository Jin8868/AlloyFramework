using UnityEngine;

namespace AlloyFramework.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    public sealed class UILayerRoot : MonoBehaviour
    {
        [SerializeField] private UILayer m_layer;
        [SerializeField] private RectTransform m_content;

        public UILayer Layer => m_layer;
        public RectTransform Content => m_content != null ? m_content : (RectTransform)transform;

        internal void Configure()
        {
            var canvas = GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = (int)m_layer;
        }
    }
}
