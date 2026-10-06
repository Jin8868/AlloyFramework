using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace AlloyFramework.UI
{
    internal sealed class UIBlurCoordinator : ILateUpdateable, IDisposable
    {
        private static readonly UILayer[] m_layers = (UILayer[])Enum.GetValues(typeof(UILayer)); // 固定层级快照。
        private static readonly IComparer<UIEntry> m_entryComparer =
            Comparer<UIEntry>.Create(Compare); // 缓存排序器，不创建运行时闭包。
        private readonly List<UIEntry> m_orderedEntries = new List<UIEntry>(); // 复用的逻辑排序缓冲。
        private readonly UIRoot m_root; // 两个独立根画布所属的框架根节点。
        private readonly List<UIEntry> m_entries; // 由 UIManager 持有的实例列表。
        private readonly IUIBlurCameraBinding m_cameraBinding; // 持久化 UI 相机与当前场景相机的连接。
        private readonly Dictionary<Transform, int> m_originalLayers = new Dictionary<Transform, int>(); // 原 Layer。
        private readonly Dictionary<UIEntry, CanvasState> m_canvases =
            new Dictionary<UIEntry, CanvasState>(); // 界面画布配置及原层逻辑位置。
        private readonly Dictionary<UILayer, RectTransform> m_foregroundLayers =
            new Dictionary<UILayer, RectTransform>(); // 前景画布下复用的轻量层容器。
        private readonly Dictionary<RectTransform, RectTransform> m_foregroundContainers =
            new Dictionary<RectTransform, RectTransform>(); // 镜像原层及其 Content 的完整布局路径。
        private readonly List<UIEntry> m_removedEntries = new List<UIEntry>(); // 失效实例的清理缓冲。
        private readonly Dictionary<Canvas, int> m_layerOrders = new Dictionary<Canvas, int>(); // 层 Canvas 原排序。
        private readonly List<Transform> m_destroyedNodes = new List<Transform>(); // 已销毁动态 Item 的清理缓冲。
        private UIBlurSettings m_settings = new UIBlurSettings(); // 已验证的配置副本。
        private IUIBlurRenderer m_renderer; // 当前活跃的渲染后端。
        private UIEntry m_owner; // 当前最高的模糊边界。
        private RawImage m_image; // 全屏模糊纹理显示节点，不拦截输入。
        private Canvas m_imageCanvas; // 独立的模糊背景排序画布。
        private int m_backgroundLayer; // 背景 Unity Layer。
        private int m_foregroundLayer; // 前景 Unity Layer。
        private bool m_routingChanged; // 本帧前后景归属改变时让静态模式重新捕获。
        private Exception m_error; // 本次渲染会话的同步错误。

        internal UIBlurCoordinator(UIRoot root, List<UIEntry> entries)
        {
            m_root = root;
            m_entries = entries;
            m_cameraBinding = UIBlurRendererProvider.BindCamera(root.RootCanvas.worldCamera);
            GameLoop.Register(this);
        }

        internal void Configure(UIBlurSettings settings)
        {
            var validated = settings.CopyValidated();
            if (m_renderer != null && (validated.BackgroundLayerName != m_settings.BackgroundLayerName ||
                validated.ForegroundLayerName != m_settings.ForegroundLayerName))
            {
                throw new InvalidOperationException("有模糊界面打开时不能更改前景或背景 Layer。");
            }

            m_settings = validated;
            m_renderer?.Configure(validated);
        }

        internal async UniTask PrepareAsync(UIEntry entry, CancellationToken cancellationToken)
        {
            if (entry.Definition.BackgroundMode != UIBackgroundMode.Blur)
            {
                return;
            }

            // 准备期间也参与边界选择，保证首帧捕获不包含本界面。
            entry.BlurRequested = true;
            Refresh();
            if (m_error != null)
            {
                throw new InvalidOperationException("UI 模糊会话创建失败。", m_error);
            }

            await m_renderer.WaitForCaptureAsync(cancellationToken);
            // 等待 Canvas 批次使用新纹理后再开始前景开场动画。
            await UniTask.NextFrame(cancellationToken);
        }

        internal void Release(UIEntry entry)
        {
            entry.BlurRequested = false;
            if (m_canvases.TryGetValue(entry, out var state))
            {
                state.Restore();
                m_canvases.Remove(entry);
                RestoreLayerOrder(entry.Definition.Layer);
            }

            if (entry.View != null)
            {
                RestoreTreeLayers(entry.View.transform);
            }
        }

        /// <summary>渲染前统一更新画布归属、排序及动态 Item 的 Layer。</summary>
        /// <param name="deltaTime">受缩放影响的帧间隔。</param>
        /// <param name="unscaledDeltaTime">不受缩放影响的帧间隔。</param>
        public void OnLateUpdate(float deltaTime, float unscaledDeltaTime)
        {
            try
            {
                Refresh();
            }
            catch (Exception exception)
            {
                if (m_error == null)
                {
                    m_error = exception;
                    AlloyDebug.Error(exception);
                    StopSession();
                }
            }
        }

        /// <summary>恢复 Layer、归属和排序，释放纹理及相机绑定。</summary>
        public void Dispose()
        {
            GameLoop.Unregister(this);
            StopSession();
            m_cameraBinding?.Dispose();
            if (m_image != null)
            {
                UnityEngine.Object.Destroy(m_image.gameObject);
                m_image = null;
                m_imageCanvas = null;
            }

            foreach (var layerRoot in m_foregroundContainers.Values)
            {
                if (layerRoot != null && layerRoot.parent == m_root.ForegroundCanvas.transform)
                {
                    UnityEngine.Object.Destroy(layerRoot.gameObject);
                }
            }

            m_foregroundContainers.Clear();
            m_foregroundLayers.Clear();
        }

        private void Refresh()
        {
            UIEntry owner = null;
            foreach (var entry in m_entries)
            {
                if (entry.BlurRequested && entry.View != null && entry.View.gameObject.activeInHierarchy &&
                    entry.State != UIState.Disposed && entry.State != UIState.Cached &&
                    (owner == null || Compare(entry, owner) > 0))
                {
                    owner = entry;
                }
            }

            if (owner == null)
            {
                StopSession();
                m_error = null;
                return;
            }

            if (m_error != null)
            {
                return;
            }

            if (m_renderer == null)
            {
                m_cameraBinding?.Synchronize();
                StartSession();
            }

            var changedOwner = !ReferenceEquals(m_owner, owner);
            m_owner = owner;
            m_root.SynchronizeForegroundCanvas();
            UpdateCanvasRouting();
            m_renderer.Synchronize();
            if (changedOwner || m_routingChanged)
            {
                m_renderer.Refresh();
            }
        }

        private void StartSession()
        {
            var camera = m_root.RootCanvas.worldCamera;
            m_backgroundLayer = LayerMask.NameToLayer(m_settings.BackgroundLayerName);
            m_foregroundLayer = LayerMask.NameToLayer(m_settings.ForegroundLayerName);
            if (m_root.RootCanvas.renderMode != RenderMode.ScreenSpaceCamera || camera == null ||
                m_backgroundLayer < 0 || m_foregroundLayer < 0 || m_backgroundLayer == m_foregroundLayer)
            {
                throw new InvalidOperationException(
                    "UI 模糊需要 Screen Space - Camera 和两个已创建的专用 Layer：" +
                    $"{m_settings.BackgroundLayerName}、{m_settings.ForegroundLayerName}。");
            }

            m_root.SynchronizeForegroundCanvas();
            // 整个框架生命周期只创建一个背景显示节点，关闭后隐藏并复用。
            if (m_image == null)
            {
                // 模糊纹理必须属于前景相机的独立根画布，不能留在背景捕获画布下。
                var imageObject = new GameObject("[AlloyFramework] BlurBackground", typeof(RectTransform));
                imageObject.transform.SetParent(m_root.ForegroundCanvas.transform, false);
                var rectTransform = (RectTransform)imageObject.transform;
                rectTransform.anchorMin = Vector2.zero;
                rectTransform.anchorMax = Vector2.one;
                rectTransform.offsetMin = Vector2.zero;
                rectTransform.offsetMax = Vector2.zero;
                imageObject.layer = m_foregroundLayer;
                m_imageCanvas = imageObject.AddComponent<Canvas>();
                m_imageCanvas.overrideSorting = true;
                m_imageCanvas.worldCamera = m_root.ForegroundCamera;
                m_image = imageObject.AddComponent<RawImage>();
                m_image.raycastTarget = false;
                m_image.enabled = false;
            }

            m_image.gameObject.layer = m_foregroundLayer;
            m_image.gameObject.SetActive(true);
            m_image.enabled = false;
            m_image.texture = null;
            try
            {
                m_renderer = UIBlurRendererProvider.Create(camera, m_image, m_settings);
                // 前景相机刚启用时立即更新其根画布，避免沿用禁用期间的零尺寸。
                Canvas.ForceUpdateCanvases();
            }
            catch
            {
                StopSession();
                throw;
            }
        }

        private void UpdateCanvasRouting()
        {
            m_routingChanged = false;
            m_removedEntries.Clear();
            foreach (var pair in m_canvases)
            {
                if (!m_entries.Contains(pair.Key) || pair.Key.View == null)
                {
                    pair.Value.Restore();
                    m_removedEntries.Add(pair.Key);
                }
            }

            foreach (var entry in m_removedEntries)
            {
                m_canvases.Remove(entry);
            }

            // 原层布局跟随现有适配流程，前景层容器只镜像布局，不重复注册适配任务。
            foreach (var layer in m_layers)
            {
                var layerCanvas = m_root.GetLayerCanvas(layer);
                if (!m_layerOrders.ContainsKey(layerCanvas))
                {
                    if (layerCanvas.sortingLayerID != m_root.RootCanvas.sortingLayerID)
                    {
                        throw new InvalidOperationException("UI 模糊要求八层 Canvas 使用相同的 Sorting Layer。");
                    }

                    m_layerOrders.Add(layerCanvas, layerCanvas.sortingOrder);
                }

                layerCanvas.sortingOrder = (int)layer / 100 * 1024;
            }

            foreach (var pair in m_foregroundContainers)
            {
                SynchronizeContainer(pair.Key, pair.Value);
            }

            // 一次排序同时服务相机边界和 Canvas 排序，不创建占位节点。
            m_orderedEntries.Clear();
            foreach (var entry in m_entries)
            {
                if (entry.View != null)
                {
                    m_orderedEntries.Add(entry);
                }
            }

            m_orderedEntries.Sort(m_entryComparer);
            var currentLayer = (UILayer)(-1);
            var layerIndex = 0;
            foreach (var entry in m_orderedEntries)
            {
                if (entry.View == null)
                {
                    continue;
                }

                if (!m_canvases.TryGetValue(entry, out var state))
                {
                    state = new CanvasState(entry);
                    m_canvases.Add(entry, state);
                }

                var foreground = entry.State != UIState.Cached && entry.State != UIState.Disposed &&
                    Compare(entry, m_owner) >= 0;
                m_routingChanged |= foreground != state.IsForeground;
                if (foreground)
                {
                    state.MoveToForeground(GetForegroundLayer(entry.Definition.Layer));
                }
                else
                {
                    state.RestoreParent();
                }

                var layerCanvas = m_root.GetLayerCanvas(entry.Definition.Layer);
                state.Canvas.overrideSorting = true;
                state.Canvas.sortingLayerID = layerCanvas.sortingLayerID;
                if (currentLayer != entry.Definition.Layer)
                {
                    currentLayer = entry.Definition.Layer;
                    layerIndex = 0;
                }

                if (layerIndex >= 500)
                {
                    throw new InvalidOperationException("模糊期间单个 UI 层最多支持 500 个管理实例。");
                }

                state.Canvas.sortingOrder = (int)currentLayer / 100 * 1024 + layerIndex * 2 + 2;
                layerIndex++;
                state.Canvas.worldCamera = foreground ? m_root.ForegroundCamera : m_root.RootCanvas.worldCamera;
            }

            m_imageCanvas.sortingLayerID = m_root.GetLayerCanvas(m_owner.Definition.Layer).sortingLayerID;
            m_imageCanvas.sortingOrder = m_canvases[m_owner].Canvas.sortingOrder - 1;
            RouteTree(m_root.WindowRoot, m_backgroundLayer);
            RouteTree(m_root.ForegroundCanvas.transform, m_foregroundLayer);

            // 长时间打开界面时，不保留已销毁 Item 的原始 Layer 记录。
            m_destroyedNodes.Clear();
            foreach (var node in m_originalLayers.Keys)
            {
                if (node == null)
                {
                    m_destroyedNodes.Add(node);
                }
            }

            foreach (var node in m_destroyedNodes)
            {
                m_originalLayers.Remove(node);
            }
        }

        private RectTransform GetForegroundLayer(UILayer layer)
        {
            var original = m_root.GetLayer(layer);
            if (CanUseForegroundRoot(original))
            {
                return (RectTransform)m_root.ForegroundCanvas.transform;
            }

            if (!m_foregroundLayers.TryGetValue(layer, out var layerRoot))
            {
                layerRoot = GetForegroundContainer(original);
                m_foregroundLayers.Add(layer, layerRoot);
            }

            return layerRoot;
        }

        private bool CanUseForegroundRoot(RectTransform original)
        {
            // 普通全屏层与根画布共享坐标系，直接复用 ForegroundRoot。
            return original.parent == m_root.WindowRoot &&
                original.anchorMin == Vector2.zero && original.anchorMax == Vector2.one &&
                original.offsetMin == Vector2.zero && original.offsetMax == Vector2.zero &&
                original.localScale == Vector3.one && original.localRotation == Quaternion.identity &&
                Mathf.Approximately(original.localPosition.z, 0f) &&
                original.gameObject.activeSelf && original.GetComponent<CanvasGroup>() == null;
        }

        private RectTransform GetForegroundContainer(RectTransform original)
        {
            if (original == m_root.WindowRoot)
            {
                return (RectTransform)m_root.ForegroundCanvas.transform;
            }

            if (original == null || !original.IsChildOf(m_root.WindowRoot) ||
                !(original.parent is RectTransform originalParent))
            {
                throw new InvalidOperationException("UI 层的 Content 必须位于 WindowRoot 下的 RectTransform 路径中。");
            }

            if (m_foregroundContainers.TryGetValue(original, out var existing))
            {
                return existing;
            }

            // 镜像完整祖先路径，使自定义 Content、SafeArea 和视口矩形仍保持相同的坐标系。
            var parent = GetForegroundContainer(originalParent);
            var containerObject = new GameObject($"[BlurLayer] {original.name}", typeof(RectTransform));
            var container = (RectTransform)containerObject.transform;
            container.SetParent(parent, false);
            m_foregroundContainers.Add(original, container);
            SynchronizeContainer(original, container);
            return container;
        }

        private static void SynchronizeContainer(RectTransform original, RectTransform mirror)
        {
            CopyRectTransform(original, mirror);
            mirror.gameObject.SetActive(original.gameObject.activeSelf);
            var originalGroup = original.GetComponent<CanvasGroup>();
            var mirrorGroup = mirror.GetComponent<CanvasGroup>();
            if (originalGroup != null)
            {
                if (mirrorGroup == null)
                {
                    mirrorGroup = mirror.gameObject.AddComponent<CanvasGroup>();
                }

                mirrorGroup.enabled = originalGroup.enabled;
                mirrorGroup.alpha = originalGroup.alpha;
                mirrorGroup.interactable = originalGroup.interactable;
                mirrorGroup.blocksRaycasts = originalGroup.blocksRaycasts;
                mirrorGroup.ignoreParentGroups = originalGroup.ignoreParentGroups;
            }
            else if (mirrorGroup != null)
            {
                mirrorGroup.enabled = false;
                mirrorGroup.alpha = 1f;
                mirrorGroup.interactable = true;
                mirrorGroup.blocksRaycasts = true;
                mirrorGroup.ignoreParentGroups = false;
            }
        }

        private static void CopyRectTransform(RectTransform source, RectTransform target)
        {
            target.anchorMin = source.anchorMin;
            target.anchorMax = source.anchorMax;
            target.pivot = source.pivot;
            target.sizeDelta = source.sizeDelta;
            target.anchoredPosition3D = source.anchoredPosition3D;
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;
        }

        private void RouteTree(Transform node, int layer)
        {
            if (!m_originalLayers.ContainsKey(node))
            {
                m_originalLayers.Add(node, node.gameObject.layer);
            }

            if (node.gameObject.layer != layer)
            {
                node.gameObject.layer = layer;
            }

            for (var index = 0; index < node.childCount; index++)
            {
                RouteTree(node.GetChild(index), layer);
            }
        }

        private void RestoreTreeLayers(Transform node)
        {
            if (m_originalLayers.TryGetValue(node, out var layer))
            {
                node.gameObject.layer = layer;
                m_originalLayers.Remove(node);
            }

            for (var index = 0; index < node.childCount; index++)
            {
                RestoreTreeLayers(node.GetChild(index));
            }
        }

        private void StopSession()
        {
            m_renderer?.Dispose();
            m_renderer = null;
            if (m_image != null)
            {
                m_image.enabled = false;
                m_image.texture = null;
                m_image.gameObject.SetActive(false);
            }
            m_owner = null;
            var hadRouting = m_canvases.Count > 0;
            foreach (var state in m_canvases.Values)
            {
                state.Restore();
            }

            if (hadRouting)
            {
                foreach (var layer in m_layers)
                {
                    RestoreLayerOrder(layer);
                }
            }

            foreach (var pair in m_originalLayers)
            {
                if (pair.Key != null)
                {
                    pair.Key.gameObject.layer = pair.Value;
                }
            }

            foreach (var pair in m_layerOrders)
            {
                if (pair.Key != null)
                {
                    pair.Key.sortingOrder = pair.Value;
                }
            }

            m_layerOrders.Clear();
            m_originalLayers.Clear();
            m_canvases.Clear();
            m_orderedEntries.Clear();
        }

        private static int Compare(UIEntry left, UIEntry right)
        {
            var layerComparison = ((int)left.Definition.Layer).CompareTo((int)right.Definition.Layer);
            return layerComparison != 0 ? layerComparison :
                left.DisplayOrder.CompareTo(right.DisplayOrder);
        }

        private void RestoreLayerOrder(UILayer layer)
        {
            if (m_root == null || !m_root.IsInitialized)
            {
                return;
            }

            // 返回原层后按逻辑顺序还原实际 sibling，无需保留 GameObject 槽位。
            var parent = m_root.GetLayer(layer);
            m_orderedEntries.Clear();
            foreach (var entry in m_entries)
            {
                if (entry.View != null && entry.Definition.Layer == layer && entry.View.transform.parent == parent)
                {
                    m_orderedEntries.Add(entry);
                }
            }

            m_orderedEntries.Sort(m_entryComparer);
            foreach (var entry in m_orderedEntries)
            {
                entry.View.transform.SetAsLastSibling();
            }
        }

        private sealed class CanvasState
        {
            private readonly UIEntry m_entry; // 路由及逻辑占位所属实例。
            private readonly Canvas m_canvas; // 实例独立的批次画布。
            private readonly Transform m_parent; // 实例在原层的父节点。
            private readonly bool m_overrideSorting; // 原始排序覆盖开关。
            private readonly int m_sortingLayer; // 原始排序层。
            private readonly int m_sortingOrder; // 原始排序值。
            private readonly Camera m_camera; // 原始事件相机。

            internal Canvas Canvas => m_canvas;
            internal bool IsForeground => m_entry.View != null && m_entry.View.transform.parent != m_parent;

            internal CanvasState(UIEntry entry)
            {
                m_entry = entry;
                var root = entry.View.gameObject;
                m_parent = root.transform.parent;
                m_canvas = root.GetComponent<Canvas>();
                if (m_canvas == null)
                {
                    m_canvas = root.AddComponent<Canvas>();
                }

                m_overrideSorting = m_canvas.overrideSorting;
                m_sortingLayer = m_canvas.sortingLayerID;
                m_sortingOrder = m_canvas.sortingOrder;
                m_camera = m_canvas.worldCamera;
                if (root.GetComponent<GraphicRaycaster>() == null)
                {
                    root.AddComponent<GraphicRaycaster>();
                }
            }

            internal void MoveToForeground(RectTransform parent)
            {
                var rectTransform = (RectTransform)m_entry.View.transform;
                if (rectTransform.parent != parent)
                {
                    MovePreservingLayout(rectTransform, parent);
                }
            }

            internal void RestoreParent()
            {
                if (IsForeground && m_parent != null)
                {
                    // 保留动画当前的局部状态，物理排序由协调器统一恢复。
                    MovePreservingLayout((RectTransform)m_entry.View.transform, m_parent);
                }
            }

            internal void Restore()
            {
                RestoreParent();
                if (m_canvas == null)
                {
                    return;
                }

                m_canvas.overrideSorting = m_overrideSorting;
                m_canvas.sortingLayerID = m_sortingLayer;
                m_canvas.sortingOrder = m_sortingOrder;
                m_canvas.worldCamera = m_camera;
                // 补充组件随 UI 复用并最终随 UI 销毁，避免延迟 Destroy 造成快速重开的悬空组件。
            }

            private static void MovePreservingLayout(RectTransform rectTransform, Transform parent)
            {
                var anchorMin = rectTransform.anchorMin;
                var anchorMax = rectTransform.anchorMax;
                var pivot = rectTransform.pivot;
                var sizeDelta = rectTransform.sizeDelta;
                var position = rectTransform.anchoredPosition3D;
                var rotation = rectTransform.localRotation;
                var scale = rectTransform.localScale;
                rectTransform.SetParent(parent, false);
                rectTransform.anchorMin = anchorMin;
                rectTransform.anchorMax = anchorMax;
                rectTransform.pivot = pivot;
                rectTransform.sizeDelta = sizeDelta;
                rectTransform.anchoredPosition3D = position;
                rectTransform.localRotation = rotation;
                rectTransform.localScale = scale;
            }
        }
    }
}
