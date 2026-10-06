using System;
using UnityEngine;

namespace AlloyFramework.UI
{
    /// <summary>
    /// 描述一次屏幕与安全区域计算结果的不可变数据快照。
    /// </summary>
    public readonly struct UIScreenAdaptationSnapshot : IEquatable<UIScreenAdaptationSnapshot>
    {
        private const float SAFEAREAEPSILON = 0.01f; // 判定安全区域实际变化的像素容差。

        /// <summary>
        /// 屏幕像素宽度。
        /// </summary>
        public int ScreenWidth { get; }

        /// <summary>
        /// 屏幕像素高度。
        /// </summary>
        public int ScreenHeight { get; }

        /// <summary>
        /// 系统报告的安全区域像素矩形。
        /// </summary>
        public Rect SafeArea { get; }

        /// <summary>
        /// 安全区域左下角的归一化锚点。
        /// </summary>
        public Vector2 SafeAreaAnchorMin { get; }

        /// <summary>
        /// 安全区域右上角的归一化锚点。
        /// </summary>
        public Vector2 SafeAreaAnchorMax { get; }

        /// <summary>
        /// 左侧安全区域边距的像素值。
        /// </summary>
        public float LeftInset { get; }

        /// <summary>
        /// 右侧安全区域边距的像素值。
        /// </summary>
        public float RightInset { get; }

        /// <summary>
        /// 顶部安全区域边距的像素值。
        /// </summary>
        public float TopInset { get; }

        /// <summary>
        /// 底部安全区域边距的像素值。
        /// </summary>
        public float BottomInset { get; }

        /// <summary>
        /// 当前屏幕方向。
        /// </summary>
        public ScreenOrientation Orientation { get; }

        /// <summary>
        /// 当前屏幕宽高比。
        /// </summary>
        public float AspectRatio { get; }

        /// <summary>
        /// 快照版本号，每次有效变化递增。
        /// </summary>
        public ulong Version { get; }

        internal UIScreenAdaptationSnapshot(
            int screenWidth,
            int screenHeight,
            Rect safeArea,
            ScreenOrientation orientation,
            ulong version)
        {
            ScreenWidth = screenWidth;
            ScreenHeight = screenHeight;
            SafeArea = safeArea;
            SafeAreaAnchorMin = new Vector2(
                Mathf.Clamp01(safeArea.xMin / screenWidth),
                Mathf.Clamp01(safeArea.yMin / screenHeight));
            SafeAreaAnchorMax = new Vector2(
                Mathf.Clamp01(safeArea.xMax / screenWidth),
                Mathf.Clamp01(safeArea.yMax / screenHeight));
            LeftInset = safeArea.xMin;
            RightInset = screenWidth - safeArea.xMax;
            TopInset = screenHeight - safeArea.yMax;
            BottomInset = safeArea.yMin;
            Orientation = orientation;
            AspectRatio = (float)screenWidth / screenHeight;
            Version = version;
        }

        /// <summary>
        /// 判断当前快照是否与另一快照的屏幕数据相同。
        /// </summary>
        /// <param name="other">要比较的屏幕适配快照。</param>
        /// <returns>屏幕尺寸、安全区域和方向均相同时返回 true。</returns>
        public bool Equals(UIScreenAdaptationSnapshot other)
        {
            return ScreenWidth == other.ScreenWidth &&
                   ScreenHeight == other.ScreenHeight &&
                   Approximately(SafeArea, other.SafeArea) &&
                   Orientation == other.Orientation;
        }

        /// <summary>
        /// 判断当前快照是否与指定对象相等。
        /// </summary>
        /// <param name="obj">要比较的对象。</param>
        /// <returns>对象为相等快照时返回 true。</returns>
        public override bool Equals(object obj)
        {
            return obj is UIScreenAdaptationSnapshot snapshot && Equals(snapshot);
        }

        /// <summary>
        /// 获取当前快照的哈希代码。
        /// </summary>
        /// <returns>用于哈希容器的哈希代码。</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = ScreenWidth;
                hashCode = (hashCode * 397) ^ ScreenHeight;
                hashCode = (hashCode * 397) ^ (int)Orientation;
                return hashCode;
            }
        }

        private static bool Approximately(Rect left, Rect right)
        {
            return Mathf.Abs(left.x - right.x) <= SAFEAREAEPSILON &&
                   Mathf.Abs(left.y - right.y) <= SAFEAREAEPSILON &&
                   Mathf.Abs(left.width - right.width) <= SAFEAREAEPSILON &&
                   Mathf.Abs(left.height - right.height) <= SAFEAREAEPSILON;
        }
    }
}
