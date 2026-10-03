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
        private const string BlockStart = "        // <alloy-generated-bindings>";
        private const string BlockEnd = "        // </alloy-generated-bindings>";

        private sealed class BindingInfo
        {
            public string Name;
            public string FieldName;
            public string Path;
            public Type Type;
            public UnityEngine.Object Reference;
        }

        [MenuItem("AlloyFramework/UI/校验全部界面绑定", false, 102)]
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
            var block = BuildBindingBlock(Scan(prefab.transform));
            Directory.CreateDirectory(Path.GetDirectoryName(viewPath) ?? string.Empty);
            if (!File.Exists(viewPath))
            {
                File.WriteAllText(viewPath, BuildView(scriptNamespace, viewName, block),
                    new UTF8Encoding(false));
                return;
            }

            var source = File.ReadAllText(viewPath);
            var start = source.IndexOf(BlockStart, StringComparison.Ordinal);
            var end = source.IndexOf(BlockEnd, StringComparison.Ordinal);
            if (start < 0 || end < 0 || end < start ||
                source.IndexOf(BlockStart, start + BlockStart.Length, StringComparison.Ordinal) >= 0 ||
                source.IndexOf(BlockEnd, end + BlockEnd.Length, StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException(
                    $"View 文件缺少唯一的自动绑定区块，生成器不会覆盖手写代码：{viewPath}");

            end += BlockEnd.Length;
            File.WriteAllText(viewPath,
                source.Substring(0, start) + block + source.Substring(end), new UTF8Encoding(false));
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

        private static string BuildView(string scriptNamespace, string viewName, string block)
        {
            var source = new StringBuilder();
            source.AppendLine("using AlloyFramework.UI;");
            source.AppendLine();
            source.AppendLine($"namespace {scriptNamespace}");
            source.AppendLine("{");
            source.AppendLine($"    public sealed partial class {viewName} : UIView");
            source.AppendLine("    {");
            source.AppendLine(block);
            source.AppendLine("    }");
            source.AppendLine("}");
            return source.ToString();
        }

        private static string BuildBindingBlock(IReadOnlyList<BindingInfo> bindings)
        {
            var source = new StringBuilder();
            source.AppendLine(BlockStart);
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
            source.Append(BlockEnd);
            return source.ToString();
        }

        private static string GetTypeName(Type type) => type.FullName?.Replace('+', '.') ?? type.Name;
    }
}
