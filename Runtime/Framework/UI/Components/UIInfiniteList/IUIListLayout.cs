using System.Collections.Generic;
using UnityEngine;

namespace AlloyFramework.UI
{
    /// <summary>由单个列表独占的布局契约，支持普通对象和组件实现。</summary>
    public interface IUIListLayout
    {
        /// <summary>滚动时是否更新已有项姿态。</summary>
        bool ChangesWithScroll { get; }
        /// <summary>是否输出视口坐标。</summary>
        bool UsesViewportCoordinates { get; }
        /// <summary>重建时是否保持可见项偏移。</summary>
        bool PreserveScrollAnchor { get; }

        /// <summary>保存输入并重建布局，由列表调用。</summary>
        /// <param name="context">布局输入。</param>
        void Prepare(UIListLayoutContext context);

        /// <summary>获取逻辑内容尺寸。</summary>
        /// <returns>内容宽高。</returns>
        Vector2 GetContentSize();

        /// <summary>查询需要显示的数据索引。</summary>
        /// <param name="start">带缓冲的起点。</param>
        /// <param name="end">带缓冲的终点。</param>
        /// <param name="scrollPosition">当前滚动距离。</param>
        /// <param name="results">已清空的输出集合。</param>
        void CollectVisibleItems(float start, float end, float scrollPosition, List<int> results);

        /// <summary>获取单项布局。</summary>
        /// <param name="index">数据索引。</param>
        /// <param name="scrollPosition">当前距离。</param>
        /// <returns>位置、尺寸和姿态。</returns>
        UIListItemLayout GetItemLayout(int index, float scrollPosition);

        /// <summary>选择项模板。</summary>
        /// <param name="index">数据索引。</param>
        /// <returns>模板编号。</returns>
        int GetTemplateKey(int index);

        /// <summary>计算定位目标。</summary>
        /// <param name="index">目标索引。</param>
        /// <param name="alignment">对齐方式。</param>
        /// <param name="currentPosition">当前位置。</param>
        /// <returns>目标滚动距离。</returns>
        float GetScrollPosition(int index, EUIListAlignment alignment, float currentPosition);

        /// <summary>记录实际尺寸，等待后续重建。</summary>
        /// <param name="index">数据索引。</param>
        /// <param name="size">实测尺寸。</param>
        /// <returns>是否需要重建。</returns>
        bool ReportItemSize(int index, Vector2 size);

        /// <summary>释放本轮数据引用和缓存，实例仍可再次使用。</summary>
        void Clear();
    }
}