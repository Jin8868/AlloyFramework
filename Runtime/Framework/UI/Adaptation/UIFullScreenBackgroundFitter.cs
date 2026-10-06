#if UNITY_EDITOR
using UnityEditor;
#endif
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

        private void Start()
        {
            // 首次布局在初始化校验结束后应用，避免 Awake 中触发尺寸变更消息。
            ApplyLayout();
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            // 合并连续 Inspector 修改，退出校验回调后再调整布局。
            EditorApplication.delayCall -= ApplyDeferredLayout;
            EditorApplication.delayCall += ApplyDeferredLayout;
#endif
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            EditorApplication.delayCall -= ApplyDeferredLayout;
#endif
        }

#if UNITY_EDITOR
        private void ApplyDeferredLayout()
        {
            if (this == null || !isActiveAndEnabled)
            {
                return;
            }

            ApplyLayout();
        }
#endif

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
