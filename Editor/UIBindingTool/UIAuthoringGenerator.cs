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
        private const string DefinitionPath = "Assets/Scripts/Runtime/UI/Generated/GameUI.g.cs";
        private const string RuntimeFolder = "Assets/Scripts/Runtime";

        [Serializable]
        private sealed class PendingGeneration
        {
            public string PrefabGuid;
            public string ViewPath;
            public string ViewTypeName;
        }

        private sealed class DefinitionEntry
        {
            public UIAuthoringSettings Settings;
            public string Location;
            public string ViewName;
            public string ControllerName;
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
                throw new InvalidOperationException("请先退出运行模式。");
            if (!string.IsNullOrEmpty(SessionState.GetString(PendingKey, string.Empty)))
                throw new InvalidOperationException("上一个 UI 正在等待脚本编译，请稍后再生成。");

            var prefabPath = AssetDatabase.GUIDToAssetPath(settings.PrefabGuid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null ||
                !prefabPath.StartsWith("Assets/Res/Prefabs/UI/", StringComparison.Ordinal))
                throw new InvalidOperationException("请选择 Assets/Res/Prefabs/UI 内的业务 UI 预制体。");
            if (prefab.GetComponentInChildren<UIRoot>(true) != null)
                throw new InvalidOperationException("UIRoot 是框架预制体，不能生成业务 View。");

            ValidateIdentifier(settings.UIName, "UI 名称");
            foreach (var segment in settings.ScriptNamespace.Split('.'))
                ValidateIdentifier(segment, "命名空间");
            var viewFolder = ValidateFolder(settings.ViewFolder, "View 文件夹");
            var controllerFolder = ValidateFolder(settings.ControllerFolder, "Controller 文件夹");
            ValidateUniqueName(settings);

            var viewName = settings.UIName + "View";
            var controllerName = settings.UIName + "Controller";
            var viewPath = $"{viewFolder}/{viewName}.cs";
            var controllerPath = $"{controllerFolder}/{controllerName}.cs";
            var existingViews = prefab.GetComponents<UIView>();
            if (existingViews.Length > 1 || existingViews.Length == 1 &&
                existingViews[0].GetType().FullName != settings.ScriptNamespace + "." + viewName)
                throw new InvalidOperationException(
                    "预制体根节点已有不同的 UIView，请先检查；生成器不会删除组件。");

            EnsureWritableGeneratedFile(DefinitionPath);
            Directory.CreateDirectory(viewFolder);
            Directory.CreateDirectory(controllerFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(DefinitionPath) ?? string.Empty);
            UIBindingGenerator.GenerateViewSource(
                prefab, settings.ScriptNamespace, viewName, viewPath);
            WriteIfMissing(controllerPath,
                BuildController(settings.ScriptNamespace, viewName, controllerName));
            WriteDefinitions(settings);
            UIAuthoringSettings.Save();

            var pending = new PendingGeneration
            {
                PrefabGuid = settings.PrefabGuid,
                ViewPath = viewPath,
                ViewTypeName = settings.ScriptNamespace + "." + viewName
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
                Debug.LogError(
                    $"UI 脚本编译失败，尚未修改预制体。修复错误后请重新生成。\n{message.message}");
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
                    Debug.LogError($"未找到 View 类型 {pending.ViewTypeName}，请检查脚本后重新生成。");
                }
                return;
            }

            try
            {
                if (viewType.FullName != pending.ViewTypeName || viewType.IsAbstract ||
                    !typeof(UIView).IsAssignableFrom(viewType))
                    throw new InvalidOperationException(
                        $"{pending.ViewPath} 必须声明 {pending.ViewTypeName} : UIView。");

                var prefabPath = AssetDatabase.GUIDToAssetPath(pending.PrefabGuid);
                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    var views = root.GetComponents<UIView>();
                    if (views.Length > 1 || views.Length == 1 && views[0].GetType() != viewType)
                        throw new InvalidOperationException(
                            "预制体根节点已有不同的 UIView，生成器未修改预制体。");
                    if (views.Length == 0) root.AddComponent(viewType);
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }

                UIBindingGenerator.ApplyBindings(prefabPath);
                var settings = UIAuthoringSettings.TryLoad(pending.PrefabGuid);
                settings?.MarkGenerated();
                UIAuthoringSettings.Save();
                SessionState.SetString(PendingKey, string.Empty);
                Debug.Log($"UI 已生成并写入强类型绑定：{prefabPath}");
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
                throw new InvalidOperationException($"{label}不能为空。");
            var project = Directory.GetParent(Application.dataPath)?.FullName ??
                          throw new InvalidOperationException("无法确定 Unity 项目路径。");
            var full = Path.GetFullPath(Path.Combine(project, path));
            var runtime = Path.GetFullPath(Path.Combine(project, RuntimeFolder));
            if (!full.StartsWith(runtime + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{label}必须位于 {RuntimeFolder} 内。");
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
            foreach (var other in UIAuthoringSettings.All)
            {
                if (other != null && other != settings && other.UIName == settings.UIName)
                    throw new InvalidOperationException(
                        $"UI 名称 {settings.UIName} 已用于另一个预制体。");
            }
        }

        private static void EnsureWritableGeneratedFile(string path)
        {
            if (!File.Exists(path)) return;
            using (var reader = new StreamReader(path))
                if (reader.ReadLine() != "// <auto-generated />")
                    throw new InvalidOperationException($"文件已有手写内容，生成器不会覆盖：{path}");
        }

        private static void WriteIfMissing(string path, string content)
        {
            if (!File.Exists(path))
                File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private static string BuildController(string scriptNamespace, string viewName,
            string controllerName)
        {
            var source = new StringBuilder();
            source.AppendLine("using System.Threading;");
            source.AppendLine("using AlloyFramework.UI;");
            source.AppendLine("using Cysharp.Threading.Tasks;");
            source.AppendLine();
            source.AppendLine($"namespace {scriptNamespace}");
            source.AppendLine("{");
            source.AppendLine($"    public sealed class {controllerName} : UIController<{viewName}>");
            source.AppendLine("    {");
            source.AppendLine("        // Controller 和 View 首次创建后调用一次。");
            source.AppendLine("        protected override void OnCreate()");
            source.AppendLine("        {");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        // 每次打开前调用，可以异步准备数据和资源。");
            source.AppendLine("        protected override UniTask OnPrepareAsync(");
            source.AppendLine("            UIEmptyData data, CancellationToken cancellationToken)");
            source.AppendLine("        {");
            source.AppendLine("            return UniTask.CompletedTask;");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        // 每次打开时初始化界面数据。");
            source.AppendLine("        protected override void OnInitData(UIEmptyData data)");
            source.AppendLine("        {");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        // 已打开的界面以刷新模式再次打开时调用。");
            source.AppendLine("        protected override void OnRefresh(UIEmptyData data)");
            source.AppendLine("        {");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        // 开始播放打开动画前调用。");
            source.AppendLine("        protected override void OnStartOpenAnimation()");
            source.AppendLine("        {");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        // 返回打开动画任务；没有动画时直接返回已完成任务。");
            source.AppendLine("        protected override UniTask OnOpenAnimationAsync(");
            source.AppendLine("            CancellationToken cancellationToken)");
            source.AppendLine("        {");
            source.AppendLine("            return UniTask.CompletedTask;");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        // 打开动画执行完成后调用。");
            source.AppendLine("        protected override void OnEndOpenAnimation()");
            source.AppendLine("        {");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        // 界面完成打开并可以交互后调用。");
            source.AppendLine("        protected override void OnOpen()");
            source.AppendLine("        {");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        // 界面被更高层界面暂停时调用。");
            source.AppendLine("        protected override void OnPause()");
            source.AppendLine("        {");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        // 界面从暂停状态恢复时调用。");
            source.AppendLine("        protected override void OnResume()");
            source.AppendLine("        {");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        // 界面开始关闭时调用。");
            source.AppendLine("        protected override void OnClose()");
            source.AppendLine("        {");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        // 开始播放关闭动画前调用。");
            source.AppendLine("        protected override void OnStartCloseAnimation()");
            source.AppendLine("        {");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        // 返回关闭动画任务；没有动画时直接返回已完成任务。");
            source.AppendLine("        protected override UniTask OnCloseAnimationAsync()");
            source.AppendLine("        {");
            source.AppendLine("            return UniTask.CompletedTask;");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        // 关闭动画执行完成后调用。");
            source.AppendLine("        protected override void OnEndCloseAnimation()");
            source.AppendLine("        {");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        // Controller 被彻底释放时调用。");
            source.AppendLine("        protected override void OnDispose()");
            source.AppendLine("        {");
            source.AppendLine("        }");
            source.AppendLine("    }");
            source.AppendLine("}");
            return source.ToString();
        }

        private static void WriteDefinitions(UIAuthoringSettings current)
        {
            var entries = new List<DefinitionEntry>();
            foreach (var settings in UIAuthoringSettings.All)
            {
                if (settings == null || !settings.HasGenerated &&
                    settings.PrefabGuid != current.PrefabGuid)
                    continue;
                if (!string.Equals(settings.ScriptNamespace, current.ScriptNamespace,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"统一的 GameUI 要求所有界面使用命名空间 {current.ScriptNamespace}；" +
                        $"{settings.UIName} 当前使用 {settings.ScriptNamespace}。");

                var prefabPath = AssetDatabase.GUIDToAssetPath(settings.PrefabGuid);
                if (string.IsNullOrEmpty(prefabPath) ||
                    !prefabPath.StartsWith("Assets/Res/", StringComparison.Ordinal) ||
                    !prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"UI {settings.UIName} 对应的预制体不存在。");

                var location = prefabPath.Substring("Assets/Res/".Length);
                location = location.Substring(0, location.Length - ".prefab".Length);
                entries.Add(new DefinitionEntry
                {
                    Settings = settings,
                    Location = location,
                    ViewName = settings.UIName + "View",
                    ControllerName = settings.UIName + "Controller"
                });
            }

            entries.Sort((left, right) =>
                string.CompareOrdinal(left.Settings.UIName, right.Settings.UIName));
            File.WriteAllText(DefinitionPath,
                BuildDefinitions(current.ScriptNamespace, entries), new UTF8Encoding(false));
        }

        private static string BuildDefinitions(
            string scriptNamespace,
            IReadOnlyList<DefinitionEntry> entries)
        {
            var source = new StringBuilder();
            source.AppendLine("// <auto-generated />");
            source.AppendLine("// 由 AlloyFramework UI 生成器生成，请勿手动修改。");
            source.AppendLine("using AlloyFramework.UI;");
            source.AppendLine();
            source.AppendLine($"namespace {scriptNamespace}");
            source.AppendLine("{");
            source.AppendLine("    public static class GameUI");
            source.AppendLine("    {");
            AppendDefinitionFields(source, entries);
            AppendDefinitionProperties(source, entries);
            AppendDefinitionProvider(source, entries);
            source.AppendLine("    }");
            source.AppendLine("}");
            return source.ToString();
        }

        private static void AppendDefinitionFields(
            StringBuilder source,
            IReadOnlyList<DefinitionEntry> entries)
        {
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var uiName = entry.Settings.UIName;
                source.AppendLine(
                    $"        private static UIDefinition<{entry.ViewName}, {entry.ControllerName}> " +
                    $"m_{ToCamelCase(uiName)}; // {uiName} 的延迟创建缓存。");
            }

            source.AppendLine();
            source.AppendLine("        /// <summary>");
            source.AppendLine("        /// 按名称延迟解析业务界面定义的提供器。");
            source.AppendLine("        /// </summary>");
            source.AppendLine("        public static IUIDefinitionProvider DefinitionProvider { get; } =");
            source.AppendLine("            new GeneratedDefinitionProvider();");
        }

        private static void AppendDefinitionProperties(
            StringBuilder source,
            IReadOnlyList<DefinitionEntry> entries)
        {
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var settings = entry.Settings;
                var fieldName = $"m_{ToCamelCase(settings.UIName)}";
                source.AppendLine();
                source.AppendLine("        /// <summary>");
                source.AppendLine($"        /// {settings.UIName} 的静态界面定义。");
                source.AppendLine("        /// </summary>");
                source.AppendLine(
                    $"        public static UIDefinition<{entry.ViewName}, {entry.ControllerName}> " +
                    $"{settings.UIName}");
                source.AppendLine("        {");
                source.AppendLine("            get");
                source.AppendLine("            {");
                source.AppendLine($"                if ({fieldName} == null)");
                source.AppendLine("                {");
                source.AppendLine($"                    {fieldName} = Create{settings.UIName}();");
                source.AppendLine("                }");
                source.AppendLine();
                source.AppendLine($"                return {fieldName};");
                source.AppendLine("            }");
                source.AppendLine("        }");
                AppendDefinitionFactory(source, entry);
            }
        }

        private static void AppendDefinitionFactory(StringBuilder source, DefinitionEntry entry)
        {
            var settings = entry.Settings;
            source.AppendLine();
            source.AppendLine(
                $"        private static UIDefinition<{entry.ViewName}, {entry.ControllerName}> " +
                $"Create{settings.UIName}()");
            source.AppendLine("        {");
            source.AppendLine(
                $"            return new UIDefinitionBuilder<{entry.ViewName}, {entry.ControllerName}>(" +
                $"\"{settings.UIName}\")");
            source.AppendLine($"                .Location(\"{Escape(entry.Location)}\")");
            source.AppendLine($"                .Layer(UILayer.{settings.Layer})");
            source.AppendLine($"                .Layout(UILayoutMode.{settings.Layout})");
            source.AppendLine($"                .Background(UIBackgroundMode.{settings.Background})");
            source.AppendLine($"                .Input(UIInputMode.{settings.Input})");
            source.AppendLine($"                .Cache(UICacheMode.{settings.Cache})");
            source.AppendLine($"                .OpenMode(UIOpenMode.{settings.OpenMode})");
            source.AppendLine($"                .Navigation(UINavigationMode.{settings.Navigation})");
            source.AppendLine(
                $"                .PauseCovered({(settings.PauseCovered ? "true" : "false")})");
            source.AppendLine("                .Build();");
            source.AppendLine("        }");
        }

        private static void AppendDefinitionProvider(
            StringBuilder source,
            IReadOnlyList<DefinitionEntry> entries)
        {
            var buckets = BuildDefinitionBuckets(entries);
            source.AppendLine();
            source.AppendLine("        private sealed class GeneratedDefinitionProvider : IUIDefinitionProvider");
            source.AppendLine("        {");
            source.AppendLine(
                "            private const int DEFINITIONBUCKETCOUNT = 64; // 生成定义查找方法的固定分桶数。");
            source.AppendLine();
            source.AppendLine("            /// <summary>");
            source.AppendLine("            /// 判断是否包含指定的稳定 UI 名称，但不创建界面定义。");
            source.AppendLine("            /// </summary>");
            source.AppendLine("            /// <param name=\"uiName\">界面的稳定名称。</param>");
            source.AppendLine("            /// <returns>包含该名称时返回 true，否则返回 false。</returns>");
            source.AppendLine("            public bool Contains(string uiName)");
            source.AppendLine("            {");
            source.AppendLine("                switch (CalculateBucket(uiName))");
            source.AppendLine("                {");
            foreach (var bucket in buckets)
            {
                source.AppendLine($"                    case {bucket.Key}:");
                source.AppendLine($"                        return ContainsBucket{bucket.Key}(uiName);");
            }
            source.AppendLine("                    default:");
            source.AppendLine("                        return false;");
            source.AppendLine("                }");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            /// <summary>");
            source.AppendLine("            /// 尝试按稳定 UI 名称取得延迟创建的界面定义。");
            source.AppendLine("            /// </summary>");
            source.AppendLine("            /// <param name=\"uiName\">界面的稳定名称。</param>");
            source.AppendLine("            /// <param name=\"definition\">成功时返回对应界面定义。</param>");
            source.AppendLine("            /// <returns>能够解析名称时返回 true，否则返回 false。</returns>");
            source.AppendLine(
                "            public bool TryGetDefinition(string uiName, out UIDefinition definition)");
            source.AppendLine("            {");
            source.AppendLine("                switch (CalculateBucket(uiName))");
            source.AppendLine("                {");
            foreach (var bucket in buckets)
            {
                source.AppendLine($"                    case {bucket.Key}:");
                source.AppendLine(
                    $"                        return TryGetBucket{bucket.Key}(uiName, out definition);");
            }
            source.AppendLine("                    default:");
            source.AppendLine("                        definition = null;");
            source.AppendLine("                        return false;");
            source.AppendLine("                }");
            source.AppendLine("            }");
            AppendDefinitionBucketMethods(source, buckets);
            source.AppendLine();
            source.AppendLine("            private static int CalculateBucket(string uiName)");
            source.AppendLine("            {");
            source.AppendLine("                if (string.IsNullOrEmpty(uiName))");
            source.AppendLine("                {");
            source.AppendLine("                    return -1;");
            source.AppendLine("                }");
            source.AppendLine();
            source.AppendLine("                unchecked");
            source.AppendLine("                {");
            source.AppendLine("                    uint hash = 2166136261;");
            source.AppendLine("                    for (var index = 0; index < uiName.Length; index++)");
            source.AppendLine("                    {");
            source.AppendLine("                        hash ^= uiName[index];");
            source.AppendLine("                        hash *= 16777619;");
            source.AppendLine("                    }");
            source.AppendLine();
            source.AppendLine("                    return (int)(hash & (DEFINITIONBUCKETCOUNT - 1));");
            source.AppendLine("                }");
            source.AppendLine("            }");
            source.AppendLine("        }");
        }

        private static void AppendDefinitionBucketMethods(
            StringBuilder source,
            SortedDictionary<int, List<DefinitionEntry>> buckets)
        {
            foreach (var bucket in buckets)
            {
                source.AppendLine();
                source.AppendLine($"            private static bool ContainsBucket{bucket.Key}(string uiName)");
                source.AppendLine("            {");
                source.AppendLine("                switch (uiName)");
                source.AppendLine("                {");
                foreach (var entry in bucket.Value)
                {
                    source.AppendLine($"                    case \"{entry.Settings.UIName}\":");
                    source.AppendLine("                        return true;");
                }
                source.AppendLine("                    default:");
                source.AppendLine("                        return false;");
                source.AppendLine("                }");
                source.AppendLine("            }");
                source.AppendLine();
                source.AppendLine(
                    $"            private static bool TryGetBucket{bucket.Key}(" +
                    "string uiName, out UIDefinition definition)");
                source.AppendLine("            {");
                source.AppendLine("                switch (uiName)");
                source.AppendLine("                {");
                foreach (var entry in bucket.Value)
                {
                    source.AppendLine($"                    case \"{entry.Settings.UIName}\":");
                    source.AppendLine($"                        definition = {entry.Settings.UIName};");
                    source.AppendLine("                        return true;");
                }
                source.AppendLine("                    default:");
                source.AppendLine("                        definition = null;");
                source.AppendLine("                        return false;");
                source.AppendLine("                }");
                source.AppendLine("            }");
            }
        }

        private static SortedDictionary<int, List<DefinitionEntry>> BuildDefinitionBuckets(
            IReadOnlyList<DefinitionEntry> entries)
        {
            var buckets = new SortedDictionary<int, List<DefinitionEntry>>();
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var bucket = CalculateStableHash(entry.Settings.UIName) & 63;
                if (!buckets.TryGetValue(bucket, out var bucketEntries))
                {
                    bucketEntries = new List<DefinitionEntry>();
                    buckets.Add(bucket, bucketEntries);
                }

                bucketEntries.Add(entry);
            }

            return buckets;
        }

        private static int CalculateStableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                for (var index = 0; index < value.Length; index++)
                {
                    hash ^= value[index];
                    hash *= 16777619;
                }

                return (int)hash;
            }
        }

        private static string Escape(string value) =>
            value.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static string ToCamelCase(string value) =>
            char.ToLowerInvariant(value[0]) + value.Substring(1);
    }
}
