using System;
using UnityEngine;

namespace AlloyFramework.UI
{
    [Serializable]
    public sealed class UIAnimationDefinition
    {
        [SerializeField]
        private string m_key; // 当前动效使用的稳定业务 Key。

        [SerializeField]
        private AnimationClip m_clip; // 当前定义播放的明确 Clip。

        [SerializeField]
        private EUIAnimationTimeMode m_timeMode = EUIAnimationTimeMode.Unscaled; // 播放使用的时间来源。

        [SerializeField]
        private bool m_loop; // 是否循环播放；Open 和 Close 不允许循环。

        public string Key => m_key;
        public AnimationClip Clip => m_clip;
        public EUIAnimationTimeMode TimeMode => m_timeMode;
        public bool Loop => m_loop;
        public bool IsConfigured => !string.IsNullOrEmpty(m_key) || m_clip != null;
    }
}
