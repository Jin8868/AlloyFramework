using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AlloyFramework.Editor
{
    public sealed class UIBindingRule
    {
        public UIBindingRule(string prefix, Type componentType)
        {
            if (string.IsNullOrWhiteSpace(prefix) || prefix.Length < 2 ||
                !prefix.EndsWith("_", StringComparison.Ordinal))
                throw new ArgumentException("Binding prefix must end with '_'.", nameof(prefix));
            if (componentType != typeof(GameObject) && !typeof(Component).IsAssignableFrom(componentType))
                throw new ArgumentException("Binding type must be GameObject or a Component.", nameof(componentType));
            Prefix = prefix;
            ComponentType = componentType;
        }

        public string Prefix { get; }
        public Type ComponentType { get; }
    }

    // A project can derive from this class in an Editor assembly. Its override may
    // call base.ConfigureRules(rules) to keep the framework defaults, then add or replace rules.
    public class UIBindingPrefixSettings
    {
        protected virtual void ConfigureRules(IList<UIBindingRule> rules)
        {
            rules.Add(new UIBindingRule("btn_", typeof(Button)));
            rules.Add(new UIBindingRule("txt_", typeof(TMP_Text)));
            rules.Add(new UIBindingRule("img_", typeof(Image)));
            rules.Add(new UIBindingRule("raw_", typeof(RawImage)));
            rules.Add(new UIBindingRule("input_", typeof(TMP_InputField)));
            rules.Add(new UIBindingRule("toggle_", typeof(Toggle)));
            rules.Add(new UIBindingRule("slider_", typeof(Slider)));
            rules.Add(new UIBindingRule("scroll_", typeof(ScrollRect)));
            rules.Add(new UIBindingRule("go_", typeof(GameObject)));
            rules.Add(new UIBindingRule("tf_", typeof(Transform)));
            rules.Add(new UIBindingRule("rt_", typeof(RectTransform)));
        }

        internal static IReadOnlyList<UIBindingRule> LoadRules()
        {
            var providers = new List<Type>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<UIBindingPrefixSettings>())
                if (!type.IsAbstract && !type.IsGenericTypeDefinition) providers.Add(type);
            if (providers.Count > 1)
                throw new InvalidOperationException("Multiple project UIBindingPrefixSettings providers were found.");

            var settings = providers.Count == 0
                ? new UIBindingPrefixSettings()
                : (UIBindingPrefixSettings)Activator.CreateInstance(providers[0]);
            var rules = new List<UIBindingRule>();
            settings.ConfigureRules(rules);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in rules)
            {
                if (rule == null) throw new InvalidOperationException("A binding rule is null.");
                if (!seen.Add(rule.Prefix))
                    throw new InvalidOperationException($"Duplicate binding prefix {rule.Prefix}.");
            }
            rules.Sort((left, right) => right.Prefix.Length.CompareTo(left.Prefix.Length));
            return rules;
        }
    }
}
