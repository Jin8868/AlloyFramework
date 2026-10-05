using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlloyFramework.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class UIView : MonoBehaviour
    {
        private UIAnimationPlayer m_animationPlayer; // 当前 View 根节点上的可选动效播放器。
        private bool m_animationPlayerResolved; // 是否已经查找过根节点动效播放器。

        /// <summary>
        /// 当前 View 动效越过业务事件帧时触发。
        /// </summary>
        public event Action<UIAnimationEventContext> AnimationEventReceived
        {
            add => GetRequiredAnimationPlayer().AnimationEventReceived += value;
            remove
            {
                var animationPlayer = GetAnimationPlayer();
                if (animationPlayer != null)
                {
                    animationPlayer.AnimationEventReceived -= value;
                }
            }
        }

        /// <summary>
        /// 根据生成的 Key 播放自定义动效并通过回调报告完成或错误。
        /// </summary>
        /// <param name="animationKey">Prefab 中定义的自定义动效 Key。</param>
        /// <param name="onCompleted">播放结束后的错误回调，成功时参数为空。</param>
        /// <param name="cancellationToken">用于取消播放的令牌。</param>
        public void Play(
            string animationKey,
            Action<Exception> onCompleted = null,
            CancellationToken cancellationToken = default)
        {
            var animationPlayer = GetAnimationPlayer();
            if (animationPlayer == null)
            {
                var exception = new InvalidOperationException(
                    $"UIView {name} 的 Prefab 根节点没有配置 UIAnimationPlayer，无法播放 Key={animationKey}。");
                UICallbacks.Run(UniTask.FromException(exception), onCompleted);
                return;
            }

            animationPlayer.Play(animationKey, onCompleted, cancellationToken);
        }

        internal void PrepareOpenAnimation()
        {
            var animationPlayer = GetAnimationPlayer();
            if (animationPlayer != null && animationPlayer.HasOpenAnimation)
            {
                animationPlayer.PrepareOpenAnimation();
                return;
            }

            // 没有 Open 动效时恢复正常可见状态，避免沿用旧生命周期留下的透明度。
            var canvasGroup = GetComponent<CanvasGroup>();
            canvasGroup.alpha = 1f;
        }

        internal UniTask PlayOpenAnimationAsync(CancellationToken cancellationToken)
        {
            var animationPlayer = GetAnimationPlayer();
            return animationPlayer == null
                ? UniTask.CompletedTask
                : animationPlayer.PlayOpenAsync(cancellationToken);
        }

        internal UniTask PlayCloseAnimationAsync(CancellationToken cancellationToken)
        {
            var animationPlayer = GetAnimationPlayer();
            return animationPlayer == null
                ? UniTask.CompletedTask
                : animationPlayer.PlayCloseAsync(cancellationToken);
        }

        private UIAnimationPlayer GetAnimationPlayer()
        {
            if (!m_animationPlayerResolved)
            {
                m_animationPlayer = GetComponent<UIAnimationPlayer>();
                m_animationPlayerResolved = true;
            }

            return m_animationPlayer;
        }

        private UIAnimationPlayer GetRequiredAnimationPlayer()
        {
            return GetAnimationPlayer() ?? throw new InvalidOperationException(
                $"UIView {name} 的 Prefab 根节点没有配置 UIAnimationPlayer，无法订阅动效事件。");
        }
    }
}
