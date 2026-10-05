namespace AlloyFramework.UI
{
    public readonly struct UIAnimationEventContext
    {
        internal UIAnimationEventContext(
            UIAnimationPlayer player,
            string animationKey,
            string eventKey,
            float time)
        {
            Player = player;
            AnimationKey = animationKey;
            EventKey = eventKey;
            Time = time;
        }

        public UIAnimationPlayer Player { get; }
        public string AnimationKey { get; }
        public string EventKey { get; }
        public float Time { get; }
    }
}
