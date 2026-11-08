using System.Collections.Generic;
using UnityEngine;

namespace AlloyFramework.UI
{
    /// <summary>异形布局行为契约；不创建实例、不绑定数据、不修改源集合。</summary>
    public abstract class UIListLayoutBehaviour : MonoBehaviour, IUIListLayout
    {
        /// <summary>当前布局输入。</summary>
        protected UIListLayoutContext Context { get; private set; }
        /// <summary>滚动时是否需要更新仍可见项的位置和姿态。</summary>
        public virtual bool ChangesWithScroll => false;
        /// <summary>是否输出视口坐标；启用后框架补偿 Content 平移。</summary>
        public virtual bool UsesViewportCoordinates => false;
        /// <summary>重建时是否保持首个可见项的相对偏移，路径布局默认关闭。</summary>
        public virtual bool PreserveScrollAnchor => false;

        void IUIListLayout.Prepare(UIListLayoutContext context)
        {
            Context = context;
            Rebuild(context);
        }

        /// <summary>构建缓存，不允许修改源集合或重入列表更新接口。</summary>
        /// <param name="context">布局输入。</param>
        public abstract void Rebuild(UIListLayoutContext context);

        /// <summary>返回逻辑内容尺寸，主轴长度决定滚动范围。</summary>
        /// <returns>非负有限的内容宽高。</returns>
        public abstract Vector2 GetContentSize();

        /// <summary>查询本次显示项，允许非连续索引，不允许重复或越界。</summary>
        /// <param name="start">带缓冲的主轴起点。</param>
        /// <param name="end">带缓冲的主轴终点。</param>
        /// <param name="scrollPosition">实际滚动距离。</param>
        /// <param name="results">已清空的输出集合。</param>
        public abstract void CollectVisibleItems(
            float start, float end, float scrollPosition, List<int> results);

        /// <summary>取得项的位置、尺寸和姿态。</summary>
        /// <param name="index">有效数据索引。</param>
        /// <param name="scrollPosition">实际滚动距离。</param>
        /// <returns>项布局结果。</returns>
        public abstract UIListItemLayout GetItemLayout(int index, float scrollPosition);

        /// <summary>选择模板；同一数据的模板不应随滚动变化。</summary>
        /// <param name="index">有效数据索引。</param>
        /// <returns>模板编号，默认零。</returns>
        public virtual int GetTemplateKey(int index)
        {
            return 0;
        }

        /// <summary>将索引和对齐方式转换成逻辑滚动距离。</summary>
        /// <param name="index">目标索引。</param>
        /// <param name="alignment">对齐方式。</param>
        /// <param name="currentPosition">当前位置。</param>
        /// <returns>目标距离，由列表限制到边界。</returns>
        public abstract float GetScrollPosition(
            int index, EUIListAlignment alignment, float currentPosition);

        /// <summary>记录待应用的测量尺寸，不立即修改当前布局缓存。</summary>
        /// <param name="index">有效数据索引。</param>
        /// <param name="size">实际尺寸。</param>
        /// <returns>需要重建时返回真，未支持或无变化时返回假。</returns>
        public virtual bool ReportItemSize(int index, Vector2 size)
        {
            return false;
        }

        /// <summary>解绑数据源时释放引用和缓存。</summary>
        public virtual void Clear()
        {
            Context = default;
        }
    }
}