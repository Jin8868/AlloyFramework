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
        private sealed class BindingInfo
        {
            public string Name;
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
            Debug.Log($"Validated {guids.Length} UI prefabs.");
        }

        internal static void Generate(string path, string generatedDirectory)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponentInChildren<UIRoot>(true) != null)
                    throw new InvalidOperationException("UIRoot has no UIView bindings to generate.");
                var view = GetView(root, path);
                var bindings = Scan(root.transform);
                var container = root.GetComponent<UIBinding>() ?? root.AddComponent<UIBinding>();
                var serialized = new SerializedObject(container);
                var references = serialized.FindProperty("m_references");
                var paths = serialized.FindProperty("m_paths");
                references.arraySize = bindings.Count;
                paths.arraySize = bindings.Count;
                for (var index = 0; index < bindings.Count; index++)
                {
                    references.GetArrayElementAtIndex(index).objectReferenceValue = bindings[index].Reference;
                    paths.GetArrayElementAtIndex(index).stringValue = bindings[index].Path;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Directory.CreateDirectory(generatedDirectory);
                var sourcePath = $"{generatedDirectory}/{view.GetType().Name}.Binding.g.cs";
                File.WriteAllText(sourcePath, BuildSource(view.GetType().Namespace,
                    view.GetType().Name, bindings), new UTF8Encoding(false));
                PrefabUtility.SaveAsPrefabAsset(root, path);
                AssetDatabase.ImportAsset(sourcePath);
                AssetDatabase.SaveAssets();
                Debug.Log($"Generated {bindings.Count} bindings for {path}.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        internal static void GenerateSource(GameObject prefab, string scriptNamespace,
            string viewName, string generatedDirectory)
        {
            var bindings = Scan(prefab.transform);
            Directory.CreateDirectory(generatedDirectory);
            var sourcePath = $"{generatedDirectory}/{viewName}.Binding.g.cs";
            File.WriteAllText(sourcePath, BuildSource(scriptNamespace, viewName, bindings),
                new UTF8Encoding(false));
        }

        private static void Validate(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponentInChildren<UIRoot>(true) != null) return;
                GetView(root, path);
                var expected = Scan(root.transform);
                var container = root.GetComponent<UIBinding>() ??
                    throw new InvalidOperationException("UIBinding is missing.");
                var serialized = new SerializedObject(container);
                var references = serialized.FindProperty("m_references");
                var paths = serialized.FindProperty("m_paths");
                if (references.arraySize != expected.Count || paths.arraySize != expected.Count)
                    throw new InvalidOperationException("Binding count does not match the prefab hierarchy.");
                for (var index = 0; index < expected.Count; index++)
                {
                    if (references.GetArrayElementAtIndex(index).objectReferenceValue != expected[index].Reference ||
                        paths.GetArrayElementAtIndex(index).stringValue != expected[index].Path)
                        throw new InvalidOperationException($"Binding mismatch at {expected[index].Path}.");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static UIView GetView(GameObject root, string path)
        {
            var views = root.GetComponents<UIView>();
            if (views.Length != 1)
                throw new InvalidOperationException($"{path} must have exactly one UIView on its root.");
            return views[0];
        }

        private static List<BindingInfo> Scan(Transform root)
        {
            var result = new List<BindingInfo>();
            var rules = UIBindingPrefixSettings.LoadRules();
            Visit(root, root.name, result, rules);
            result.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
            var duplicates = result.GroupBy(item => item.Name).FirstOrDefault(group => group.Count() > 1);
            if (duplicates != null)
                throw new InvalidOperationException($"Duplicate binding field {duplicates.Key}.");
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
                    throw new InvalidOperationException($"Binding name is empty at {path}.");
                var stem = rule.Prefix.Substring(0, rule.Prefix.Length - 1);
                var property = string.Concat(char.ToUpperInvariant(stem[0]), stem.Substring(1),
                    string.Concat(suffix.Split('_').Where(part => part.Length > 0)
                        .Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1))));
                if (!char.IsLetter(property[0]) && property[0] != '_')
                    throw new InvalidOperationException($"Invalid binding field name {property} at {path}.");
                for (var characterIndex = 1; characterIndex < property.Length; characterIndex++)
                    if (!char.IsLetterOrDigit(property[characterIndex]) && property[characterIndex] != '_')
                        throw new InvalidOperationException($"Invalid binding field name {property} at {path}.");
                var component = rule.ComponentType == typeof(GameObject)
                    ? (UnityEngine.Object)node.gameObject : node.GetComponent(rule.ComponentType);
                if (component == null)
                    throw new InvalidOperationException($"{path} requires {rule.ComponentType.Name}.");
                result.Add(new BindingInfo { Name = property, Path = path, Type = rule.ComponentType, Reference = component });
                break;
            }
            for (var index = 0; index < node.childCount; index++)
            {
                var child = node.GetChild(index);
                Visit(child, path + "/" + child.name, result, rules);
            }
        }

        private static string BuildSource(string scriptNamespace, string viewName,
            List<BindingInfo> bindings)
        {
            var source = new StringBuilder();
            source.AppendLine("// <auto-generated />");
            source.AppendLine("// 由 AlloyFramework UI 生成器生成，切勿手动修改。");
            source.AppendLine("namespace " + scriptNamespace);
            source.AppendLine("{");
            source.AppendLine("    public sealed partial class " + viewName);
            source.AppendLine("    {");
            foreach (var binding in bindings)
                source.AppendLine($"        public {binding.Type.FullName} {binding.Name} {{ get; private set; }}");
            source.AppendLine("        protected override void BindComponents(AlloyFramework.UI.UIBinding binding)");
            source.AppendLine("        {");
            for (var index = 0; index < bindings.Count; index++)
            {
                var item = bindings[index];
                source.AppendLine($"            {item.Name} = binding.Get<{item.Type.FullName}>({index}, \"{item.Name}\");");
            }
            source.AppendLine("        }");
            source.AppendLine("    }");
            source.AppendLine("}");
            return source.ToString();
        }
    }
}
