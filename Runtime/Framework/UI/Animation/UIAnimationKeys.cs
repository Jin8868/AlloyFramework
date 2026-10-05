using System.Collections.Generic;

namespace AlloyFramework.UI
{
    public static class UIAnimationKeys
    {
        private static readonly string[] m_all =
        {
            OPEN,
            CLOSE,
            ANIMATION1,
            ANIMATION2,
            ANIMATION3,
            ANIMATION4,
            ANIMATION5
        }; // 框架支持的全部 UI 动效 Key。

        private static readonly string[] m_custom =
        {
            ANIMATION1,
            ANIMATION2,
            ANIMATION3,
            ANIMATION4,
            ANIMATION5
        }; // 框架支持的自定义 UI 动效 Key。

        /// <summary>
        /// UI 打开生命周期动效 Key。
        /// </summary>
        public const string OPEN = "Open";

        /// <summary>
        /// UI 关闭生命周期动效 Key。
        /// </summary>
        public const string CLOSE = "Close";

        /// <summary>
        /// 第一个自定义 UI 动效 Key。
        /// </summary>
        public const string ANIMATION1 = "Animation1";

        /// <summary>
        /// 第二个自定义 UI 动效 Key。
        /// </summary>
        public const string ANIMATION2 = "Animation2";

        /// <summary>
        /// 第三个自定义 UI 动效 Key。
        /// </summary>
        public const string ANIMATION3 = "Animation3";

        /// <summary>
        /// 第四个自定义 UI 动效 Key。
        /// </summary>
        public const string ANIMATION4 = "Animation4";

        /// <summary>
        /// 第五个自定义 UI 动效 Key。
        /// </summary>
        public const string ANIMATION5 = "Animation5";

        public static IReadOnlyList<string> All => m_all;
        public static IReadOnlyList<string> Custom => m_custom;
    }
}
