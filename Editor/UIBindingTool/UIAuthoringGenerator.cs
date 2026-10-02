using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AlloyFramework.UI;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace AlloyFramework.Editor
{
    [InitializeOnLoad]
    internal static class UIAuthoringGenerator
    {
        private const string PendingKey = "AlloyFramework.UI.Authoring.Pending";
        private const string DefinitionFolder = "Assets/Scripts/Hotfix/UI/Generated";
        private const string HotfixFolder = "Assets/Scripts/Hotfix";

        [Serializable]
        private sealed class PendingGeneration
        {
            public string PrefabGuid;
            public string ViewPath;
            public string ViewTypeName;
            public string BindingFolder;
        }

        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
            "checked", "class", "const", "continue", "decimal", "default", "delegate", "do",
            "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
            "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
            "interface", "internal", "is", "lock", "long", "namespace", "new", "null",
            "object", "operator", "out", "override", "params", "private", "protected",
            "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
            "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
            "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
            "virtual", "void", "volatile", "while"
        };

        private static bool s_generatedInThisDomain;

        static UIAuthoringGenerator()
        {
            EditorApplication.delayCall += FinishPending;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
        }

        internal static void Generate(UIAuthoringSettings settings)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出运行模式。 ");
            if (!string.IsNullOrEmpty(SessionState.GetString(PendingKey, string.Empty)))
                throw new InvalidOperationException("上一个 UI 正在等待脚本编译，请稍后再生成。 ");

            var prefabPath = AssetDatabase.GUIDToAssetPath(settings.PrefabGuid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null || !prefabPath.StartsWith("Assets/Res/Prefabs/UI/", StringComparison.Ordinal))
                throw new InvalidOperationException("请选择 Assets/Res/Prefabs/UI 内的业务 UI 预制体。 ");
            if (prefab.GetComponentInChildren<UIRoot>(true) != null)
                throw new InvalidOperationException("UIRoot 是框架预制体，不能生成业务 View。 ");

            ValidateIdentifier(settings.UIName, "UI 名称");
            foreach (var segment in settings.ScriptNamespace.Split('.'))
                ValidateIdentifier(segment, "命名空间");
            var viewFolder = ValidateFolder(settings.ViewFolder, "View 文件夹");
            var controllerFolder = ValidateFolder(settings.ControllerFolder, "Controller 文件夹");
            ValidateUniqueName(settings);

            var name = settings.UIName;
            var viewName = name + "View";
            var controllerName = name + "Controller";
            var viewPath = $"{viewFolder}/{viewName}.cs";
            var controllerPath = $"{controllerFolder}/{controllerName}.cs";
            var definitionPath = $"{DefinitionFolder}/{name}.UI.g.cs";
            var location = prefabPath.Substring("Assets/Res/".Length);
            location = location.Substring(0, location.Length - ".prefab".Length);

            var existingViews = prefab.GetComponents<UIView>();
            if (existingViews.Length > 1 || existingViews.Length == 1 &&
                existingViews[0].GetType().FullName != settings.ScriptNamespace + "." + viewName)
                throw new InvalidOperationException("预制体根节点已有不同的 UIView，请先检查，生成器不会删除组件。 ");
            EnsureWritableGeneratedFile(definitionPath);
            EnsureWritableGeneratedFile($"{viewFolder}/Generated/{viewName}.Binding.g.cs");

            Directory.CreateDirectory(viewFolder);
            Directory.CreateDirectory(controllerFolder);
            Directory.CreateDirectory(DefinitionFolder);
            UIBindingGenerator.GenerateSource(prefab, settings.ScriptNamespace, viewName,
                viewFolder + "/Generated");
            WriteIfMissing(viewPath, BuildView(settings.ScriptNamespace, viewName));
            WriteIfMissing(controllerPath, BuildController(settings.ScriptNamespace, viewName, controllerName));
            File.WriteAllText(definitionPath,
                BuildDefinition(settings, location, viewName, controllerName), new UTF8Encoding(false));
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            var pending = new PendingGeneration
            {
                PrefabGuid = settings.PrefabGuid,
                ViewPath = viewPath,
                ViewTypeName = settings.ScriptNamespace + "." + viewName,
                BindingFolder = viewFolder + "/Generated"
            };
            SessionState.SetString(PendingKey, JsonUtility.ToJson(pending));
            s_generatedInThisDomain = true;
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EditorApplication.delayCall += FinishPending;
        }

        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            if (string.IsNullOrEmpty(SessionState.GetString(PendingKey, string.Empty))) return;
            foreach (var message in messages)
            {
                if (message.type != CompilerMessageType.Error) continue;
                SessionState.SetString(PendingKey, string.Empty);
                Debug.LogError($"UI 脚本编译失败，未修改预制体。请修复编译错误后重新生成。\n{message.message}");
                return;
            }
        }

        private static void FinishPending()
        {
            var json = SessionState.GetString(PendingKey, string.Empty);
            if (string.IsNullOrEmpty(json) || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            var pending = JsonUtility.FromJson<PendingGeneration>(json);
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(pending.ViewPath);
            var viewType = script != null ? script.GetClass() : null;
            if (viewType == null)
            {
                if (!s_generatedInThisDomain)
                {
                    SessionState.SetString(PendingKey, string.Empty);
                    Debug.LogError($"未找到 View 类型 {pending.ViewTypeName}，请检查脚本并重新生成。 ");
                }
                return;
            }

            try
            {
                if (viewType.FullName != pending.ViewTypeName || viewType.IsAbstract ||
                    !typeof(UIView).IsAssignableFrom(viewType))
                    throw new InvalidOperationException($"{pending.ViewPath} 必须声明 {pending.ViewTypeName} : UIView。");

                var prefabPath = AssetDatabase.GUIDToAssetPath(pending.PrefabGuid);
                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    var views = root.GetComponents<UIView>();
                    if (views.Length > 1 || views.Length == 1 && views[0].GetType() != viewType)
                        throw new InvalidOperationException("预制体根节点已有不同的 UIView，未修改预制体。 ");
                    if (views.Length == 0) root.AddComponent(viewType);
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }

                UIBindingGenerator.Generate(prefabPath, pending.BindingFolder);
                var settingsPath = UIAuthoringSettings.GetAssetPath(pending.PrefabGuid);
                var settings = AssetDatabase.LoadAssetAtPath<UIAuthoringSettings>(settingsPath);
                settings?.MarkGenerated();
                AssetDatabase.SaveAssets();
                SessionState.SetString(PendingKey, string.Empty);
                Debug.Log($"UI 已生成并绑定：{prefabPath}");
            }
            catch (Exception exception)
            {
                SessionState.SetString(PendingKey, string.Empty);
                Debug.LogException(exception);
            }
        }

        private static string ValidateFolder(string path, string label)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException($"{label}不能为空。 ");
            var project = Directory.GetParent(Application.dataPath)?.FullName ??
                          throw new InvalidOperationException("无法确定 Unity 项目路径。 ");
            var full = Path.GetFullPath(Path.Combine(project, path));
            var hotfix = Path.GetFullPath(Path.Combine(project, HotfixFolder));
            if (!full.StartsWith(hotfix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{label}必须位于 {HotfixFolder} 内。 ");
            return "Assets" + full.Substring(Application.dataPath.Length).Replace('\\', '/');
        }

        private static void ValidateIdentifier(string value, string label)
        {
            if (string.IsNullOrEmpty(value) || Keywords.Contains(value) ||
                !(char.IsLetter(value[0]) || value[0] == '_'))
                throw new InvalidOperationException($"{label}不是有效的 C# 标识符：{value}");
            for (var index = 1; index < value.Length; index++)
                if (!char.IsLetterOrDigit(value[index]) && value[index] != '_')
                    throw new InvalidOperationException($"{label}不是有效的 C# 标识符：{value}");
        }

        private static void ValidateUniqueName(UIAuthoringSettings settings)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:UIAuthoringSettings"))
            {
                var other = AssetDatabase.LoadAssetAtPath<UIAuthoringSettings>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (other != null && other != settings && other.UIName == settings.UIName)
                    throw new InvalidOperationException($"UI 名称 {settings.UIName} 已用于另一个预制体。 ");
            }
        }

        private static void EnsureWritableGeneratedFile(string path)
        {
            if (!File.Exists(path)) return;
            using (var reader = new StreamReader(path))
                if (reader.ReadLine() != "// <auto-generated />")
                    throw new InvalidOperationException($"文件已有手写内容，不会覆盖：{path}");
        }

        private static void WriteIfMissing(string path, string content)
        {
            if (!File.Exists(path)) File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private static string BuildView(string scriptNamespace, string viewName) =>
            $"using AlloyFramework.UI;\n\nnamespace {scriptNamespace}\n{{\n    public sealed partial class {viewName} : UIView\n    {{\n    }}\n}}\n";

        private static string BuildController(string scriptNamespace, string viewName, string controllerName) =>
            $"using AlloyFramework.UI;\n\nnamespace {scriptNamespace}\n{{\n    public sealed class {controllerName} : UIController<{viewName}>\n    {{\n    }}\n}}\n";

        private static string BuildDefinition(UIAuthoringSettings settings, string location,
            string viewName, string controllerName)
        {
            var source = new StringBuilder();
            source.AppendLine("// <auto-generated />");
            source.AppendLine("// 由 AlloyFramework UI 生成器生成，切勿手动修改；请在 UI 生成面板中调整配置后重新生成。");
            source.AppendLine("using AlloyFramework.UI;");
            source.AppendLine();
            source.AppendLine($"namespace {settings.ScriptNamespace}");
            source.AppendLine("{");
            source.AppendLine("    public static partial class GameUI");
            source.AppendLine("    {");
            source.AppendLine($"        public static readonly UIDefinition<{viewName}, {controllerName}> {settings.UIName} =");
            source.AppendLine($"            new UIDefinitionBuilder<{viewName}, {controllerName}>(\"{settings.UIName}\")");
            source.AppendLine($"                .Location(\"{Escape(location)}\")");
            source.AppendLine($"                .Layer(UILayer.{settings.Layer})");
            source.AppendLine($"                .Layout(UILayoutMode.{settings.Layout})");
            source.AppendLine($"                .Background(UIBackgroundMode.{settings.Background})");
            source.AppendLine($"                .Input(UIInputMode.{settings.Input})");
            source.AppendLine($"                .Cache(UICacheMode.{settings.Cache})");
            source.AppendLine($"                .OpenMode(UIOpenMode.{settings.OpenMode})");
            source.AppendLine($"                .Navigation(UINavigationMode.{settings.Navigation})");
            source.AppendLine($"                .PauseCovered({(settings.PauseCovered ? "true" : "false")})");
            source.AppendLine("                .Build();");
            source.AppendLine("    }");
            source.AppendLine("}");
            return source.ToString();
        }

        private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
