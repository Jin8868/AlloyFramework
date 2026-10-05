using System;

namespace AlloyFramework.UI
{
    public static class UIAnimationValidationRules
    {
        /// <summary>
        /// 校验自定义 UI 动效 Key 是否符合生成规范。
        /// </summary>
        /// <param name="animationKey">需要校验的动效 Key。</param>
        /// <exception cref="InvalidOperationException">Key 为空、格式非法或使用保留名称时抛出。</exception>
        public static void ValidateKey(string animationKey)
        {
            if (string.IsNullOrWhiteSpace(animationKey))
            {
                throw new InvalidOperationException("UI 自定义动效 Key 不能为空。");
            }

            if (string.Equals(animationKey, "Open", StringComparison.Ordinal) ||
                string.Equals(animationKey, "Close", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"UI 自定义动效 Key 使用了保留名称：{animationKey}。");
            }

            if (animationKey[0] < 'A' || animationKey[0] > 'Z')
            {
                throw new InvalidOperationException($"UI 自定义动效 Key 必须以大写英文字母开头：{animationKey}。");
            }

            for (var index = 1; index < animationKey.Length; index++)
            {
                var character = animationKey[index];
                var isLetter = character >= 'A' && character <= 'Z' || character >= 'a' && character <= 'z';
                if (!isLetter && (character < '0' || character > '9'))
                {
                    throw new InvalidOperationException(
                        $"UI 自定义动效 Key 只能包含英文字母和数字：{animationKey}。");
                }
            }

            if (!IsDefinedCustomAnimationKey(animationKey))
            {
                throw new InvalidOperationException($"UI 自定义动效 Key 未在框架中定义：{animationKey}。");
            }
        }

        /// <summary>
        /// 校验 Clip 中 UIAnimationEvent 使用的业务事件 Key。
        /// </summary>
        /// <param name="eventKey">需要校验的业务事件 Key。</param>
        /// <exception cref="InvalidOperationException">事件 Key 为空或格式非法时抛出。</exception>
        public static void ValidateEventKey(string eventKey)
        {
            if (string.IsNullOrWhiteSpace(eventKey))
            {
                throw new InvalidOperationException("UIAnimationEvent 的 stringParameter 不能为空。");
            }

            if (eventKey[0] < 'A' || eventKey[0] > 'Z')
            {
                throw new InvalidOperationException($"UIAnimationEvent Key 必须以大写英文字母开头：{eventKey}。");
            }

            for (var index = 1; index < eventKey.Length; index++)
            {
                var character = eventKey[index];
                var isLetter = character >= 'A' && character <= 'Z' || character >= 'a' && character <= 'z';
                if (!isLetter && (character < '0' || character > '9'))
                {
                    throw new InvalidOperationException(
                        $"UIAnimationEvent Key 只能包含英文字母和数字：{eventKey}。");
                }
            }

            if (!IsDefinedEventKey(eventKey))
            {
                throw new InvalidOperationException($"UIAnimationEvent Key 未在框架中定义：{eventKey}。");
            }
        }

        private static bool IsDefinedEventKey(string eventKey)
        {
            var eventKeys = UIAnimationEventKeys.All;
            for (var index = 0; index < eventKeys.Count; index++)
            {
                if (string.Equals(eventKeys[index], eventKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsDefinedCustomAnimationKey(string animationKey)
        {
            var animationKeys = UIAnimationKeys.Custom;
            for (var index = 0; index < animationKeys.Count; index++)
            {
                if (string.Equals(animationKeys[index], animationKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
