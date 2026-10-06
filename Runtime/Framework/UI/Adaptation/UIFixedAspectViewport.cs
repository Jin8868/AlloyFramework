using UnityEngine;
using UnityEngine.UI;

namespace AlloyFramework.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AspectRatioFitter))]
    [RequireComponent(typeof(RectTransform))]
    public sealed class UIFixedAspectViewport : MonoBehaviour
    {
        [SerializeField] private float m_aspectRatio = 16f / 9f; // 固定内容区域的宽高比。
        [SerializeField] private Image m_letterboxBackground; // 由业务放置在内容后方的全屏留边背景。
        [SerializeField] private Color m_letterboxColor = Color.black; // 留边背景颜色。

        private AspectRatioFitter m_aspectRatioFitter; // Unity 内置比例适配器。

        private void Awake()
        {
            ApplyLayout();
        }

        private void OnValidate()
        {
            ApplyLayout();
        }

        private void ApplyLayout()
        {
            var aspectRatioFitter = GetAspectRatioFitter();
            aspectRatioFitter.enabled = true;
            aspectRatioFitter.aspectRatio = Mathf.Max(0.0001f, m_aspectRatio);
            aspectRatioFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            if (m_letterboxBackground != null)
            {
                m_letterboxBackground.color = m_letterboxColor;
            }
        }

        private AspectRatioFitter GetAspectRatioFitter()
        {
            if (m_aspectRatioFitter == null)
            {
                m_aspectRatioFitter = GetComponent<AspectRatioFitter>();
            }

            return m_aspectRatioFitter;
        }
    }
}
