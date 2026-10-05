using System;
using System.Collections.Generic;
using AlloyFramework.UI;
using UnityEngine;

namespace AlloyFramework.Editor
{
    internal static class UIAnimationValidation
    {
        internal static List<string> GetErrors(UIAnimationPlayer player)
        {
            var errors = new List<string>();
            var definitionsByKey = new Dictionary<string, UIAnimationDefinition>(StringComparer.Ordinal);
            ValidateAnimations(player.Animations, definitionsByKey, errors);
            ValidateAnimationEvents(player.AnimationEvents, definitionsByKey, errors);
            return errors;
        }

        internal static void ValidateOrThrow(UIAnimationPlayer player)
        {
            var errors = GetErrors(player);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(string.Join("\n", errors));
            }
        }

        private static void ValidateAnimations(
            IReadOnlyList<UIAnimationDefinition> definitions,
            IDictionary<string, UIAnimationDefinition> definitionsByKey,
            ICollection<string> errors)
        {
            var constantNames = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < definitions.Count; index++)
            {
                var definition = definitions[index];
                var label = $"Animation[{index}]";
                if (!ValidateDefinition(definition, label, errors))
                {
                    continue;
                }

                ValidateAnimationKey(definition, label, errors);
                if (definitionsByKey.ContainsKey(definition.Key))
                {
                    errors.Add($"UI 动效 Key 重复：{definition.Key}。");
                    continue;
                }

                definitionsByKey.Add(definition.Key, definition);
                if (definition.Key == UIAnimationKeys.OPEN || definition.Key == UIAnimationKeys.CLOSE)
                {
                    continue;
                }

                var constantName = definition.Key.ToUpperInvariant();
                if (!constantNames.Add(constantName))
                {
                    errors.Add($"自定义动效常量名冲突：{definition.Key} -> {constantName}。");
                }
            }
        }

        private static bool ValidateDefinition(
            UIAnimationDefinition definition,
            string label,
            ICollection<string> errors)
        {
            if (definition == null || !definition.IsConfigured)
            {
                errors.Add($"{label} 动效配置为空。");
                return false;
            }

            if (definition.Clip == null)
            {
                errors.Add($"{label} 缺少 AnimationClip 引用。");
                return false;
            }

            if (!definition.Clip.legacy)
            {
                errors.Add($"{label} 的 Clip {definition.Clip.name} 必须启用 Legacy。");
            }

            if (definition.Clip.length <= 0f)
            {
                errors.Add($"{label} 的 Clip 时长必须大于 0。");
            }

            if (definition.Clip.frameRate <= 0f)
            {
                errors.Add($"{label} 的 Clip 帧率必须大于 0。");
            }

            return true;
        }

        private static void ValidateAnimationKey(
            UIAnimationDefinition definition,
            string label,
            ICollection<string> errors)
        {
            if (definition.Key == UIAnimationKeys.OPEN || definition.Key == UIAnimationKeys.CLOSE)
            {
                if (definition.Loop)
                {
                    errors.Add($"{definition.Key} 生命周期动效不允许循环。");
                }

                return;
            }

            try
            {
                UIAnimationValidationRules.ValidateKey(definition.Key);
            }
            catch (Exception exception)
            {
                errors.Add($"{label}：{exception.Message}");
            }
        }

        private static void ValidateAnimationEvents(
            IReadOnlyList<UIAnimationEventDefinition> eventDefinitions,
            IReadOnlyDictionary<string, UIAnimationDefinition> definitionsByKey,
            ICollection<string> errors)
        {
            for (var index = 0; index < eventDefinitions.Count; index++)
            {
                var eventDefinition = eventDefinitions[index];
                var label = $"AnimationEvent[{index}]";
                if (eventDefinition == null)
                {
                    errors.Add($"{label} 配置为空。");
                    continue;
                }

                if (!definitionsByKey.TryGetValue(
                        eventDefinition.AnimationKey,
                        out var animationDefinition))
                {
                    errors.Add($"{label} 引用了未配置的 Animation Key：{eventDefinition.AnimationKey}。");
                    continue;
                }

                try
                {
                    UIAnimationValidationRules.ValidateEventKey(eventDefinition.EventKey);
                }
                catch (Exception exception)
                {
                    errors.Add($"{label}：{exception.Message}");
                }

                if (eventDefinition.Time < 0f || eventDefinition.Time > animationDefinition.Clip.length)
                {
                    errors.Add(
                        $"{label} 时间超出 Clip 范围：Time={eventDefinition.Time}，" +
                        $"Duration={animationDefinition.Clip.length}。");
                }
            }
        }
    }
}
