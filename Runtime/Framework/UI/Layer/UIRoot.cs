using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AlloyFramework.UI
{
    [DisallowMultipleComponent]
    public sealed class UIRoot : MonoBehaviour
    {
        private Dictionary<UILayer, UILayerRoot> m_layers;
        private CanvasScaler m_scaler;
        private Canvas m_rootCanvas; // WindowRoot 的主画布。
        private RectTransform m_windowRoot; // 全屏 UI 内容根节点。
        private Canvas m_foregroundCanvas; // 与背景画布平级的前景根画布。
        private CanvasScaler m_foregroundScaler; // 跟随背景参考分辨率的前景缩放器。
        private Camera m_foregroundCamera; // 预制体中供模糊前景复用的相机。
        private UIScreenAdaptationSystem m_screenAdaptationSystem; // 全局屏幕适配快照与目标注册系统。
        [SerializeField] private EUIScreenOrientationMode m_orientationMode =
            EUIScreenOrientationMode.FixedLandscape; // 屏幕方向缩放策略。
        [SerializeField] private Vector2 m_landscapeReferenceResolution =
            new Vector2(1920f, 1080f); // 横屏设计参考分辨率。
        [SerializeField, Range(0f, 1f)] private float m_landscapeMatchWidthOrHeight; // 横屏缩放匹配值。
        [SerializeField] private Vector2 m_portraitReferenceResolution =
            new Vector2(1080f, 1920f); // 竖屏设计参考分辨率。
        [SerializeField, Range(0f, 1f)] private float m_portraitMatchWidthOrHeight = 1f; // 竖屏缩放匹配值。

        public CanvasScaler Scaler => m_scaler;
        internal Canvas RootCanvas => m_rootCanvas;
        internal RectTransform WindowRoot => m_windowRoot;
        internal Canvas ForegroundCanvas => m_foregroundCanvas;
        internal Camera ForegroundCamera => m_foregroundCamera;
        internal bool IsInitialized => m_layers != null;
        internal UIScreenAdaptationSystem ScreenAdaptationSystem => m_screenAdaptationSystem;

        internal void Initialize(GameObject prefabRoot)
        {
            if (prefabRoot == null) throw new ArgumentNullException(nameof(prefabRoot));
            var windowRoot = prefabRoot.transform.Find("WindowRoot");
            if (windowRoot == null)
                throw new InvalidOperationException("UIRoot needs a direct child named WindowRoot.");
            var canvas = windowRoot.GetComponent<Canvas>();
            if (canvas == null)
                throw new InvalidOperationException("WindowRoot needs a Canvas.");
            m_rootCanvas = canvas;
            m_windowRoot = windowRoot as RectTransform;
            m_scaler = windowRoot.GetComponent<CanvasScaler>();
            if (m_scaler == null)
                throw new InvalidOperationException("WindowRoot needs a CanvasScaler.");
            if (canvas.renderMode == RenderMode.ScreenSpaceCamera)
            {
                var camera = canvas.worldCamera;
                if (camera == null)
                    throw new InvalidOperationException(
                        "WindowRoot Screen Space - Camera canvas has no Render Camera.");
                if (camera.rect != new Rect(0f, 0f, 1f, 1f))
                    throw new InvalidOperationException(
                        "WindowRoot UI Camera must use a full-screen Viewport Rect.");
            }
            // 前景相机由预制体管理，非模糊期间不参与渲染。
            var foregroundCameraNode = prefabRoot.transform.Find("CameraRoot/BlurForegroundCamera");
            m_foregroundCamera = foregroundCameraNode == null
                ? null : foregroundCameraNode.GetComponent<Camera>();
            if (m_foregroundCamera != null)
            {
                m_foregroundCamera.enabled = false;
            }

            var foregroundRoot = prefabRoot.transform.Find("ForegroundRoot");
            m_foregroundCanvas = foregroundRoot == null ? null : foregroundRoot.GetComponent<Canvas>();
            m_foregroundScaler = foregroundRoot == null ? null : foregroundRoot.GetComponent<CanvasScaler>();

            var eventSystems =
                prefabRoot.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true);
            if (eventSystems.Length != 1 ||
                !eventSystems[0].transform.IsChildOf(windowRoot) ||
                eventSystems[0].GetComponent<BaseInputModule>() == null)
                throw new InvalidOperationException("WindowRoot needs exactly one EventSystem with an input module.");

            var found = prefabRoot.GetComponentsInChildren<UILayerRoot>(true);
            var layers = new Dictionary<UILayer, UILayerRoot>();
            foreach (var layer in found)
            {
                if (layer.transform.parent != windowRoot)
                    throw new InvalidOperationException(
                        $"UIRoot layer {layer.name} must be a direct child of WindowRoot.");
                if (!Enum.IsDefined(typeof(UILayer), layer.Layer))
                    throw new InvalidOperationException($"UIRoot has an unknown layer value on {layer.name}.");
                if (layer.GetComponent<GraphicRaycaster>() == null)
                    throw new InvalidOperationException($"UIRoot layer {layer.Layer} needs a GraphicRaycaster.");
                if (layers.ContainsKey(layer.Layer))
                    throw new InvalidOperationException($"UIRoot has duplicate {layer.Layer} layers.");
                layers.Add(layer.Layer, layer);
                layer.Configure();
            }

            foreach (UILayer value in Enum.GetValues(typeof(UILayer)))
                if (!layers.ContainsKey(value))
                    throw new InvalidOperationException($"UIRoot is missing the {value} layer.");
            m_layers = layers;
            m_screenAdaptationSystem = new UIScreenAdaptationSystem(HandleScreenAdaptationChanged);
            m_screenAdaptationSystem.Initialize();
        }

        internal void SynchronizeForegroundCanvas()
        {
            // 两个根画布必须独立，嵌套 Canvas 无法只靠 Layer 切换渲染相机。
            if (m_foregroundCanvas == null || m_foregroundScaler == null || m_foregroundCamera == null ||
                !(m_foregroundCanvas.transform is RectTransform) ||
                m_foregroundCanvas.transform.parent != m_windowRoot.parent ||
                m_foregroundCanvas.rootCanvas != m_foregroundCanvas ||
                !m_foregroundCanvas.gameObject.activeInHierarchy || !m_foregroundCamera.gameObject.activeInHierarchy)
            {
                throw new InvalidOperationException(
                    "UI 模糊需要 UIRoot/ForegroundRoot 上的 Canvas 和 CanvasScaler，" +
                    "以及 CameraRoot/BlurForegroundCamera；两个根节点及相机对象必须激活。");
            }

            // 保留 Screen Space - Camera 适配，前景配置统一跟随 WindowRoot。
            m_foregroundCanvas.enabled = true;
            m_foregroundCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            m_foregroundCanvas.worldCamera = m_foregroundCamera;
            m_foregroundCanvas.planeDistance = m_rootCanvas.planeDistance;
            m_foregroundCanvas.sortingLayerID = m_rootCanvas.sortingLayerID;
            m_foregroundCanvas.pixelPerfect = m_rootCanvas.pixelPerfect;
            m_foregroundCanvas.additionalShaderChannels = m_rootCanvas.additionalShaderChannels;
            m_foregroundCanvas.targetDisplay = m_rootCanvas.targetDisplay;
            ((RectTransform)m_foregroundCanvas.transform).pivot = m_windowRoot.pivot;
            m_foregroundScaler.enabled = m_scaler.enabled;
            m_foregroundScaler.uiScaleMode = m_scaler.uiScaleMode;
            m_foregroundScaler.screenMatchMode = m_scaler.screenMatchMode;
            m_foregroundScaler.referenceResolution = m_scaler.referenceResolution;
            m_foregroundScaler.matchWidthOrHeight = m_scaler.matchWidthOrHeight;
            m_foregroundScaler.scaleFactor = m_scaler.scaleFactor;
            m_foregroundScaler.referencePixelsPerUnit = m_scaler.referencePixelsPerUnit;
            m_foregroundCanvas.scaleFactor = m_rootCanvas.scaleFactor;
            m_foregroundCanvas.referencePixelsPerUnit = m_rootCanvas.referencePixelsPerUnit;
        }

        private void OnDestroy()
        {
            m_screenAdaptationSystem?.Dispose();
            m_screenAdaptationSystem = null;
            m_layers = null;
        }

        internal RectTransform GetLayer(UILayer layer)
        {
            if (m_layers == null)
                throw new InvalidOperationException("UIRoot is not initialized.");
            return m_layers[layer].Content;
        }

        internal Canvas GetLayerCanvas(UILayer layer)
        {
            return m_layers[layer].GetComponent<Canvas>();
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

        private void ApplyOrientationLayout(bool isLandscape)
        {
            if (m_scaler == null)
            {
                m_scaler = GetComponentInChildren<CanvasScaler>(true);
            }

            if (m_scaler == null)
            {
                return;
            }

            var referenceResolution = isLandscape
                ? m_landscapeReferenceResolution
                : m_portraitReferenceResolution;
            var matchWidthOrHeight = isLandscape
                ? m_landscapeMatchWidthOrHeight
                : m_portraitMatchWidthOrHeight;
            m_scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            m_scaler.referenceResolution = referenceResolution;
            m_scaler.matchWidthOrHeight = matchWidthOrHeight;
            Canvas.ForceUpdateCanvases();
        }

        private void HandleScreenAdaptationChanged(UIScreenAdaptationSnapshot snapshot)
        {
            if (m_orientationMode == EUIScreenOrientationMode.FixedLandscape)
            {
                ApplyOrientationLayout(true);
                return;
            }

            if (m_orientationMode == EUIScreenOrientationMode.FixedPortrait)
            {
                ApplyOrientationLayout(false);
                return;
            }

            ApplyOrientationLayout(snapshot.ScreenWidth >= snapshot.ScreenHeight);
        }
    }
}
