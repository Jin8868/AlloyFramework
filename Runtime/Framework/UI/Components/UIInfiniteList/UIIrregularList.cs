using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlloyFramework.UI
{
    /// <summary>由业务布局行为驱动的异形虚拟列表。</summary>
    public class UIIrregularList : UIInfiniteList
    {
        [SerializeField] private UIListLayoutBehaviour m_layoutBehaviour; // 当前列表独占的布局组件。
        [SerializeField] private UIListTemplate[] m_templates = Array.Empty<UIListTemplate>(); // 额外模板。
        private readonly Dictionary<int, UIListItem> m_templateLookup =
            new Dictionary<int, UIListItem>(); // 模板编号查询。
        private IUIListLayout m_preparedLayout; // 最近一次准备的布局对象。
        private IUIListLayout m_runtimeLayout; // 代码配置，优先于 Inspector。

        /// <summary>Inspector 配置的布局行为。</summary>
        public UIListLayoutBehaviour LayoutBehaviour => m_layoutBehaviour;

        /// <summary>设置代码布局；传空恢复 Inspector，已有数据会重新绑定并回到起点。</summary>
        /// <param name="layout">当前列表独占的布局对象或同节点组件，空表示恢复 Inspector。</param>
        /// <exception cref="InvalidOperationException">回调重入、布局未配置或组件不属于当前列表。</exception>
        public void SetLayout(IUIListLayout layout)
        {
            if (IsUpdatingItems)
                throw new InvalidOperationException("不能在列表生命周期回调内切换布局。");
            if (layout is UnityEngine.Object unityObject && unityObject == null)
                throw new InvalidOperationException("传入的布局组件已销毁。");
            if (layout is Component component && component.gameObject != gameObject)
                throw new InvalidOperationException("组件布局必须位于列表同一节点。");
            if (layout == null)
            {
                if (m_layoutBehaviour == null) m_layoutBehaviour = GetComponent<UIListLayoutBehaviour>();
                if (m_layoutBehaviour == null || m_layoutBehaviour.gameObject != gameObject)
                    throw new InvalidOperationException("未配置可恢复的 Inspector 布局。");
            }
            if (ReferenceEquals(m_runtimeLayout, layout)) return;

            // 先通过原有清理流程取消动画、解绑实例和释放旧布局，再安装新布局。
            var items = BoundData;
            UIListItem prefab = BoundPrefab;
            Clear(false);
            m_runtimeLayout = layout;
            if (items != null) Bind(prefab, items);
        }

        protected override bool LayoutChangesWithScroll =>
            m_preparedLayout != null && m_preparedLayout.ChangesWithScroll;

        protected override bool PreserveLayoutAnchor =>
            m_preparedLayout != null && m_preparedLayout.PreserveScrollAnchor;

        /// <summary>上报实际尺寸并请求下次更新重建，可从 Item 生命周期回调调用。</summary>
        /// <param name="item">发起测量的活动实例。</param>
        /// <param name="size">实际布局尺寸，不乘缩放。</param>
        /// <param name="bindingVersion">测量开始时保存的 BindingVersion。</param>
        /// <returns>尺寸有效且发生变化时返回真，旧绑定或未支持测量返回假。</returns>
        /// <exception cref="ArgumentOutOfRangeException">尺寸不是正有限数。</exception>
        public bool ReportItemSize(UIListItem item, Vector2 size, uint bindingVersion)
        {
            // 旧异步结果不能覆盖复用后新数据的布局尺寸。
            if (!IsCurrentItem(item, bindingVersion) || m_preparedLayout == null) return false;
            if (!UIListLayoutValidation.IsPositiveSize(size))
                throw new ArgumentOutOfRangeException(nameof(size));
            if (!m_preparedLayout.ReportItemSize(item.Index, size)) return false;
            RequestLayoutRebuild();
            return true;
        }

        protected override Vector2 RebuildLayout()
        {
            IUIListLayout selected = m_runtimeLayout;
            if (selected == null)
            {
                if (m_layoutBehaviour == null) m_layoutBehaviour = GetComponent<UIListLayoutBehaviour>();
                if (m_layoutBehaviour == null || m_layoutBehaviour.gameObject != gameObject)
                    throw new InvalidOperationException("请调用 SetLayout，或在同一节点配置布局行为组件。");
                selected = m_layoutBehaviour;
            }
            if (selected is UnityEngine.Object unityObject && unityObject == null)
                throw new InvalidOperationException("当前布局组件已销毁。");
            if (m_preparedLayout != null && !ReferenceEquals(m_preparedLayout, selected))
                ClearPreparedLayout();
            m_preparedLayout = selected;

            // 编号零保留给默认模板，额外模板编号必须为唯一正整数。
            m_templateLookup.Clear();
            if (m_templates != null)
            {
                foreach (UIListTemplate template in m_templates)
                {
                    if (template == null || template.Key <= 0 || template.Prefab == null ||
                        template.Prefab.transform.IsChildOf(content) || m_templateLookup.ContainsKey(template.Key))
                        throw new InvalidOperationException("额外模板需使用唯一正编号和 Content 外的预制体引用。");
                    m_templateLookup.Add(template.Key, template.Prefab);
                }
            }
            var context = new UIListLayoutContext(
                BoundData, ViewportSize, ItemSize, Spacing, Padding, vertical, LayoutUpdateReason);
            m_preparedLayout.Prepare(context);
            return m_preparedLayout.GetContentSize();
        }

        protected override Rect GetItemRect(int index)
        {
            return m_preparedLayout.GetItemLayout(index, Position).Rect;
        }

        protected override void CollectVisibleIndices(float start, float end, List<int> indices)
        {
            m_preparedLayout.CollectVisibleItems(start, end, Position, indices);
        }

        protected override UIListItem GetItemPrefab(int index)
        {
            int key = m_preparedLayout.GetTemplateKey(index);
            if (key == 0) return base.GetItemPrefab(index);
            if (!m_templateLookup.TryGetValue(key, out UIListItem prefab))
                throw new InvalidOperationException($"未配置异形列表模板编号：{key}。");
            return prefab;
        }

        protected override float GetScrollPosition(int index, EUIListAlignment alignment)
        {
            return m_preparedLayout.GetScrollPosition(index, alignment, Position);
        }

        protected override void ApplyItemLayout(UIListItem item, int index)
        {
            UIListItemLayout layout = m_preparedLayout.GetItemLayout(index, Position);
            Rect rect = layout.Rect;
            if (!UIListLayoutValidation.IsPositiveSize(rect.size) ||
                !UIListLayoutValidation.IsFinite(rect.x) || !UIListLayoutValidation.IsFinite(rect.y) ||
                !UIListLayoutValidation.IsFinite(layout.Rotation) ||
                !UIListLayoutValidation.IsFinite(layout.Scale.x) ||
                !UIListLayoutValidation.IsFinite(layout.Scale.y))
                throw new InvalidOperationException("布局结果包含无效位置、尺寸、旋转或缩放。");

            // 视口坐标布局自动抵消 Content 位移，避免滚动被重复应用。
            if (m_preparedLayout.UsesViewportCoordinates)
            {
                if (vertical) rect.y += Position;
                else rect.x += Position;
            }
            RectTransform itemTransform = item.RectTransform;
            itemTransform.anchorMin = new Vector2(0f, 1f);
            itemTransform.anchorMax = new Vector2(0f, 1f);
            itemTransform.pivot = new Vector2(0f, 1f);
            itemTransform.sizeDelta = rect.size;
            itemTransform.anchoredPosition = new Vector2(rect.x, -rect.y);
            itemTransform.localRotation = Quaternion.Euler(0f, 0f, layout.Rotation);
            itemTransform.localScale = new Vector3(layout.Scale.x, layout.Scale.y, 1f);
        }

        private void ClearPreparedLayout()
        {
            // 接口引用不会执行 Unity 的空值判断，跳过已经销毁的组件。
            if (m_preparedLayout is UnityEngine.Object unityObject && unityObject == null) return;
            m_preparedLayout?.Clear();
        }

        protected override void OnDataCleared()
        {
            ClearPreparedLayout();
            m_preparedLayout = null;
            m_templateLookup.Clear();
        }
    }

    /// <summary>Inspector 按编号登记的额外 Item 模板。</summary>
    [Serializable]
    public sealed class UIListTemplate
    {
        [SerializeField] private int m_key = 1; // 正整数模板编号。
        [SerializeField] private UIListItem m_prefab; // 业务 Item 预制体引用。

        /// <summary>模板编号。</summary>
        public int Key => m_key;
        /// <summary>模板预制体。</summary>
        public UIListItem Prefab => m_prefab;
    }
}