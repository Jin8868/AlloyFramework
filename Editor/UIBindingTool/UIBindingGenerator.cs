using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AlloyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace AlloyFramework.Editor
{
    internal static class UIBindingGenerator
    {
        private const string BLOCKSTART = "        // <alloy-generated-bindings>";
        private const string BLOCKEND = "        // </alloy-generated-bindings>";
        private const string ANIMATIONBLOCKSTART = "        // <alloy-generated-animation-keys>";
        private const string ANIMATIONBLOCKEND = "        // </alloy-generated-animation-keys>";

        private sealed class BindingInfo
        {
            public string Name;
            public string FieldName;
            public string Path;
            public Type Type;
            public UnityEngine.Object Reference;
        }

        [MenuItem("★AlloyFramework★/UI/校验全部界面绑定", false, 102)]
        private static void ValidateAll()
        {
            var failures = new List<string>();
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Res/Prefabs/UI" });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                try { Validate(path); }
                catch (Exception exception) { failures.Add($"{path}: {exception.Message}"); }
            }
            if (failures.Count > 0)
                throw new InvalidOperationException(string.Join("\n", failures));
            Debug.Log($"已校验 {guids.Length} 个 UI 预制体绑定。");
        }

        internal static void GenerateViewSource(GameObject prefab, string scriptNamespace,
            string viewName, string viewPath)
        {
            var bindingBlock = BuildBindingBlock(Scan(prefab.transform));
            var animationBlock = BuildAnimationKeyBlock(ScanAnimationKeys(prefab));
            Directory.CreateDirectory(Path.GetDirectoryName(viewPath) ?? string.Empty);
            if (!File.Exists(viewPath))
            {
                File.WriteAllText(viewPath, BuildView(scriptNamespace, viewName, bindingBlock, animationBlock),
                    new UTF8Encoding(false));
                return;
            }

            var source = File.ReadAllText(viewPath);
            source = ReplaceRequiredBlock(source, BLOCKSTART, BLOCKEND, bindingBlock, viewPath);
            source = ReplaceOrInsertAnimationBlock(source, animationBlock, viewPath);
            File.WriteAllText(viewPath, source, new UTF8Encoding(false));
        }

        internal static void ApplyBindings(string prefabPath)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                if (root.GetComponentInChildren<UIRoot>(true) != null)
                    throw new InvalidOperationException("UIRoot 不需要生成业务界面绑定。");
                var view = GetView(root, prefabPath);
                var bindings = Scan(root.transform);
                WriteBindings(view, bindings);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"已为 {prefabPath} 写入 {bindings.Count} 个强类型绑定。");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void Validate(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponentInChildren<UIRoot>(true) != null) return;
                var view = GetView(root, path);
                var animationPlayer = root.GetComponent<UIAnimationPlayer>();
                if (animationPlayer != null)
                {
                    UIAnimationValidation.ValidateOrThrow(animationPlayer);
                }

                foreach (var binding in Scan(root.transform))
                {
                    var property = new SerializedObject(view).FindProperty(binding.FieldName);
                    if (property == null)
                        throw new InvalidOperationException(
                            $"{view.GetType().Name} 缺少生成字段 {binding.FieldName}，请重新生成。");
                    if (property.objectReferenceValue != binding.Reference)
                        throw new InvalidOperationException($"绑定不匹配：{binding.Path} -> {binding.Name}。");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static UIView GetView(GameObject root, string path)
        {
            var views = root.GetComponents<UIView>();
            if (views.Length != 1)
                throw new InvalidOperationException($"{path} 的根节点必须且只能挂载一个 UIView。");
            return views[0];
        }

        private static void WriteBindings(UIView view, IReadOnlyList<BindingInfo> bindings)
        {
            var serializedView = new SerializedObject(view);
            foreach (var binding in bindings)
            {
                var property = serializedView.FindProperty(binding.FieldName);
                if (property == null)
                    throw new InvalidOperationException(
                        $"{view.GetType().Name} 缺少生成字段 {binding.FieldName}，请等待编译完成后重新生成。");
                property.objectReferenceValue = binding.Reference;
            }
            serializedView.ApplyModifiedPropertiesWithoutUndo();
        }

        private static List<BindingInfo> Scan(Transform root)
        {
            var result = new List<BindingInfo>();
            Visit(root, root.name, result, UIBindingPrefixSettings.LoadRules());
            result.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
            var duplicate = result.GroupBy(item => item.Name).FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
                throw new InvalidOperationException($"绑定属性重名：{duplicate.Key}。");
            return result;
        }

        private static void Visit(Transform node, string path, List<BindingInfo> result,
            IReadOnlyList<UIBindingRule> rules)
        {
            foreach (var rule in rules)
            {
                if (!node.name.StartsWith(rule.Prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var suffix = node.name.Substring(rule.Prefix.Length);
                if (string.IsNullOrWhiteSpace(suffix))
                    throw new InvalidOperationException($"绑定名称不能为空：{path}。");
                var stem = rule.Prefix.Substring(0, rule.Prefix.Length - 1);
                var property = string.Concat(char.ToUpperInvariant(stem[0]), stem.Substring(1),
                    string.Concat(suffix.Split('_').Where(part => part.Length > 0)
                        .Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1))));
                ValidateIdentifier(property, path);
                var component = rule.ComponentType == typeof(GameObject)
                    ? (UnityEngine.Object)node.gameObject : node.GetComponent(rule.ComponentType);
                if (component == null)
                    throw new InvalidOperationException($"{path} 需要组件 {rule.ComponentType.Name}。");
                result.Add(new BindingInfo
                {
                    Name = property,
                    FieldName = "m_" + char.ToLowerInvariant(property[0]) + property.Substring(1),
                    Path = path,
                    Type = rule.ComponentType,
                    Reference = component
                });
                break;
            }
            for (var index = 0; index < node.childCount; index++)
            {
                var child = node.GetChild(index);
                Visit(child, path + "/" + child.name, result, rules);
            }
        }

        private static void ValidateIdentifier(string value, string path)
        {
            if (value.Length == 0 || !char.IsLetter(value[0]) && value[0] != '_')
                throw new InvalidOperationException($"绑定字段名称无效：{value}，节点：{path}。");
            for (var index = 1; index < value.Length; index++)
                if (!char.IsLetterOrDigit(value[index]) && value[index] != '_')
                    throw new InvalidOperationException($"绑定字段名称无效：{value}，节点：{path}。");
        }

        private static string BuildView(
            string scriptNamespace,
            string viewName,
            string bindingBlock,
            string animationBlock)
        {
            var source = new StringBuilder();
            source.AppendLine("using AlloyFramework.UI;");
            source.AppendLine();
            source.AppendLine($"namespace {scriptNamespace}");
            source.AppendLine("{");
            source.AppendLine($"    public sealed partial class {viewName} : UIView");
            source.AppendLine("    {");
            source.AppendLine(bindingBlock);
            source.AppendLine();
            source.AppendLine(animationBlock);
            source.AppendLine("    }");
            source.AppendLine("}");
            return source.ToString();
        }

        private static string BuildBindingBlock(IReadOnlyList<BindingInfo> bindings)
        {
            var source = new StringBuilder();
            source.AppendLine(BLOCKSTART);
            source.AppendLine("        // 此区块由 AlloyFramework UI 生成器维护，请勿手动修改。");
            foreach (var binding in bindings)
            {
                source.AppendLine("        [UnityEngine.SerializeField, AlloyFramework.UI.UIBindingReference]");
                source.AppendLine($"        private {GetTypeName(binding.Type)} {binding.FieldName};");
                source.AppendLine();
                source.AppendLine($"        public {GetTypeName(binding.Type)} {binding.Name} => {binding.FieldName};");
                source.AppendLine();
            }
            if (bindings.Count > 0) source.Length -= Environment.NewLine.Length;
            source.Append(BLOCKEND);
            return source.ToString();
        }

        private static List<string> ScanAnimationKeys(GameObject prefab)
        {
            var result = new List<string>();
            var animationPlayer = prefab.GetComponent<UIAnimationPlayer>();
            if (animationPlayer == null)
            {
                return result;
            }

            UIAnimationValidation.ValidateOrThrow(animationPlayer);
            var sourceKeys = new HashSet<string>(StringComparer.Ordinal);
            var constantNames = new HashSet<string>(StringComparer.Ordinal);
            var definitions = animationPlayer.Animations;
            for (var index = 0; index < definitions.Count; index++)
            {
                var definition = definitions[index];
                if (definition == null)
                {
                    throw new InvalidOperationException($"动效配置为空：索引={index}，Prefab={prefab.name}。");
                }

                if (definition.Key == UIAnimationKeys.OPEN || definition.Key == UIAnimationKeys.CLOSE)
                {
                    continue;
                }

                UIAnimationValidationRules.ValidateKey(definition.Key);
                if (!sourceKeys.Add(definition.Key))
                {
                    throw new InvalidOperationException($"自定义动效 Key 重复：{definition.Key}。");
                }

                var constantName = definition.Key.ToUpperInvariant();
                if (!constantNames.Add(constantName))
                {
                    throw new InvalidOperationException(
                        $"自定义动效 Key 转换为常量名后冲突：{definition.Key} -> {constantName}。");
                }

                result.Add(definition.Key);
            }

            return result;
        }

        private static string BuildAnimationKeyBlock(IReadOnlyList<string> animationKeys)
        {
            var source = new StringBuilder();
            source.AppendLine(ANIMATIONBLOCKSTART);
            source.AppendLine("        // 此区块由 AlloyFramework UI 生成器维护，请勿手动修改。");
            source.AppendLine("        public static class AnimationKeys");
            source.AppendLine("        {");
            for (var index = 0; index < animationKeys.Count; index++)
            {
                var animationKey = animationKeys[index];
                source.AppendLine("            /// <summary>");
                source.AppendLine($"            /// {animationKey} 自定义动效 Key。");
                source.AppendLine("            /// </summary>");
                source.AppendLine(
                    $"            public const string {animationKey.ToUpperInvariant()} = \"{animationKey}\";");
                if (index < animationKeys.Count - 1)
                {
                    source.AppendLine();
                }
            }

            source.AppendLine("        }");
            source.Append(ANIMATIONBLOCKEND);
            return source.ToString();
        }

        private static string ReplaceRequiredBlock(
            string source,
            string blockStart,
            string blockEnd,
            string replacement,
            string viewPath)
        {
            var start = source.IndexOf(blockStart, StringComparison.Ordinal);
            var end = source.IndexOf(blockEnd, StringComparison.Ordinal);
            if (start < 0 || end < 0 || end < start ||
                source.IndexOf(blockStart, start + blockStart.Length, StringComparison.Ordinal) >= 0 ||
                source.IndexOf(blockEnd, end + blockEnd.Length, StringComparison.Ordinal) >= 0)
            {
                throw new InvalidOperationException(
                    $"View 文件缺少唯一的自动绑定区块，生成器不会覆盖手写代码：{viewPath}");
            }

            end += blockEnd.Length;
            return source.Substring(0, start) + replacement + source.Substring(end);
        }

        private static string ReplaceOrInsertAnimationBlock(
            string source,
            string animationBlock,
            string viewPath)
        {
            var start = source.IndexOf(ANIMATIONBLOCKSTART, StringComparison.Ordinal);
            var end = source.IndexOf(ANIMATIONBLOCKEND, StringComparison.Ordinal);
            if (start < 0 && end < 0)
            {
                var bindingEnd = source.IndexOf(BLOCKEND, StringComparison.Ordinal) + BLOCKEND.Length;
                return source.Insert(bindingEnd, Environment.NewLine + Environment.NewLine + animationBlock);
            }

            if (start < 0 || end < 0 || end < start ||
                source.IndexOf(ANIMATIONBLOCKSTART, start + ANIMATIONBLOCKSTART.Length,
                    StringComparison.Ordinal) >= 0 ||
                source.IndexOf(ANIMATIONBLOCKEND, end + ANIMATIONBLOCKEND.Length,
                    StringComparison.Ordinal) >= 0)
            {
                throw new InvalidOperationException(
                    $"View 文件的自动动效 Key 区块不完整或不唯一：{viewPath}");
            }

            end += ANIMATIONBLOCKEND.Length;
            return source.Substring(0, start) + animationBlock + source.Substring(end);
        }

        private static string GetTypeName(Type type) => type.FullName?.Replace('+', '.') ?? type.Name;
    }
}
