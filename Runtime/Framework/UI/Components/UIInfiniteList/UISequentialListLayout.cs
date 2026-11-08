using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlloyFramework.UI
{
    /// <summary>不等宽或不等高的顺序布局，业务通常仅需重写 GetItemSettings。</summary>
    public class UISequentialListLayout : UIListLayoutBehaviour
    {
        private readonly List<Rect> m_rects = new List<Rect>(); // 按主轴排序的项矩形。
        private readonly List<int> m_templates = new List<int>(); // 每项模板编号。
        private readonly Dictionary<int, Vector2> m_measuredSizes = new Dictionary<int, Vector2>(); // 实测尺寸。
        private Vector2 m_contentSize; // 逻辑内容尺寸。
        private Vector2 m_lastViewport; // 旧测量对应的视口。
        private bool m_lastVertical; // 旧测量对应的方向。

        /// <summary>尺寸变化时保持首个可见项的位置。</summary>
        public override bool PreserveScrollAnchor => true;

        /// <summary>重建顺序布局，数据刷新、数量或视口改变会清除旧测量。</summary>
        /// <param name="context">布局输入。</param>
        /// <exception cref="InvalidOperationException">业务返回无效尺寸或间距。</exception>
        public override void Rebuild(UIListLayoutContext context)
        {
            // 测量值不能跨数据刷新、方向或视口变化复用。
            if (context.Reason == EUIListUpdateReason.Bind || context.Reason == EUIListUpdateReason.Refresh ||
                m_rects.Count != context.Items.Count || m_lastViewport != context.ViewportSize ||
                m_lastVertical != context.Vertical)
                m_measuredSizes.Clear();
            m_lastViewport = context.ViewportSize;
            m_lastVertical = context.Vertical;
            m_rects.Clear();
            m_templates.Clear();
            float cursor = context.Vertical ? context.Padding.top : context.Padding.left;
            float crossLength = 0f;
            for (int index = 0; index < context.Items.Count; index++)
            {
                UIListItemSettings settings = GetItemSettings(context.Items[index], index, context);
                Vector2 size = m_measuredSizes.TryGetValue(index, out Vector2 measured)
                    ? measured : settings.Size;
                if (!UIListLayoutValidation.IsPositiveSize(size) ||
                    !UIListLayoutValidation.IsFinite(settings.SpacingAfter) || settings.SpacingAfter < 0f)
                    throw new InvalidOperationException("项尺寸必须为正有限数，间距必须为非负有限数。");
                Rect rect = context.Vertical
                    ? new Rect(context.Padding.left, cursor, size.x, size.y)
                    : new Rect(cursor, context.Padding.top, size.x, size.y);
                m_rects.Add(rect);
                m_templates.Add(settings.TemplateKey);
                cursor += context.Vertical ? size.y : size.x;
                if (index + 1 < context.Items.Count) cursor += settings.SpacingAfter;
                crossLength = Mathf.Max(crossLength, context.Vertical ? size.x : size.y);
            }
            m_contentSize = context.Vertical
                ? new Vector2(Mathf.Max(context.ViewportSize.x, crossLength + context.Padding.horizontal),
                    cursor + context.Padding.bottom)
                : new Vector2(cursor + context.Padding.right,
                    Mathf.Max(context.ViewportSize.y, crossLength + context.Padding.vertical));
        }

        /// <summary>提供单项配置，不依赖 Item 实例。</summary>
        /// <param name="data">业务数据。</param>
        /// <param name="index">数据索引。</param>
        /// <param name="context">视口和默认配置。</param>
        /// <returns>尺寸、模板编号和项后间距。</returns>
        protected virtual UIListItemSettings GetItemSettings(
            object data, int index, UIListLayoutContext context)
        {
            return new UIListItemSettings(context.DefaultItemSize, 0, context.Spacing);
        }

        /// <summary>返回缓存的内容尺寸。</summary>
        /// <returns>内容宽高。</returns>
        public override Vector2 GetContentSize()
        {
            return m_contentSize;
        }

        /// <summary>以二分查找和顺序遍历查询可见项。</summary>
        /// <param name="start">带缓冲的起点。</param>
        /// <param name="end">带缓冲的终点。</param>
        /// <param name="scrollPosition">当前滚动距离。</param>
        /// <param name="results">已清空的输出集合。</param>
        public override void CollectVisibleItems(
            float start, float end, float scrollPosition, List<int> results)
        {
            // 跳过结束位置在视口之前的项，避免每帧全量扫描。
            int low = 0;
            int high = m_rects.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                Rect rect = m_rects[middle];
                float itemEnd = Context.Vertical ? rect.yMax : rect.xMax;
                if (itemEnd < start) low = middle + 1;
                else high = middle;
            }
            for (int index = low; index < m_rects.Count; index++)
            {
                Rect rect = m_rects[index];
                if ((Context.Vertical ? rect.y : rect.x) > end) break;
                results.Add(index);
            }
        }

        /// <summary>返回缓存矩形。</summary>
        /// <param name="index">有效数据索引。</param>
        /// <param name="scrollPosition">当前滚动距离。</param>
        /// <returns>无旋转、单位缩放的布局结果。</returns>
        public override UIListItemLayout GetItemLayout(int index, float scrollPosition)
        {
            return new UIListItemLayout(m_rects[index]);
        }

        /// <summary>返回项的模板编号。</summary>
        /// <param name="index">有效数据索引。</param>
        /// <returns>模板编号。</returns>
        public override int GetTemplateKey(int index)
        {
            return m_templates[index];
        }

        /// <summary>按矩形计算索引定位目标。</summary>
        /// <param name="index">有效数据索引。</param>
        /// <param name="alignment">对齐方式。</param>
        /// <param name="currentPosition">当前位置。</param>
        /// <returns>目标距离。</returns>
        public override float GetScrollPosition(
            int index, EUIListAlignment alignment, float currentPosition)
        {
            Rect rect = m_rects[index];
            float start = Context.Vertical ? rect.y : rect.x;
            float length = Context.Vertical ? rect.height : rect.width;
            float factor = alignment == EUIListAlignment.Center ? 0.5f :
                alignment == EUIListAlignment.End ? 1f : 0f;
            return start - (Context.ViewportLength - length) * factor;
        }

        /// <summary>缓存测量结果，下次重建时应用。</summary>
        /// <param name="index">有效数据索引。</param>
        /// <param name="size">实际尺寸。</param>
        /// <returns>尺寸是否发生变化。</returns>
        /// <exception cref="ArgumentOutOfRangeException">尺寸或索引无效。</exception>
        public override bool ReportItemSize(int index, Vector2 size)
        {
            if (index < 0 || index >= m_rects.Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (!UIListLayoutValidation.IsPositiveSize(size))
                throw new ArgumentOutOfRangeException(nameof(size));
            Vector2 previous = m_measuredSizes.TryGetValue(index, out Vector2 measured)
                ? measured : m_rects[index].size;
            if ((previous - size).sqrMagnitude <= 0.01f) return false;
            m_measuredSizes[index] = size;
            return true;
        }

        /// <summary>释放数据和测量缓存。</summary>
        public override void Clear()
        {
            base.Clear();
            m_rects.Clear();
            m_templates.Clear();
            m_measuredSizes.Clear();
            m_contentSize = Vector2.zero;
        }
    }

    internal static class UIListLayoutValidation
    {
        internal static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        internal static bool IsPositiveSize(Vector2 size)
        {
            return IsFinite(size.x) && IsFinite(size.y) && size.x > 0f && size.y > 0f;
        }
    }
}