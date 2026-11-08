using System.Collections.Generic;

namespace AlloyFramework.UI
{
    /// <summary>目标项在视口中的对齐位置。</summary>
    public enum EUIListAlignment
    {
        Start,
        Center,
        End
    }

    /// <summary>列表显示批次的触发原因。</summary>
    public enum EUIListUpdateReason
    {
        Bind,
        Refresh,
        Rebuild,
        Scroll
    }

    /// <summary>显示批次上下文；活动集合是只读实时视图，仅在同步回调内读取。</summary>
    public readonly struct UIListUpdateContext
    {
        /// <summary>触发本次更新的列表。</summary>
        public UIInfiniteList List { get; }

        /// <summary>本次更新的原因。</summary>
        public EUIListUpdateReason Reason { get; }

        /// <summary>本次创建并初始化的实例数量。</summary>
        public int CreatedCount { get; }

        /// <summary>本批次活动项，包含缓冲区；创建阶段尚未全部绑定。</summary>
        public IReadOnlyList<UIListItem> Items { get; }

        internal UIListUpdateContext(
            UIInfiniteList list,
            EUIListUpdateReason reason,
            int createdCount,
            IReadOnlyList<UIListItem> items)
        {
            List = list;
            Reason = reason;
            CreatedCount = createdCount;
            Items = items;
        }
    }}
