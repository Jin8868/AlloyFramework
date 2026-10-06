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
    public sealed class UIFixedAspectViewport : MonoBehaviour
    {
        [SerializeField] private float m_aspectRatio = 16f / 9f; // 固定内容区域的宽高比。
        [SerializeField] private Image m_letterboxBackground; // 由业务放置在内容后方的全屏留边背景。
        [SerializeField] private Color m_letterboxColor = Color.black; // 留边背景颜色。

        private AspectRatioFitter m_aspectRatioFitter; // Unity 内置比例适配器。

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
