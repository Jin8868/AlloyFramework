using System.Collections.Generic;

namespace AlloyFramework.UI
{
    public static class UIAnimationEventKeys
    {
        private static readonly string[] m_all =
        {
            START,
            CONTENTVISIBLE,
            IMPACT,
            INTERACTIONREADY,
            COMPLETE,
            ANIMATIONEVENT1,
            ANIMATIONEVENT2,
            ANIMATIONEVENT3,
            ANIMATIONEVENT4,
            ANIMATIONEVENT5
        }; // 框架支持的全部 UI 动效帧事件 Key。

        /// <summary>
        /// 动效开始事件。
        /// </summary>
        public const string START = "Start";

        /// <summary>
        /// 主要界面内容已经可见事件。
        /// </summary>
        public const string CONTENTVISIBLE = "ContentVisible";

        /// <summary>
        /// 动效到达主要冲击或强调时机事件。
        /// </summary>
        public const string IMPACT = "Impact";

        /// <summary>
        /// 界面已经适合响应交互事件。
        /// </summary>
        public const string INTERACTIONREADY = "InteractionReady";

        /// <summary>
        /// Clip 中约定的完成事件帧。
        /// </summary>
        public const string COMPLETE = "Complete";

        /// <summary>
        /// 第一个通用业务帧事件。
        /// </summary>
        public const string ANIMATIONEVENT1 = "AnimationEvent1";

        /// <summary>
        /// 第二个通用业务帧事件。
        /// </summary>
        public const string ANIMATIONEVENT2 = "AnimationEvent2";

        /// <summary>
        /// 第三个通用业务帧事件。
        /// </summary>
        public const string ANIMATIONEVENT3 = "AnimationEvent3";

        /// <summary>
        /// 第四个通用业务帧事件。
        /// </summary>
        public const string ANIMATIONEVENT4 = "AnimationEvent4";

        /// <summary>
        /// 第五个通用业务帧事件。
        /// </summary>
        public const string ANIMATIONEVENT5 = "AnimationEvent5";

        public static IReadOnlyList<string> All => m_all;
    }
}
