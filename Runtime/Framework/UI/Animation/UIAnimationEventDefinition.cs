using System;
using UnityEngine;

namespace AlloyFramework.UI
{
    [Serializable]
    public sealed class UIAnimationEventDefinition
    {
        [SerializeField]
        private string m_animationKey; // 触发当前事件的目标动效 Key。

        [SerializeField]
        private string m_eventKey; // 派发给业务层的事件 Key。

        [SerializeField]
        private float m_time; // 相对目标动效起点的触发时间。

        public string AnimationKey => m_animationKey;
        public string EventKey => m_eventKey;
        public float Time => m_time;
    }
}
