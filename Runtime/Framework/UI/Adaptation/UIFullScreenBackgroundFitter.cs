using UnityEngine;
using UnityEngine.UI;

namespace AlloyFramework.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AspectRatioFitter))]
    [RequireComponent(typeof(RectTransform))]
    public sealed class UIFullScreenBackgroundFitter : MonoBehaviour
    {
        [SerializeField] private EUIFullScreenBackgroundMode m_mode =
            EUIFullScreenBackgroundMode.Cover; // 背景显示策略。
        [SerializeField] private float m_aspectRatio = 16f / 9f; // 背景素材宽高比。

        private AspectRatioFitter m_aspectRatioFitter; // Unity 内置比例适配器。
        private RectTransform m_rectTransform; // 背景的目标节点。

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
            if (m_mode == EUIFullScreenBackgroundMode.Custom)
            {
                aspectRatioFitter.enabled = false;
                return;
            }

            var rectTransform = GetRectTransform();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = Vector2.zero;
            if (m_mode == EUIFullScreenBackgroundMode.Stretch)
            {
                aspectRatioFitter.enabled = false;
                return;
            }

            aspectRatioFitter.enabled = true;
            aspectRatioFitter.aspectRatio = Mathf.Max(0.0001f, m_aspectRatio);
            aspectRatioFitter.aspectMode = m_mode == EUIFullScreenBackgroundMode.Cover
                ? AspectRatioFitter.AspectMode.EnvelopeParent
                : AspectRatioFitter.AspectMode.FitInParent;
        }

        private AspectRatioFitter GetAspectRatioFitter()
        {
            if (m_aspectRatioFitter == null)
            {
                m_aspectRatioFitter = GetComponent<AspectRatioFitter>();
            }

            return m_aspectRatioFitter;
        }

        private RectTransform GetRectTransform()
        {
            if (m_rectTransform == null)
            {
                m_rectTransform = (RectTransform)transform;
            }

            return m_rectTransform;
        }
    }
}
