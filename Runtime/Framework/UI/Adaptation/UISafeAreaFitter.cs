#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace AlloyFramework.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class UISafeAreaFitter : MonoBehaviour, IUIScreenAdaptationTarget
    {
        [SerializeField] private bool m_fitLeft = true; // 是否应用左侧安全区域。
        [SerializeField] private bool m_fitRight = true; // 是否应用右侧安全区域。
        [SerializeField] private bool m_fitTop = true; // 是否应用顶部安全区域。
        [SerializeField] private bool m_fitBottom = true; // 是否应用底部安全区域。
        [SerializeField] private Vector4 m_padding; // 按左、下、右、上顺序定义的 Canvas 坐标 Padding。
        [SerializeField] private bool m_restoreLayoutOnDisable = true; // 禁用时是否恢复初始布局。
        [SerializeField] private Rect m_currentSafeArea; // 当前安全区域像素矩形。
        [SerializeField] private Vector2 m_currentAnchorMin; // 当前最终左下锚点。
        [SerializeField] private Vector2 m_currentAnchorMax; // 当前最终右上锚点。

        private RectTransform m_rectTransform; // 需要应用安全区域的目标节点。
        private Vector2 m_originalAnchorMin; // 启用前的原始左下锚点。
        private Vector2 m_originalAnchorMax; // 启用前的原始右上锚点。
        private Vector2 m_originalOffsetMin; // 启用前的原始最小偏移。
        private Vector2 m_originalOffsetMax; // 启用前的原始最大偏移。
        private bool m_layoutCaptured; // 是否已保存原始布局。
        private bool m_runtimeLayoutRefreshPending; // 是否已安排延迟运行时布局刷新。
        private UIScreenAdaptationSystem m_system; // 当前所属的全局适配系统。

        private void OnEnable()
        {
            CaptureLayout();
            RegisterToSystem();
        }

        private void OnDisable()
        {
            UnregisterFromSystem();
            if (m_restoreLayoutOnDisable)
            {
                RestoreLayout();
            }
        }

        private void OnDestroy()
        {
            UnregisterFromSystem();
#if UNITY_EDITOR
            EditorApplication.delayCall -= ApplyScheduledRuntimeLayoutRefresh;
#endif
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
            {
                ScheduleRuntimeLayoutRefresh();
            }
#endif
        }

        /// <summary>
        /// 重新应用当前运行时屏幕适配快照。
        /// </summary>
        public void RefreshRuntimeLayout()
        {
            if (m_system != null)
            {
                ApplyScreenAdaptation(m_system.Snapshot);
            }
        }

#if UNITY_EDITOR
        private void ScheduleRuntimeLayoutRefresh()
        {
            if (m_runtimeLayoutRefreshPending)
            {
                return;
            }

            m_runtimeLayoutRefreshPending = true;
            EditorApplication.delayCall += ApplyScheduledRuntimeLayoutRefresh;
        }

        private void ApplyScheduledRuntimeLayoutRefresh()
        {
            m_runtimeLayoutRefreshPending = false;
            if (this == null || !Application.isPlaying)
            {
                return;
            }

            RefreshRuntimeLayout();
        }
#endif

        /// <summary>
        /// 应用最新的屏幕适配快照。
        /// </summary>
        /// <param name="snapshot">不可变的当前屏幕适配数据。</param>
        public void ApplyScreenAdaptation(UIScreenAdaptationSnapshot snapshot)
        {
            CaptureLayout();

            // 仅调整选中的安全区域边，并以初始偏移为基准应用 Canvas 坐标 Padding。
            var anchorMin = m_originalAnchorMin;
            var anchorMax = m_originalAnchorMax;
            if (m_fitLeft)
            {
                anchorMin.x = snapshot.SafeAreaAnchorMin.x;
            }

            if (m_fitRight)
            {
                anchorMax.x = snapshot.SafeAreaAnchorMax.x;
            }

            if (m_fitBottom)
            {
                anchorMin.y = snapshot.SafeAreaAnchorMin.y;
            }

            if (m_fitTop)
            {
                anchorMax.y = snapshot.SafeAreaAnchorMax.y;
            }

            var rectTransform = GetRectTransform();
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.offsetMin = m_originalOffsetMin + new Vector2(m_padding.x, m_padding.y);
            rectTransform.offsetMax = m_originalOffsetMax - new Vector2(m_padding.z, m_padding.w);
            m_currentSafeArea = snapshot.SafeArea;
            m_currentAnchorMin = anchorMin;
            m_currentAnchorMax = anchorMax;
        }

        private void CaptureLayout()
        {
            if (m_layoutCaptured)
            {
                return;
            }

            var rectTransform = GetRectTransform();
            m_originalAnchorMin = rectTransform.anchorMin;
            m_originalAnchorMax = rectTransform.anchorMax;
            m_originalOffsetMin = rectTransform.offsetMin;
            m_originalOffsetMax = rectTransform.offsetMax;
            m_layoutCaptured = true;
        }

        private void RestoreLayout()
        {
            if (!m_layoutCaptured)
            {
                return;
            }

            var rectTransform = GetRectTransform();
            rectTransform.anchorMin = m_originalAnchorMin;
            rectTransform.anchorMax = m_originalAnchorMax;
            rectTransform.offsetMin = m_originalOffsetMin;
            rectTransform.offsetMax = m_originalOffsetMax;
        }

        private void RegisterToSystem()
        {
            var uiRoot = GetComponentInParent<UIRoot>();
            if (uiRoot == null || !uiRoot.IsInitialized)
            {
                return;
            }

            m_system = uiRoot.ScreenAdaptationSystem;
            m_system.Register(this);
        }

        private void UnregisterFromSystem()
        {
            if (m_system == null)
            {
                return;
            }

            m_system.Unregister(this);
            m_system = null;
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
