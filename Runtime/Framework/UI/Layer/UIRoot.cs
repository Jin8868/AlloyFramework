using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AlloyFramework.UI
{
    [DisallowMultipleComponent]
    public sealed class UIRoot : MonoBehaviour, IUpdateable
    {
        private readonly List<Canvas> m_canvases = new List<Canvas>(); // 已打开及缓存的独立界面画布。
        private Dictionary<UILayer, UILayerRoot> m_layers; // 八层逻辑容器。
        private RectTransform m_windowRoot; // 不挂 Canvas 的界面分类容器。
        private Camera m_uiCamera; // 普通界面使用的共享相机。
        private Camera m_foregroundCamera; // 模糊边界及以上界面使用的固定相机。
        private UIScreenAdaptationSystem m_screenAdaptationSystem; // 全局屏幕适配系统。
        private bool m_isLandscape = true; // 当前采用的参考分辨率方向。
        private bool m_layoutRefreshPending; // Inspector 配置改变后等待主线程刷新。
        [SerializeField] private EUIScreenOrientationMode m_orientationMode =
            EUIScreenOrientationMode.FixedLandscape; // 屏幕方向缩放策略。
        [SerializeField] private Vector2 m_landscapeReferenceResolution =
            new Vector2(1920f, 1080f); // 横屏设计参考分辨率。
        [SerializeField, Range(0f, 1f)] private float m_landscapeMatchWidthOrHeight; // 横屏缩放匹配值。
        [SerializeField] private Vector2 m_portraitReferenceResolution =
            new Vector2(1080f, 1920f); // 竖屏设计参考分辨率。
        [SerializeField, Range(0f, 1f)] private float m_portraitMatchWidthOrHeight = 1f; // 竖屏缩放匹配值。
        [SerializeField, Min(0.01f)] private float m_planeDistance = 100f; // 所有界面共享的相机平面距离。
        [SerializeField] private int m_sortingLayerID; // 所有界面共享的 Unity 排序层。

        /// <summary>独立 Canvas 模式没有全局缩放器。</summary>
        [Obsolete("独立 Canvas 模式没有全局缩放器，请从目标 UIView 获取 CanvasScaler。")]
        public CanvasScaler Scaler => null;
        internal Canvas RootCanvas => null;
        internal Camera UICamera => m_uiCamera;
        internal RectTransform WindowRoot => m_windowRoot;
        internal Camera ForegroundCamera => m_foregroundCamera;
        internal bool IsInitialized => m_layers != null;
        internal UIScreenAdaptationSystem ScreenAdaptationSystem => m_screenAdaptationSystem;

        internal void Initialize(GameObject prefabRoot)
        {
            if (prefabRoot == null)
            {
                throw new ArgumentNullException(nameof(prefabRoot));
            }

            // 每个界面是独立根画布，分类容器不能形成祖先 Canvas。
            m_windowRoot = prefabRoot.transform.Find("WindowRoot") as RectTransform;
            if (m_windowRoot == null)
            {
                throw new InvalidOperationException("UIRoot 需要名为 WindowRoot 的 RectTransform 子节点。");
            }

            var cameraNode = prefabRoot.transform.Find("CameraRoot/UICamera");
            m_uiCamera = cameraNode == null ? null : cameraNode.GetComponent<Camera>();
            if (m_uiCamera == null || !m_uiCamera.gameObject.activeInHierarchy ||
                m_uiCamera.rect != new Rect(0f, 0f, 1f, 1f))
            {
                throw new InvalidOperationException("CameraRoot/UICamera 必须激活并使用全屏 Viewport Rect。");
            }

            var foregroundNode = prefabRoot.transform.Find("CameraRoot/BlurForegroundCamera");
            m_foregroundCamera = foregroundNode == null ? null : foregroundNode.GetComponent<Camera>();
            if (m_foregroundCamera != null)
            {
                m_foregroundCamera.enabled = false;
            }

            var eventSystems = prefabRoot.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true);
            if (eventSystems.Length != 1 || !eventSystems[0].transform.IsChildOf(m_windowRoot) ||
                eventSystems[0].GetComponent<BaseInputModule>() == null)
            {
                throw new InvalidOperationException("WindowRoot 下必须恰好有一个带输入模块的 EventSystem。");
            }

            var layers = new Dictionary<UILayer, UILayerRoot>();
            foreach (var layer in prefabRoot.GetComponentsInChildren<UILayerRoot>(true))
            {
                if (layer.transform.parent != m_windowRoot || !Enum.IsDefined(typeof(UILayer), layer.Layer) ||
                    layers.ContainsKey(layer.Layer) ||
                    (layer.Content != layer.transform && !layer.Content.IsChildOf(layer.transform)))
                {
                    throw new InvalidOperationException($"UIRoot 层节点 {layer.name} 的位置或层级配置无效。");
                }

                // 包括自定义 Content 路径在内，禁止界面嵌套在分类用 Canvas 下。
                for (var node = layer.Content.transform; node != null; node = node.parent)
                {
                    if (node.GetComponent<Canvas>() != null)
                    {
                        throw new InvalidOperationException(
                            $"请手动移除分类容器 {node.name} 上的 Canvas、CanvasScaler 和 GraphicRaycaster。");
                    }
                }

                layers.Add(layer.Layer, layer);
            }

            foreach (UILayer value in Enum.GetValues(typeof(UILayer)))
            {
                if (!layers.ContainsKey(value))
                {
                    throw new InvalidOperationException($"UIRoot 缺少 {value} 层。");
                }
            }

            m_layers = layers;
            m_screenAdaptationSystem = new UIScreenAdaptationSystem(HandleScreenAdaptationChanged);
            m_screenAdaptationSystem.Initialize();
            GameLoop.Register(this);
        }

        internal void ConfigureView(UIView view)
        {
            var canvas = view.GetComponent<Canvas>();
            if (canvas == null)
            {
                throw new InvalidOperationException($"界面 {view.name} 根节点缺少 Canvas，请添加后重试。");
            }

            // 缓存界面可能尚未激活，直接检查祖先组件，避免依赖未更新的 rootCanvas。
            for (var parent = view.transform.parent; parent != null; parent = parent.parent)
            {
                if (parent.GetComponent<Canvas>() != null)
                {
                    throw new InvalidOperationException($"界面 {view.name} 必须使用独立根 Canvas。");
                }
            }

            // 相机、深度与排序层由框架统一控制，界面自身保留内容布局。
            canvas.enabled = true;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = m_uiCamera;
            canvas.planeDistance = m_planeDistance;
            canvas.sortingLayerID = m_sortingLayerID;
            canvas.overrideSorting = true;
            canvas.targetDisplay = m_uiCamera.targetDisplay;
            if (view.GetComponent<GraphicRaycaster>() == null)
            {
                view.gameObject.AddComponent<GraphicRaycaster>();
            }

            if (!m_canvases.Contains(canvas))
            {
                m_canvases.Add(canvas);
            }

            SetTreeLayer(view.transform, LayerMask.NameToLayer("UI"));
            ApplyCanvasLayout(canvas);
            Canvas.ForceUpdateCanvases();
        }

        internal void UnregisterView(UIView view)
        {
            if (view != null)
            {
                m_canvases.Remove(view.GetComponent<Canvas>());
            }
        }

        internal void ValidateForegroundCamera()
        {
            if (m_foregroundCamera == null || m_foregroundCamera == m_uiCamera ||
                !m_foregroundCamera.gameObject.activeInHierarchy)
            {
                throw new InvalidOperationException(
                    "UI 模糊需要独立的 CameraRoot/BlurForegroundCamera，其 GameObject 必须激活。");
            }
        }

        internal RectTransform GetLayer(UILayer layer)
        {
            if (m_layers == null)
            {
                throw new InvalidOperationException("UIRoot 尚未初始化。");
            }

            return m_layers[layer].Content;
        }

        internal Canvas GetLayerCanvas(UILayer layer)
        {
            return null;
        }

        internal void RegisterScreenAdaptationTarget(IUIScreenAdaptationTarget target)
        {
            if (m_screenAdaptationSystem == null)
            {
                throw new InvalidOperationException("UIRoot 屏幕适配系统尚未初始化。");
            }

            m_screenAdaptationSystem.Register(target);
        }

        internal void UnregisterScreenAdaptationTarget(IUIScreenAdaptationTarget target)
        {
            m_screenAdaptationSystem?.Unregister(target);
        }

        private static void SetTreeLayer(Transform node, int layer)
        {
            // 普通相机只渲染 UI Layer，动态内容的首帧同样需要正确归属。
            node.gameObject.layer = layer;
            for (var index = 0; index < node.childCount; index++)
            {
                SetTreeLayer(node.GetChild(index), layer);
            }
        }

        private void ApplyCanvasLayout(Canvas canvas)
        {
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            }

            // 每个独立 Canvas 使用相同参考分辨率，修改 UIRoot 即可统一更新。
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.referenceResolution = m_isLandscape
                ? m_landscapeReferenceResolution : m_portraitReferenceResolution;
            scaler.matchWidthOrHeight = m_isLandscape
                ? m_landscapeMatchWidthOrHeight : m_portraitMatchWidthOrHeight;
            scaler.enabled = true;

            // CanvasScaler 在下一帧才处理新参数，首帧先使用相同算法同步实际比例。
            var screenSize = canvas.renderingDisplaySize;
            var referenceResolution = scaler.referenceResolution;
            if (screenSize.x > 0f && screenSize.y > 0f &&
                referenceResolution.x > 0f && referenceResolution.y > 0f)
            {
                var logWidth = Mathf.Log(screenSize.x / referenceResolution.x, 2f);
                var logHeight = Mathf.Log(screenSize.y / referenceResolution.y, 2f);
                canvas.scaleFactor = Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, scaler.matchWidthOrHeight));
                canvas.referencePixelsPerUnit = scaler.referencePixelsPerUnit;
            }
        }

        private void HandleScreenAdaptationChanged(UIScreenAdaptationSnapshot snapshot)
        {
            m_isLandscape = m_orientationMode == EUIScreenOrientationMode.FixedLandscape ||
                (m_orientationMode == EUIScreenOrientationMode.AutoRotate &&
                    snapshot.ScreenWidth >= snapshot.ScreenHeight);
            ApplyRegisteredLayouts();
        }

        private void ApplyRegisteredLayouts()
        {
            for (var index = m_canvases.Count - 1; index >= 0; index--)
            {
                if (m_canvases[index] == null)
                {
                    m_canvases.RemoveAt(index);
                    continue;
                }

                ApplyCanvasLayout(m_canvases[index]);
            }

            Canvas.ForceUpdateCanvases();
        }

        /// <summary>在主线程应用 Inspector 修改后的统一适配参数。</summary>
        /// <param name="deltaTime">受缩放影响的帧间隔。</param>
        /// <param name="unscaledDeltaTime">不受缩放影响的帧间隔。</param>
        public void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (!m_layoutRefreshPending || m_screenAdaptationSystem == null ||
                !m_screenAdaptationSystem.TryGetSnapshot(out var snapshot))
            {
                return;
            }

            m_layoutRefreshPending = false;
            HandleScreenAdaptationChanged(snapshot);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // 校验回调可能来自加载线程，只记录刷新请求，不直接修改 Canvas。
            m_layoutRefreshPending = true;
        }
#endif

        private void OnDestroy()
        {
            GameLoop.Unregister(this);
            m_screenAdaptationSystem?.Dispose();
            m_screenAdaptationSystem = null;
            m_canvases.Clear();
            m_layers = null;
        }
    }
}