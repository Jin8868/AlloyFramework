using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AlloyFramework.UI
{
    /// <summary>继承 ScrollRect 的固定尺寸、单轴虚拟列表。</summary>
    [DisallowMultipleComponent]
    public class UIInfiniteList : ScrollRect
    {
        [SerializeField] private UIListItem m_defaultPrefab; // Inspector 配置的默认模板，显式绑定可覆盖。
        [SerializeField] private Vector2 m_itemSize = new Vector2(100f, 100f); // 每项占据的固定尺寸。
        [SerializeField] private float m_spacing; // 主轴项间距。
        [SerializeField] private RectOffset m_padding = new RectOffset(); // 内容边距。
        [SerializeField] private float m_buffer = 100f; // 视口两端额外保留的距离。
        [SerializeField] private int m_maxInactiveItems = 32; // 空闲实例缓存上限。

        private readonly Dictionary<int, UIListItem> m_active = new Dictionary<int, UIListItem>(); // 索引映射。
        // 按模板分别缓存，实例始终留在 Content 下。
        private readonly Dictionary<UIListItem, Stack<UIListItem>> m_pool =
            new Dictionary<UIListItem, Stack<UIListItem>>();
        private readonly Dictionary<UIListItem, UIListItem> m_itemPrefabs =
            new Dictionary<UIListItem, UIListItem>(); // 实例所属模板。
        private int m_inactiveCount; // 合计空闲实例数。
        private bool m_layoutRequested; // 回调中请求的延迟重建。
        private readonly List<int> m_indices = new List<int>(); // 本批次目标索引。
        private readonly HashSet<int> m_indexSet = new HashSet<int>(); // 回收阶段的可见性查询。
        private readonly List<int> m_removed = new List<int>(); // 延迟移除字典中的离开项。
        private readonly List<UIListItem> m_items = new List<UIListItem>(); // 按布局查询顺序排列的活动项。
        private ReadOnlyCollection<UIListItem> m_readOnlyItems; // 不允许业务修改的活动集合。
        private UIListItem m_prefab; // 本轮绑定的模板。
        private IList m_data; // 由业务持有并通过 Refresh 通知变化的数据集合。
        private Vector2 m_viewportSize; // 上次构建布局时的视口尺寸。
        private float m_contentLength; // 逻辑内容在主轴上的总长度。
        private int m_count; // 最近一次提交的数据数量。
        private bool m_updating; // 禁止业务回调重入更新过程。
        private bool m_pendingRebuild; // 启用后需要重新绑定和布局。
        private bool m_failed; // 回调异常后暂停自动更新，等待显式刷新恢复。
        private bool m_animating; // 当前是否存在程序滚动动画。
        private float m_animationStart; // 动画起点。
        private float m_animationTarget; // 动画终点。
        private float m_animationDuration; // 动画总时长。
        private float m_animationElapsed; // 不受时间缩放影响的累计时间。

        /// <summary>本批次新实例全部创建并初始化后触发，仅在有新增实例时触发。</summary>
        public event Action<UIListUpdateContext> ItemsCreated;

        /// <summary>本批次所有目标项完成同步绑定后触发，不等待异步资源。</summary>
        public event Action<UIListUpdateContext> ItemsBound;

        /// <summary>本批次布局完成、提交显示前触发，不是逐帧 Canvas 绘制事件。</summary>
        public event Action<UIListUpdateContext> BeforeDisplay;

        /// <summary>本次显示更新完成后触发，空列表也会触发。</summary>
        public event Action<UIListUpdateContext> RefreshCompleted;

        /// <summary>当前活动项的只读实时视图，包含缓冲区内的项。</summary>
        public IReadOnlyList<UIListItem> ActiveItems
        {
            get
            {
                if (m_readOnlyItems == null) m_readOnlyItems = m_items.AsReadOnly();
                return m_readOnlyItems;
            }
        }

        /// <summary>固定项尺寸；设置后调用 Rebuild 应用。</summary>
        public Vector2 ItemSize { get => m_itemSize; set => m_itemSize = value; }

        /// <summary>主轴项间距；设置后调用 Rebuild 应用。</summary>
        public float Spacing { get => m_spacing; set => m_spacing = value; }

        /// <summary>内容边距；修改后调用 Rebuild 应用。</summary>
        public RectOffset Padding => m_padding;

        /// <summary>视口两端缓冲距离；设置后调用 Rebuild 应用。</summary>
        public float Buffer { get => m_buffer; set => m_buffer = value; }

        /// <summary>从顶部或左侧起算的主轴滚动距离。</summary>
        public float Position => content == null ? 0f :
            (vertical ? content.anchoredPosition.y : -content.anchoredPosition.x);

        /// <summary>最近一次读取的数据数量。</summary>
        protected int ItemCount => m_count;

        /// <summary>当前绑定的数据，仅供布局读取。</summary>
        protected IList BoundData => m_data;

        /// <summary>当前绑定的默认模板，供派生列表切换布局后重新绑定。</summary>
        protected UIListItem BoundPrefab => m_prefab;

        /// <summary>当前是否正在执行 Item 更新或清理，禁止布局切换重入。</summary>
        protected bool IsUpdatingItems => m_updating;

        /// <summary>本次布局重建原因。</summary>
        protected EUIListUpdateReason LayoutUpdateReason { get; private set; }

        /// <summary>滚动时是否更新已有项的姿态。</summary>
        protected virtual bool LayoutChangesWithScroll => false;

        /// <summary>重建时是否保持可见项相对位置。</summary>
        protected virtual bool PreserveLayoutAnchor => false;

        /// <summary>当前布局使用的视口尺寸。</summary>
        protected Vector2 ViewportSize => m_viewportSize;

        private float ViewportLength => vertical ? m_viewportSize.y : m_viewportSize.x;
        private float MaximumPosition => Mathf.Max(0f, m_contentLength - ViewportLength);
        private RectTransform EffectiveViewport => viewport != null ? viewport : (RectTransform)transform;

        /// <summary>使用 Inspector 配置的默认模板绑定数据，并回到起点。</summary>
        /// <param name="items">保留引用的数据集合；更改后调用 Refresh。</param>
        /// <exception cref="ArgumentNullException">数据集合为空。</exception>
        /// <exception cref="InvalidOperationException">未配置默认模板、配置无效或回调重入。</exception>
        public void BindData(IList items)
        {
            // 默认配置和本次显式绑定分离，避免运行时覆盖预制体的序列化引用。
            EnsureNotUpdating();
            if (m_defaultPrefab == null)
                throw new InvalidOperationException("请在 Inspector 配置默认 Item 预制体，或调用 Bind(prefab, items)。");
            Bind(m_defaultPrefab, items);
        }

        /// <summary>绑定模板和数据，回到起点并完成首次显示；模板必须挂有 UIListItem。</summary>
        /// <param name="prefab">包含 UIListItem 的预制体根节点。</param>
        /// <param name="items">保留引用的数据集合；更改后调用 Refresh。</param>
        /// <exception cref="ArgumentException">模板为空或没有 UIListItem。</exception>
        /// <exception cref="ArgumentNullException">数据集合为空。</exception>
        /// <exception cref="InvalidOperationException">配置无效或正在生命周期回调内重入。</exception>
        public void Bind(GameObject prefab, IList items)
        {
            if (prefab == null || !prefab.TryGetComponent(out UIListItem item))
                throw new ArgumentException("预制体根节点必须包含 UIListItem。", nameof(prefab));
            Bind(item, items);
        }

        /// <summary>绑定模板和数据；同模板保留池，不同模板释放旧实例。</summary>
        /// <param name="prefab">预制体根节点上的 Item 组件。</param>
        /// <param name="items">保留引用的数据集合。</param>
        /// <exception cref="ArgumentNullException">模板或数据为空。</exception>
        /// <exception cref="InvalidOperationException">配置无效或正在生命周期回调内重入。</exception>
        public void Bind(UIListItem prefab, IList items)
        {
            EnsureNotUpdating();
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            if (items == null) throw new ArgumentNullException(nameof(items));
            ValidateConfiguration();
            if (prefab.transform.IsChildOf(content))
                throw new InvalidOperationException("模板不能放在列表 Content 内，请使用预制体资源。");

            // 旧实例必须先解绑，避免新的数据源覆盖旧绑定的清理上下文。
            m_updating = true;
            try
            {
                RecycleAll();
                if (m_prefab != prefab) DestroyPool();
                OnDataCleared();
                m_layoutRequested = false;
                m_prefab = prefab;
                m_data = items;
            }
            finally
            {
                m_updating = false;
            }

            CancelScroll();
            BuildAndUpdate(EUIListUpdateReason.Bind, true, true);
        }

        /// <summary>重新读取数据数量并刷新可见项，保留合法范围内的滚动距离。</summary>
        /// <exception cref="InvalidOperationException">尚未绑定、配置无效或回调重入。</exception>
        public void Refresh()
        {
            EnsureBound();
            if (m_count != m_data.Count) CancelScroll();
            BuildAndUpdate(EUIListUpdateReason.Refresh, true, false);
        }

        /// <summary>重建布局并更新可见集合，继续复用已有实例。</summary>
        /// <exception cref="InvalidOperationException">尚未绑定、配置无效或回调重入。</exception>
        public void Rebuild()
        {
            EnsureBound();
            CancelScroll();
            BuildAndUpdate(EUIListUpdateReason.Rebuild, false, false);
        }

        /// <summary>将指定索引滚动到目标对齐位置，终点受内容边界限制。</summary>
        /// <param name="index">从零开始的数据索引。</param>
        /// <param name="alignment">顶部或左侧、中心、底部或右侧对齐。</param>
        /// <param name="animated">是否使用平滑滚动。</param>
        /// <param name="duration">动画秒数，非正数表示立即定位。</param>
        /// <exception cref="ArgumentOutOfRangeException">索引、对齐值或时间无效。</exception>
        /// <exception cref="InvalidOperationException">尚未绑定、配置无效或回调重入。</exception>
        public void ScrollToIndex(
            int index,
            EUIListAlignment alignment = EUIListAlignment.Start,
            bool animated = false,
            float duration = 0.3f)
        {
            EnsureBound();
            if (index < 0 || index >= m_data.Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (alignment < EUIListAlignment.Start || alignment > EUIListAlignment.End)
                throw new ArgumentOutOfRangeException(nameof(alignment));
            EnsureCurrentLayout();
            ScrollToPos(GetScrollPosition(index, alignment), animated, duration);
        }

        /// <summary>滚动到从顶部或左侧起算的本地 UI 距离，超出部分自动限制到边界。</summary>
        /// <param name="position">非归一化的主轴滚动距离。</param>
        /// <param name="animated">是否使用平滑滚动。</param>
        /// <param name="duration">动画秒数，非正数表示立即定位。</param>
        /// <exception cref="ArgumentOutOfRangeException">位置或时间不是有限数。</exception>
        /// <exception cref="InvalidOperationException">尚未绑定、配置无效或回调重入。</exception>
        public void ScrollToPos(float position, bool animated = false, float duration = 0.3f)
        {
            EnsureBound();
            if (!IsFinite(position)) throw new ArgumentOutOfRangeException(nameof(position));
            if (!IsFinite(duration)) throw new ArgumentOutOfRangeException(nameof(duration));
            EnsureCurrentLayout();
            CancelScroll();
            m_animationTarget = Mathf.Clamp(position, 0f, MaximumPosition);
            if (!animated || duration <= 0f || !isActiveAndEnabled)
            {
                SetPosition(m_animationTarget);
                UpdateItems(EUIListUpdateReason.Scroll, false, true);
                return;
            }

            m_animationStart = Position;
            m_animationDuration = duration;
            m_animationElapsed = 0f;
            m_animating = true;
        }

        /// <summary>清除数据及活动项，可选择同时销毁缓存实例。</summary>
        /// <param name="releasePool">是否销毁空闲实例。</param>
        /// <exception cref="InvalidOperationException">正在生命周期回调中重入。</exception>
        public void Clear(bool releasePool = true)
        {
            EnsureNotUpdating();
            CancelScroll();
            m_updating = true;
            try
            {
                RecycleAll();
                if (releasePool) DestroyPool();
                OnDataCleared();
                m_layoutRequested = false;
                m_data = null;
                m_count = 0;
                m_failed = false;
                m_pendingRebuild = false;
                if (content != null)
                {
                    content.sizeDelta = Vector2.zero;
                    SetPosition(0f);
                }
            }
            finally
            {
                m_updating = false;
            }
        }

        /// <summary>用户开始拖拽时取消程序滚动动画。</summary>
        /// <param name="eventData">指针事件。</param>
        public override void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) CancelScroll();
            base.OnBeginDrag(eventData);
        }

        /// <summary>滚轮输入取消程序滚动动画。</summary>
        /// <param name="eventData">滚轮事件。</param>
        public override void OnScroll(PointerEventData eventData)
        {
            CancelScroll();
            base.OnScroll(eventData);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            m_pendingRebuild = true;
        }

        protected override void OnDisable()
        {
            CancelScroll();
            // 隐藏界面时终止可见项的订阅和异步任务，保留数据供再次启用。
            if (!m_updating)
            {
                m_updating = true;
                try
                {
                    RecycleAll();
                }
                finally
                {
                    m_updating = false;
                }
            }
            m_pendingRebuild = true;
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            m_updating = true;
            RecycleAll();
            DestroyPool();
            OnDataCleared();
            m_data = null;
            ItemsCreated = null;
            ItemsBound = null;
            BeforeDisplay = null;
            RefreshCompleted = null;
            base.OnDestroy();
        }

        protected override void LateUpdate()
        {
            // 布局先于 ScrollRect 的边界计算；动画在惯性更新后接管本帧位置。
            if (m_data != null && !m_failed)
            {
                try
                {
                    EnsureCurrentLayout();
                }
                catch (Exception exception)
                {
                    m_failed = true;
                    Debug.LogException(exception, this);
                }
            }

            base.LateUpdate();
            if (m_data == null || m_failed) return;
            if (m_animating)
            {
                StopMovement();
                m_animationElapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(m_animationElapsed / m_animationDuration);
                float eased = progress * progress * (3f - 2f * progress);
                SetPosition(Mathf.Lerp(m_animationStart, m_animationTarget, eased));
                if (progress >= 1f) m_animating = false;
            }

            try
            {
                UpdateItems(EUIListUpdateReason.Scroll, false, false);
            }
            catch (Exception exception)
            {
                m_failed = true;
                CancelScroll();
                Debug.LogException(exception, this);
            }
        }

        /// <summary>构建布局缓存并返回完整内容尺寸，派生类可替换固定尺寸算法。</summary>
        /// <returns>完整内容的宽高。</returns>
        protected virtual Vector2 RebuildLayout()
        {
            float length = m_count * (vertical ? m_itemSize.y : m_itemSize.x) +
                Mathf.Max(0, m_count - 1) * m_spacing;
            return vertical
                ? new Vector2(Mathf.Max(m_viewportSize.x, m_itemSize.x + m_padding.horizontal),
                    length + m_padding.vertical)
                : new Vector2(length + m_padding.horizontal,
                    Mathf.Max(m_viewportSize.y, m_itemSize.y + m_padding.vertical));
        }

        /// <summary>取得从内容左上角起算、向右下为正的项矩形。</summary>
        /// <param name="index">数据索引。</param>
        /// <returns>项的逻辑矩形。</returns>
        protected virtual Rect GetItemRect(int index)
        {
            return new Rect(
                m_padding.left + (vertical ? 0f : index * (m_itemSize.x + m_spacing)),
                m_padding.top + (vertical ? index * (m_itemSize.y + m_spacing) : 0f),
                m_itemSize.x,
                m_itemSize.y);
        }

        /// <summary>收集与主轴范围相交的索引，派生类可返回非连续索引。</summary>
        /// <param name="start">包含缓冲的范围起点。</param>
        /// <param name="end">包含缓冲的范围终点。</param>
        /// <param name="indices">已清空的输出列表，必须写入合法且不重复的索引。</param>
        protected virtual void CollectVisibleIndices(float start, float end, List<int> indices)
        {
            if (m_count == 0) return;
            float size = vertical ? m_itemSize.y : m_itemSize.x;
            float padding = vertical ? m_padding.top : m_padding.left;
            float stride = size + m_spacing;
            int first = Mathf.Max(0, Mathf.CeilToInt((start - padding - size) / stride));
            int last = Mathf.Min(m_count - 1, Mathf.FloorToInt((end - padding) / stride));
            for (int index = first; index <= last; index++) indices.Add(index);
        }

        /// <summary>取得项的模板。</summary>
        /// <param name="index">数据索引。</param>
        /// <returns>实例化及分组缓存的模板。</returns>
        protected virtual UIListItem GetItemPrefab(int index)
        {
            return m_prefab;
        }

        /// <summary>计算索引定位目标。</summary>
        /// <param name="index">数据索引。</param>
        /// <param name="alignment">对齐方式。</param>
        /// <returns>逻辑滚动距离。</returns>
        protected virtual float GetScrollPosition(int index, EUIListAlignment alignment)
        {
            Rect rect = GetItemRect(index);
            float start = vertical ? rect.y : rect.x;
            float length = vertical ? rect.height : rect.width;
            float factor = alignment == EUIListAlignment.Center ? 0.5f :
                alignment == EUIListAlignment.End ? 1f : 0f;
            return start - (ViewportLength - length) * factor;
        }

        /// <summary>应用项布局，不负责数据展示。</summary>
        /// <param name="item">显示实例。</param>
        /// <param name="index">数据索引。</param>
        protected virtual void ApplyItemLayout(UIListItem item, int index)
        {
            Rect rect = GetItemRect(index);
            RectTransform itemTransform = item.RectTransform;
            itemTransform.anchorMin = new Vector2(0f, 1f);
            itemTransform.anchorMax = new Vector2(0f, 1f);
            itemTransform.pivot = new Vector2(0f, 1f);
            itemTransform.sizeDelta = rect.size;
            itemTransform.anchoredPosition = new Vector2(rect.x, -rect.y);
        }

        /// <summary>请求下次自动更新重建布局，允许从 Item 回调调用。</summary>
        protected void RequestLayoutRebuild()
        {
            m_layoutRequested = true;
        }

        /// <summary>验证实例是否仍属于当前绑定。</summary>
        /// <param name="item">发起测量的实例。</param>
        /// <param name="bindingVersion">测量开始时的版本。</param>
        /// <returns>绑定是否仍有效。</returns>
        protected bool IsCurrentItem(UIListItem item, uint bindingVersion)
        {
            return item != null && item.BindingVersion == bindingVersion && item.Index >= 0 &&
                m_active.TryGetValue(item.Index, out UIListItem current) && current == item;
        }

        /// <summary>释放派生布局持有的数据与缓存。</summary>
        protected virtual void OnDataCleared() { }

        private void BuildAndUpdate(EUIListUpdateReason reason, bool refresh, bool resetPosition)
        {
            EnsureNotUpdating();
            ValidateConfiguration();
            float previousPosition = resetPosition ? 0f : Position;
            // 先用旧布局记录锚点，数据数量改变时沿用距离保持策略。
            int anchorIndex = -1;
            float anchorOffset = 0f;
            if (!resetPosition && PreserveLayoutAnchor && m_count == m_data.Count)
                CaptureScrollAnchor(out anchorIndex, out anchorOffset);
            m_count = m_data.Count;
            m_viewportSize = EffectiveViewport.rect.size;
            LayoutUpdateReason = reason;

            // 统一左上角坐标，Content 只由虚拟列表控制尺寸和位置。
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 1f);
            Vector2 size = RebuildLayout();
            if (!IsFinite(size.x) || !IsFinite(size.y) || size.x < 0f || size.y < 0f)
                throw new InvalidOperationException("布局内容尺寸必须为非负有限数。");
            content.sizeDelta = size;
            m_contentLength = vertical ? size.y : size.x;
            if (anchorIndex >= 0 && anchorIndex < m_count)
                previousPosition = GetScrollPosition(anchorIndex, EUIListAlignment.Start) + anchorOffset;
            SetPosition(Mathf.Clamp(previousPosition, 0f, MaximumPosition));
            m_pendingRebuild = false;
            m_failed = false;
            if (isActiveAndEnabled) UpdateItems(reason, refresh, true);
            else m_pendingRebuild = true;
        }

        private void EnsureCurrentLayout()
        {
            if (m_layoutRequested || m_pendingRebuild ||
                m_viewportSize != EffectiveViewport.rect.size || m_count != m_data.Count)
            {
                CancelScroll();
                bool refresh = m_pendingRebuild || m_count != m_data.Count;
                m_layoutRequested = false;
                BuildAndUpdate(EUIListUpdateReason.Rebuild, refresh, false);
            }
        }

        private void UpdateItems(EUIListUpdateReason reason, bool refresh, bool force)
        {
            if (m_updating || !isActiveAndEnabled) return;
            m_indices.Clear();
            float offset = Mathf.Clamp(Position, 0f, MaximumPosition);
            CollectVisibleIndices(offset - m_buffer, offset + ViewportLength + m_buffer, m_indices);
            bool changed = m_indices.Count != m_items.Count;
            m_indexSet.Clear();
            for (int index = 0; index < m_indices.Count; index++)
            {
                int dataIndex = m_indices[index];
                if (dataIndex < 0 || dataIndex >= m_count || !m_indexSet.Add(dataIndex))
                    throw new InvalidOperationException("布局返回了越界或重复的数据索引。");
                if (!changed && m_items[index].Index != dataIndex) changed = true;
            }
            if (!changed && !refresh && !force)
            {
                // 路径布局只更新姿态，不重复绑定或触发批次事件。
                if (LayoutChangesWithScroll)
                {
                    m_updating = true;
                    try
                    {
                        foreach (UIListItem item in m_items) ApplyItemLayout(item, item.Index);
                    }
                    finally
                    {
                        m_updating = false;
                    }
                }
                return;
            }

            m_updating = true;
            try
            {
                // 先回收再补齐，快速跳转时也复用同一批实例。
                m_removed.Clear();
                foreach (var pair in m_active)
                {
                    if (!m_indexSet.Contains(pair.Key) ||
                        m_itemPrefabs[pair.Value] != GetItemPrefab(pair.Key))
                        m_removed.Add(pair.Key);
                }
                foreach (int index in m_removed)
                {
                    Recycle(m_active[index]);
                    m_active.Remove(index);
                }

                int createdCount = 0;
                m_items.Clear();
                foreach (int index in m_indices)
                {
                    if (!m_active.TryGetValue(index, out UIListItem item))
                    {
                        item = RentItem(GetItemPrefab(index), ref createdCount);
                        m_active.Add(index, item);
                    }
                    m_items.Add(item);
                }

                var context = new UIListUpdateContext(this, reason, createdCount, ActiveItems);
                if (createdCount > 0) ItemsCreated?.Invoke(context);
                for (int index = 0; index < m_indices.Count; index++)
                {
                    UIListItem item = m_items[index];
                    int dataIndex = m_indices[index];
                    ApplyItemLayout(item, dataIndex);
                    if (refresh || item.Index != dataIndex) item.BindItem(m_data[dataIndex], dataIndex);
                }

                ItemsBound?.Invoke(context);
                // 激活后内部布局组件才参与计算；本同步批次结束前 Canvas 不会绘制。
                foreach (UIListItem item in m_items) item.gameObject.SetActive(true);
                // 只强制处理本批次 Item 内部布局，禁止重建整个 Canvas。
                foreach (UIListItem item in m_items)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(item.RectTransform);
                foreach (UIListItem item in m_items) item.BeforeDisplayItem();
                BeforeDisplay?.Invoke(context);
                TrimPool();
                RefreshCompleted?.Invoke(context);
            }
            catch
            {
                // 失败时不保留半绑定实例；业务可修复数据后主动 Refresh 重试。
                m_failed = true;
                RecycleAll();
                throw;
            }
            finally
            {
                if (!isActiveAndEnabled) RecycleAll();
                m_updating = false;
            }
        }

        private UIListItem RentItem(UIListItem prefab, ref int createdCount)
        {
            // 空闲实例始终留在 Content 下，复用时不再改变父节点。
            if (prefab == null || prefab.transform.IsChildOf(content))
                throw new InvalidOperationException("布局模板为空或位于 Content 内。");
            if (m_pool.TryGetValue(prefab, out Stack<UIListItem> pool) && pool.Count > 0)
            {
                m_inactiveCount--;
                return pool.Pop();
            }

            // 新实例直接创建到 Content 下，隐藏后完成框架初始化与数据绑定。
            UIListItem item = Instantiate(prefab, content, false);
            m_itemPrefabs.Add(item, prefab);
            item.gameObject.SetActive(false);
            try
            {
                item.InitializeItem();
                createdCount++;
                return item;
            }
            catch
            {
                DestroyItem(item);
                throw;
            }
        }

        private void Recycle(UIListItem item)
        {
            if (item == null) return;
            try
            {
                item.UnbindItem();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, item);
            }
            item.gameObject.SetActive(false);
            UIListItem prefab = m_itemPrefabs[item];
            if (!m_pool.TryGetValue(prefab, out Stack<UIListItem> pool))
            {
                pool = new Stack<UIListItem>();
                m_pool.Add(prefab, pool);
            }
            pool.Push(item);
            m_inactiveCount++;
        }

        private void RecycleAll()
        {
            foreach (var pair in m_active) Recycle(pair.Value);
            m_active.Clear();
            m_items.Clear();
        }

        private void TrimPool()
        {
            foreach (Stack<UIListItem> pool in m_pool.Values)
            {
                while (pool.Count > 0 && m_inactiveCount > m_maxInactiveItems)
                {
                    m_inactiveCount--;
                    DestroyItem(pool.Pop());
                }
            }
        }

        private void DestroyPool()
        {
            foreach (Stack<UIListItem> pool in m_pool.Values)
                while (pool.Count > 0) DestroyItem(pool.Pop());
            m_pool.Clear();
            m_inactiveCount = 0;
        }

        private void DestroyItem(UIListItem item)
        {
            if (item == null) return;
            try
            {
                item.DisposeItem();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, item);
            }
            m_itemPrefabs.Remove(item);
            if (Application.isPlaying) Destroy(item.gameObject);
            else DestroyImmediate(item.gameObject);
        }

        private void CaptureScrollAnchor(out int anchorIndex, out float offset)
        {
            anchorIndex = -1;
            offset = 0f;
            float position = Position;
            foreach (UIListItem item in m_items)
            {
                Rect rect = GetItemRect(item.Index);
                float start = vertical ? rect.y : rect.x;
                float end = vertical ? rect.yMax : rect.xMax;
                if (end <= position || start >= position + ViewportLength) continue;
                anchorIndex = item.Index;
                offset = position - start;
                return;
            }
        }

        private void SetPosition(float position)
        {
            SetContentAnchoredPosition(vertical ? new Vector2(0f, position) : new Vector2(-position, 0f));
        }

        private void CancelScroll()
        {
            m_animating = false;
            StopMovement();
        }

        private void EnsureNotUpdating()
        {
            if (m_updating) throw new InvalidOperationException("不能在列表生命周期回调内重入列表操作。");
        }

        private void EnsureBound()
        {
            EnsureNotUpdating();
            if (m_data == null) throw new InvalidOperationException("请先调用 BindData，或通过 Bind 显式指定模板。");
        }

        private void ValidateConfiguration()
        {
            if (content == null || content.parent != EffectiveViewport)
                throw new InvalidOperationException("Content 必须是 Viewport 的直接子节点。");
            if (horizontal == vertical)
                throw new InvalidOperationException("无限列表必须且只能启用一个滚动轴。");
            if (!IsFinite(m_itemSize.x) || !IsFinite(m_itemSize.y) ||
                m_itemSize.x <= 0f || m_itemSize.y <= 0f || !IsFinite(m_spacing) || m_spacing < 0f ||
                !IsFinite(m_buffer) || m_buffer < 0f || m_maxInactiveItems < 0 ||
                m_padding == null || m_padding.left < 0 || m_padding.right < 0 ||
                m_padding.top < 0 || m_padding.bottom < 0)
                throw new InvalidOperationException("项尺寸必须为正，间距、边距、缓冲和缓存上限不能为负。");
            var layout = content.GetComponent<LayoutGroup>();
            var fitter = content.GetComponent<ContentSizeFitter>();
            if ((layout != null && layout.enabled) || (fitter != null && fitter.enabled))
                throw new InvalidOperationException("Content 不能启用 LayoutGroup 或 ContentSizeFitter。");
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}