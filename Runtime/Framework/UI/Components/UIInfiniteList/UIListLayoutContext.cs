using System.Collections;
using UnityEngine;

namespace AlloyFramework.UI
{
    /// <summary>布局输入快照；布局查询期间不得修改数据。</summary>
    public readonly struct UIListLayoutContext
    {
        /// <summary>业务数据。</summary>
        public IList Items { get; }
        /// <summary>视口尺寸。</summary>
        public Vector2 ViewportSize { get; }
        /// <summary>默认项尺寸。</summary>
        public Vector2 DefaultItemSize { get; }
        /// <summary>默认间距。</summary>
        public float Spacing { get; }
        /// <summary>内容边距快照。</summary>
        public RectOffset Padding { get; }
        /// <summary>是否纵向滚动。</summary>
        public bool Vertical { get; }
        /// <summary>重建原因。</summary>
        public EUIListUpdateReason Reason { get; }
        /// <summary>主轴视口长度。</summary>
        public float ViewportLength => Vertical ? ViewportSize.y : ViewportSize.x;

        internal UIListLayoutContext(
            IList items, Vector2 viewportSize, Vector2 defaultItemSize,
            float spacing, RectOffset padding, bool vertical, EUIListUpdateReason reason)
        {
            Items = items;
            ViewportSize = viewportSize;
            DefaultItemSize = defaultItemSize;
            Spacing = spacing;
            Padding = new RectOffset(padding.left, padding.right, padding.top, padding.bottom);
            Vertical = vertical;
            Reason = reason;
        }
    }

    /// <summary>逐项尺寸、模板与间距配置。</summary>
    public readonly struct UIListItemSettings
    {
        /// <summary>布局尺寸。</summary>
        public Vector2 Size { get; }
        /// <summary>模板编号，零表示默认模板。</summary>
        public int TemplateKey { get; }
        /// <summary>当前项之后的间距，最后一项不使用。</summary>
        public float SpacingAfter { get; }

        /// <summary>创建单项配置。</summary>
        /// <param name="size">布局尺寸。</param>
        /// <param name="templateKey">模板编号。</param>
        /// <param name="spacingAfter">项后间距。</param>
        public UIListItemSettings(Vector2 size, int templateKey = 0, float spacingAfter = 0f)
        {
            Size = size;
            TemplateKey = templateKey;
            SpacingAfter = spacingAfter;
        }
    }

    /// <summary>单项布局结果，矩形从左上角起算，向右下为正。</summary>
    public struct UIListItemLayout
    {
        /// <summary>项的逻辑矩形。</summary>
        public Rect Rect { get; set; }
        /// <summary>平面旋转角度，遵循 Unity 角度方向。</summary>
        public float Rotation { get; set; }
        /// <summary>局部缩放。</summary>
        public Vector2 Scale { get; set; }

        /// <summary>创建无旋转、单位缩放的布局结果。</summary>
        /// <param name="rect">逻辑矩形。</param>
        public UIListItemLayout(Rect rect)
        {
            Rect = rect;
            Rotation = 0f;
            Scale = Vector2.one;
        }
    }
}