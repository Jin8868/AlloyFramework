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
        private static readonly IComparer<UIEntry> m_entryComparer =
            Comparer<UIEntry>.Create(Compare); // 缓存排序器，不创建运行时闭包。
        private readonly List<UIEntry> m_orderedEntries = new List<UIEntry>(); // 复用的逻辑排序缓冲。
        private readonly UIRoot m_root; // 提供共享相机的框架根节点。
        private readonly List<UIEntry> m_entries; // 由 UIManager 持有的实例列表。
        private readonly IUIBlurCameraBinding m_cameraBinding; // UI 相机与当前场景相机的连接。
        private readonly Dictionary<Transform, int> m_originalLayers =
            new Dictionary<Transform, int>(); // 模糊路由前的 Unity Layer。
        private readonly List<Transform> m_destroyedNodes = new List<Transform>(); // 已销毁节点的清理缓冲。
        private UIBlurSettings m_settings = new UIBlurSettings(); // 已验证的配置副本。
        private IUIBlurRenderer m_renderer; // 当前活跃的渲染后端。
        private UIEntry m_owner; // 当前最高的模糊边界。
        private RawImage m_image; // UI 根节点内部的全屏背景，不拦截输入。
        private CanvasGroup m_imageGroup; // 捕获等待期间保持旧模糊连续显示。
        private int m_backgroundLayer; // 背景 Unity Layer。
        private int m_foregroundLayer; // 前景 Unity Layer。
        private bool m_routingChanged; // 本帧前后景归属是否变化。
        private bool m_refreshRequested; // 实例释放后需要刷新逻辑排序。
        private bool m_disposed; // 是否已释放协调器。
        private Exception m_error; // 当前模糊会话的同步错误。

        internal UIBlurCoordinator(UIRoot root, List<UIEntry> entries)
        {
            m_root = root;
            m_entries = entries;
            m_cameraBinding = UIBlurRendererProvider.BindCamera(root.UICamera);
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
                Refresh();
                return;
            }

            // 准备完毕后才参与模糊边界，隐藏的本界面不能进入源相机捕获。
            entry.BlurRequested = true;
            Refresh();
            if (m_error != null)
            {
                throw new InvalidOperationException("UI 模糊会话创建失败。", m_error);
            }

            await m_renderer.WaitForCaptureAsync(cancellationToken);
            await UniTask.NextFrame(cancellationToken);
        }

        internal void Release(UIEntry entry)
        {
            entry.BlurRequested = false;
            if (ReferenceEquals(entry, m_owner) || m_image != null && entry.View != null &&
                m_image.transform.parent == entry.View.transform)
            {
                // 先收回背景节点，防止其随业务界面销毁或留在缓存界面中。
                StopSession();
            }

            if (entry.View != null)
            {
                RestoreTreeLayers(entry.View.transform);
                var canvas = entry.View.GetComponent<Canvas>();
                if (canvas != null)
                {
                    canvas.worldCamera = m_root.UICamera;
                }
            }

            m_refreshRequested = true;
        }

        /// <summary>渲染前更新动态 Item 的 Layer 与最高模糊边界。</summary>
        /// <param name="deltaTime">受缩放影响的帧间隔。</param>
        /// <param name="unscaledDeltaTime">不受缩放影响的帧间隔。</param>
        public void OnLateUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (m_disposed || m_renderer == null && !m_refreshRequested)
            {
                return;
            }

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

        /// <summary>恢复相机与 Layer，释放模糊纹理、内部背景及场景相机绑定。</summary>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;
            GameLoop.Unregister(this);
            StopSession();
            m_cameraBinding?.Dispose();
            if (m_image != null)
            {
                UnityEngine.Object.Destroy(m_image.gameObject);
                m_image = null;
                m_imageGroup = null;
            }

            m_orderedEntries.Clear();
            m_destroyedNodes.Clear();
        }

        internal void Refresh()
        {
            if (m_disposed)
            {
                return;
            }

            m_refreshRequested = false;
            var owner = FindOwner();
            if (owner == null)
            {
                StopSession();
                UpdateCanvasRouting();
                m_error = null;
                return;
            }

            if (m_error != null)
            {
                throw new InvalidOperationException("UI 模糊会话已失败。", m_error);
            }

            var changedOwner = !ReferenceEquals(m_owner, owner);
            m_root.ValidateForegroundCamera();
            m_backgroundLayer = LayerMask.NameToLayer(m_settings.BackgroundLayerName);
            m_foregroundLayer = LayerMask.NameToLayer(m_settings.ForegroundLayerName);
            if (m_backgroundLayer < 0 || m_foregroundLayer < 0 || m_backgroundLayer == m_foregroundLayer)
            {
                throw new InvalidOperationException("UI 模糊需要两个不同且已创建的背景、前景 Layer。");
            }

            m_owner = owner;
            EnsureInternalBackground();
            // 先为根画布指定前景相机，渲染后端再从内部 RawImage 获取该相机。
            UpdateCanvasRouting();
            if (m_renderer == null)
            {
                StartSession();
            }

            m_renderer.Synchronize();
            if (changedOwner || m_routingChanged)
            {
                m_renderer.Refresh();
            }
        }

        private UIEntry FindOwner()
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

            return owner;
        }

        private void EnsureInternalBackground()
        {
            // 仅最高边界显示背景，复用一个 RawImage，不增加额外 Canvas 和分类容器。
            if (m_image == null)
            {
                var imageObject = new GameObject("[AlloyFramework] BlurBackground", typeof(RectTransform));
                m_image = imageObject.AddComponent<RawImage>();
                m_image.raycastTarget = false;
                m_image.enabled = false;
                m_imageGroup = imageObject.AddComponent<CanvasGroup>();
                m_imageGroup.interactable = false;
                m_imageGroup.blocksRaycasts = false;
            }

            var rectTransform = (RectTransform)m_image.transform;
            if (rectTransform.parent != m_owner.View.transform)
            {
                rectTransform.SetParent(m_owner.View.transform, false);
                rectTransform.anchorMin = Vector2.zero;
                rectTransform.anchorMax = Vector2.one;
                rectTransform.pivot = new Vector2(0.5f, 0.5f);
                rectTransform.offsetMin = Vector2.zero;
                rectTransform.offsetMax = Vector2.zero;
                rectTransform.localPosition = Vector3.zero;
                rectTransform.localRotation = Quaternion.identity;
                rectTransform.localScale = Vector3.one;
            }

            // 背景位于安全区容器之外，并先于界面自身内容绘制。
            rectTransform.SetAsFirstSibling();
            // 业务准备任务已经结束；仅捕获等待时独立显示背景，防止叠加弹窗短暂露出清晰下层。
            m_imageGroup.ignoreParentGroups = m_owner.State == UIState.Preparing;
            m_image.gameObject.SetActive(true);
        }

        private void StartSession()
        {
            m_cameraBinding?.Synchronize();
            m_image.enabled = false;
            m_image.texture = null;
            try
            {
                m_renderer = UIBlurRendererProvider.Create(m_root.UICamera, m_image, m_settings);
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
            m_orderedEntries.Clear();
            foreach (var entry in m_entries)
            {
                if (entry.View != null && entry.State != UIState.Disposed)
                {
                    m_orderedEntries.Add(entry);
                }
            }

            // 相同排序规则同时用于普通模式、相机边界与模糊模式。
            m_orderedEntries.Sort(m_entryComparer);
            var currentLayer = (UILayer)(-1);
            var layerIndex = 0;
            foreach (var entry in m_orderedEntries)
            {
                var canvas = entry.View.GetComponent<Canvas>();
                if (canvas == null)
                {
                    throw new InvalidOperationException($"界面 {entry.Definition.UIName} 根节点缺少 Canvas。");
                }

                if (currentLayer != entry.Definition.Layer)
                {
                    currentLayer = entry.Definition.Layer;
                    layerIndex = 0;
                }

                if (layerIndex >= 500)
                {
                    throw new InvalidOperationException("单个 UI 层最多支持 500 个管理实例。");
                }

                var foreground = m_owner != null && entry.State != UIState.Cached &&
                    Compare(entry, m_owner) >= 0;
                var camera = foreground ? m_root.ForegroundCamera : m_root.UICamera;
                m_routingChanged |= canvas.worldCamera != camera;
                if (canvas.worldCamera != camera)
                {
                    canvas.worldCamera = camera;
                }

                canvas.overrideSorting = true;
                canvas.sortingOrder = (int)currentLayer / 100 * 1024 + layerIndex * 2 + 2;
                layerIndex++;
                if (m_owner != null)
                {
                    RouteTree(entry.View.transform, foreground ? m_foregroundLayer : m_backgroundLayer);
                }
            }

            // 动态 Item 销毁后移除其记录，避免长时间模糊会话持有失效节点。
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
                // 空闲时挂回普通框架根节点，不再依赖 ForegroundRoot。
                if (m_image.transform.parent != m_root.transform)
                {
                    m_image.transform.SetParent(m_root.transform, false);
                }
            }

            m_owner = null;
            foreach (var entry in m_entries)
            {
                if (entry.View != null)
                {
                    var canvas = entry.View.GetComponent<Canvas>();
                    if (canvas != null && canvas.worldCamera != m_root.UICamera)
                    {
                        canvas.worldCamera = m_root.UICamera;
                    }
                }
            }

            foreach (var pair in m_originalLayers)
            {
                if (pair.Key != null)
                {
                    pair.Key.gameObject.layer = pair.Value;
                }
            }

            m_originalLayers.Clear();
        }

        private static int Compare(UIEntry left, UIEntry right)
        {
            var layerComparison = ((int)left.Definition.Layer).CompareTo((int)right.Definition.Layer);
            return layerComparison != 0 ? layerComparison : left.DisplayOrder.CompareTo(right.DisplayOrder);
        }
    }
}