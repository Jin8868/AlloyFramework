using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AlloyFramework.UI;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.UI;

namespace AlloyFramework.Editor
{
    [InitializeOnLoad]
    internal static class UIAuthoringGenerator
    {
        private const string PendingKey = "AlloyFramework.UI.Authoring.Pending";
        private const string DefinitionPath =
            "Assets/Scripts/Runtime/UI/Generated/GameUI.Definitions.g.cs";
        private const string PropertiesPath =
            "Assets/Scripts/Runtime/UI/Generated/GameUI.Properties.g.cs";
        private const string LegacyDefinitionPath =
            "Assets/Scripts/Runtime/UI/Generated/GameUI.g.cs";
        private const string RuntimeFolder = "Assets/Scripts/Runtime";
        private const int DEFINITIONCHUNKSIZE = 256;

        [Serializable]
        private sealed class PendingGeneration
        {
            public string PrefabGuid;
            public string ViewPath;
            public string ViewTypeName;
        }

        [Serializable]
        private sealed class PendingGenerationBatch
        {
            [SerializeField] private List<PendingGeneration> m_items =
                new List<PendingGeneration>(); // 等待回写的 UI 列表。

            public List<PendingGeneration> Items => m_items;
        }

        private sealed class GenerationRequest
        {
            public UIAuthoringSettings Settings;
            public GameObject Prefab;
            public string ViewName;
            public string ControllerName;
            public string ViewPath;
            public string ControllerPath;
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
            Generate(new[] { settings });
        }

        internal static void Generate(IReadOnlyList<UIAuthoringSettings> settingsCollection)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出运行模式。");
            if (!string.IsNullOrEmpty(SessionState.GetString(PendingKey, string.Empty)))
                throw new InvalidOperationException("上一个 UI 正在等待脚本编译，请稍后再生成。");

            var requests = CreateGenerationRequests(settingsCollection);

            // 全部校验通过后才写入文件，避免批量操作只生成一部分 UI。
            EnsureWritableGeneratedFile(DefinitionPath);
            EnsureWritableGeneratedFile(PropertiesPath);
            EnsureWritableGeneratedFile(LegacyDefinitionPath);
            Directory.CreateDirectory(Path.GetDirectoryName(DefinitionPath) ?? string.Empty);
            for (var index = 0; index < requests.Count; index++)
            {
                var request = requests[index];
                Directory.CreateDirectory(Path.GetDirectoryName(request.ViewPath) ?? string.Empty);
                Directory.CreateDirectory(Path.GetDirectoryName(request.ControllerPath) ?? string.Empty);
                UIBindingGenerator.GenerateViewSource(
                    request.Prefab,
                    request.Settings.ScriptNamespace,
                    request.ViewName,
                    request.ViewPath);
                WriteIfMissing(
                    request.ControllerPath,
                    BuildController(
                        request.Settings.ScriptNamespace,
                        request.ViewName,
                        request.ControllerName));
            }

            WriteDefinitions(requests);
            UIAuthoringSettings.Save();

            var pendingBatch = new PendingGenerationBatch();
            for (var index = 0; index < requests.Count; index++)
            {
                var request = requests[index];
                pendingBatch.Items.Add(new PendingGeneration
                {
                    PrefabGuid = request.Settings.PrefabGuid,
                    ViewPath = request.ViewPath,
                    ViewTypeName = request.Settings.ScriptNamespace + "." + request.ViewName
                });
            }

            SessionState.SetString(PendingKey, JsonUtility.ToJson(pendingBatch));
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

            var pendingGenerations = GetPendingGenerations(json);
            var viewTypes = new List<Type>(pendingGenerations.Count);
            for (var index = 0; index < pendingGenerations.Count; index++)
            {
                var pending = pendingGenerations[index];
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

                viewTypes.Add(viewType);
            }

            var successCount = 0;
            var failureCount = 0;
            for (var index = 0; index < pendingGenerations.Count; index++)
            {
                try
                {
                    ApplyPendingGeneration(pendingGenerations[index], viewTypes[index]);
                    successCount++;
                }
                catch (Exception exception)
                {
                    failureCount++;
                    Debug.LogException(exception);
                }
            }

            SessionState.SetString(PendingKey, string.Empty);
            UIAuthoringSettings.Save();
            if (failureCount == 0)
            {
                Debug.Log($"已生成并写入 {successCount} 个 UI 的强类型绑定。");
            }
            else
            {
                Debug.LogError($"UI 批量生成完成：成功 {successCount} 个，失败 {failureCount} 个。");
            }
        }

        private static List<GenerationRequest> CreateGenerationRequests(
            IReadOnlyList<UIAuthoringSettings> settingsCollection)
        {
            if (settingsCollection == null || settingsCollection.Count == 0)
            {
                throw new InvalidOperationException("请至少选择一个 UI 预制体。");
            }

            RecoverGeneratedSettings();
            var requests = new List<GenerationRequest>(settingsCollection.Count);
            var prefabGuids = new HashSet<string>(StringComparer.Ordinal);
            var scriptNamespace = string.Empty;
            for (var index = 0; index < settingsCollection.Count; index++)
            {
                var request = CreateGenerationRequest(settingsCollection[index]);
                if (!prefabGuids.Add(request.Settings.PrefabGuid))
                {
                    throw new InvalidOperationException(
                        $"UI {request.Settings.UIName} 被重复加入本次生成。");
                }

                if (string.IsNullOrEmpty(scriptNamespace))
                {
                    scriptNamespace = request.Settings.ScriptNamespace;
                }
                else if (!string.Equals(
                             scriptNamespace,
                             request.Settings.ScriptNamespace,
                             StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "统一的 GameUI 要求批量生成的界面使用同一个业务命名空间。");
                }

                requests.Add(request);
            }

            ValidateGeneratedDefinitionsNamespace(scriptNamespace, prefabGuids);
            return requests;
        }

        private static GenerationRequest CreateGenerationRequest(UIAuthoringSettings settings)
        {
            if (settings == null)
            {
                throw new InvalidOperationException("存在无效的 UI 生成配置。");
            }

            var prefabPath = AssetDatabase.GUIDToAssetPath(settings.PrefabGuid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null ||
                !prefabPath.StartsWith("Assets/Res/Prefabs/UI/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("请选择 Assets/Res/Prefabs/UI 内的业务 UI 预制体。");
            }

            if (prefab.GetComponentInChildren<UIRoot>(true) != null)
            {
                throw new InvalidOperationException("UIRoot 是框架预制体，不能生成业务 View。");
            }

            ValidateIdentifier(settings.UIName, "UI 名称");
            var namespaceSegments = settings.ScriptNamespace.Split('.');
            for (var index = 0; index < namespaceSegments.Length; index++)
            {
                ValidateIdentifier(namespaceSegments[index], "命名空间");
            }

            var viewFolder = ValidateFolder(settings.ViewFolder, "View 文件夹");
            var controllerFolder = ValidateFolder(settings.ControllerFolder, "Controller 文件夹");
            ValidateUniqueName(settings);

            var viewName = settings.UIName + "View";
            var controllerName = settings.UIName + "Controller";
            var existingViews = prefab.GetComponents<UIView>();
            if (existingViews.Length > 1 || existingViews.Length == 1 &&
                existingViews[0].GetType().FullName != settings.ScriptNamespace + "." + viewName)
            {
                throw new InvalidOperationException(
                    "预制体根节点已有不同的 UIView，请先检查；生成器不会删除组件。");
            }

            return new GenerationRequest
            {
                Settings = settings,
                Prefab = prefab,
                ViewName = viewName,
                ControllerName = controllerName,
                ViewPath = $"{viewFolder}/{viewName}.cs",
                ControllerPath = $"{controllerFolder}/{controllerName}.cs"
            };
        }

        private static void ValidateGeneratedDefinitionsNamespace(
            string scriptNamespace,
            ISet<string> pendingPrefabGuids)
        {
            foreach (var settings in UIAuthoringSettings.All)
            {
                if (settings == null ||
                    !settings.HasGenerated && !pendingPrefabGuids.Contains(settings.PrefabGuid))
                {
                    continue;
                }

                if (!string.Equals(settings.ScriptNamespace, scriptNamespace, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"统一的 GameUI 要求所有界面使用命名空间 {scriptNamespace}；" +
                        $"{settings.UIName} 当前使用 {settings.ScriptNamespace}。");
                }
            }
        }

        private static List<PendingGeneration> GetPendingGenerations(string json)
        {
            var pendingBatch = JsonUtility.FromJson<PendingGenerationBatch>(json);
            if (pendingBatch != null && pendingBatch.Items != null && pendingBatch.Items.Count > 0)
            {
                return pendingBatch.Items;
            }

            var legacyPending = JsonUtility.FromJson<PendingGeneration>(json);
            if (legacyPending != null && !string.IsNullOrEmpty(legacyPending.PrefabGuid))
            {
                return new List<PendingGeneration> { legacyPending };
            }

            throw new InvalidOperationException("UI 生成等待记录无效，请重新生成。");
        }

        /// <summary>
        /// 为创建或生成的界面补齐根 Canvas、缩放器和射线检测组件。
        /// </summary>
        /// <param name="root">界面预制体或新建界面的根节点。</param>
        internal static void EnsureRootCanvas(GameObject root)
        {
            // 已有 Canvas 保持原配置；新增 Canvas 的相机由运行时 UI 管理流程提供。
            var canvas = root.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
            }

            // 独立根 Canvas 自带缩放器，参考分辨率由运行时 UIRoot 统一应用。
            if (root.GetComponent<CanvasScaler>() == null)
            {
                var scaler = root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            }

            // 每个界面保留自己的交互入口，避免依赖父级射线检测组件。
            if (root.GetComponent<GraphicRaycaster>() == null)
            {
                root.AddComponent<GraphicRaycaster>();
            }
        }

        private static void ApplyPendingGeneration(PendingGeneration pending, Type viewType)
        {
            if (viewType.FullName != pending.ViewTypeName || viewType.IsAbstract ||
                !typeof(UIView).IsAssignableFrom(viewType))
            {
                throw new InvalidOperationException(
                    $"{pending.ViewPath} 必须声明 {pending.ViewTypeName} : UIView。");
            }

            var prefabPath = AssetDatabase.GUIDToAssetPath(pending.PrefabGuid);
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var views = root.GetComponents<UIView>();
                if (views.Length > 1 || views.Length == 1 && views[0].GetType() != viewType)
                {
                    throw new InvalidOperationException(
                        "预制体根节点已有不同的 UIView，生成器未修改预制体。");
                }

                if (views.Length == 0)
                {
                    root.AddComponent(viewType);
                }

                // 根节点的渲染组件由生成流程补齐，不覆盖已有相机和排序配置。
                EnsureRootCanvas(root);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            UIBindingGenerator.ApplyBindings(prefabPath);
            var settings = UIAuthoringSettings.TryLoad(pending.PrefabGuid);
            settings?.MarkGenerated();
        }

        private static void WriteDefinitions(IReadOnlyList<GenerationRequest> requests)
        {
            var scriptNamespace = requests[0].Settings.ScriptNamespace;
            var pendingPrefabGuids = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < requests.Count; index++)
            {
                pendingPrefabGuids.Add(requests[index].Settings.PrefabGuid);
            }

            RecoverGeneratedSettings();
            var entries = new List<DefinitionEntry>();
            foreach (var settings in UIAuthoringSettings.All)
            {
                if (settings == null ||
                    !settings.HasGenerated && !pendingPrefabGuids.Contains(settings.PrefabGuid))
                {
                    continue;
                }

                var prefabPath = AssetDatabase.GUIDToAssetPath(settings.PrefabGuid);
                if (string.IsNullOrEmpty(prefabPath) ||
                    !prefabPath.StartsWith("Assets/Res/", StringComparison.Ordinal) ||
                    !prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"UI {settings.UIName} 对应的预制体不存在。");
                }

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
            File.WriteAllText(
                DefinitionPath,
                BuildDefinitionRegistry(scriptNamespace, entries),
                new UTF8Encoding(false));
            File.WriteAllText(
                PropertiesPath,
                BuildDefinitionProperties(scriptNamespace, entries),
                new UTF8Encoding(false));
            DeleteLegacyDefinitionFile();
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
            source.AppendLine("        protected override UniTask OnCloseAnimationAsync(");
            source.AppendLine("            CancellationToken cancellationToken)");
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

        private static void DeleteLegacyDefinitionFile()
        {
            if (!File.Exists(LegacyDefinitionPath))
            {
                return;
            }

            File.Delete(LegacyDefinitionPath);
            var metaPath = LegacyDefinitionPath + ".meta";
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }
        }

        internal static void RecoverGeneratedSettings()
        {
            var hasRecovered = false;
            var prefabGuids = AssetDatabase.FindAssets(
                "t:Prefab",
                new[] { "Assets/Res/Prefabs/UI" });
            for (var index = 0; index < prefabGuids.Length; index++)
            {
                var prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[index]);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null || prefab.GetComponentInChildren<UIRoot>(true) != null)
                {
                    continue;
                }

                var view = prefab.GetComponent<UIView>();
                if (view == null)
                {
                    continue;
                }

                var settings = UIAuthoringSettings.LoadOrCreate(prefabPath);
                if (settings.HasGenerated)
                {
                    continue;
                }

                var viewType = view.GetType();
                settings.UIName = prefab.name;
                settings.ScriptNamespace = viewType.Namespace;
                settings.ViewFolder = GetScriptFolder(
                    viewType.Name,
                    settings.ViewFolder);
                settings.ControllerFolder = GetScriptFolder(
                    prefab.name + "Controller",
                    settings.ControllerFolder);
                settings.MarkGenerated();
                hasRecovered = true;
                Debug.LogWarning($"已恢复丢失的 UI 生成记录：{prefabPath}");
            }

            if (hasRecovered)
            {
                UIAuthoringSettings.Save();
            }
        }

        private static string GetScriptFolder(string typeName, string fallbackFolder)
        {
            var scriptGuids = AssetDatabase.FindAssets($"{typeName} t:MonoScript");
            for (var index = 0; index < scriptGuids.Length; index++)
            {
                var scriptPath = AssetDatabase.GUIDToAssetPath(scriptGuids[index]);
                if (!string.Equals(
                        Path.GetFileNameWithoutExtension(scriptPath),
                        typeName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                return Path.GetDirectoryName(scriptPath)?.Replace('\\', '/') ?? fallbackFolder;
            }

            return fallbackFolder;
        }

        private static string BuildDefinitionRegistry(
            string scriptNamespace,
            IReadOnlyList<DefinitionEntry> entries)
        {
            var source = new StringBuilder();
            source.AppendLine("// <auto-generated />");
            source.AppendLine("// 由 AlloyFramework UI 生成器生成，请勿手动修改。");
            source.AppendLine("using System.Collections.Generic;");
            source.AppendLine("using AlloyFramework.UI;");
            source.AppendLine();
            source.AppendLine($"namespace {scriptNamespace}");
            source.AppendLine("{");
            source.AppendLine("    public static partial class GameUI");
            source.AppendLine("    {");
            AppendDefinitionRegistry(source);
            AppendDefinitionInitialization(source, entries);
            source.AppendLine("    }");
            source.AppendLine("}");
            return source.ToString();
        }

        private static string BuildDefinitionProperties(
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
            source.AppendLine("    public static partial class GameUI");
            source.AppendLine("    {");
            AppendDefinitionProperties(source, entries);
            source.AppendLine("    }");
            source.AppendLine("}");
            return source.ToString();
        }

        private static void AppendDefinitionRegistry(StringBuilder source)
        {
            source.AppendLine(
                "        private static readonly Dictionary<string, UIDefinition> m_definitions =");
            source.AppendLine("            CreateDefinitions(); // 生成的全部 UI 定义。");
            source.AppendLine("        /// <summary>");
            source.AppendLine("        /// 按名称查询业务 UI 定义的提供器。");
            source.AppendLine("        /// </summary>");
            source.AppendLine("        public static IUIDefinitionProvider DefinitionProvider { get; } =");
            source.AppendLine("            new UIDefinitionDictionaryProvider(m_definitions);");
        }

        private static void AppendDefinitionProperties(
            StringBuilder source,
            IReadOnlyList<DefinitionEntry> entries)
        {
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var settings = entry.Settings;
                source.AppendLine();
                source.AppendLine("        /// <summary>");
                source.AppendLine($"        /// {settings.UIName} 的静态界面定义。");
                source.AppendLine("        /// </summary>");
                source.AppendLine(
                    $"        public static UIDefinition<{entry.ViewName}, {entry.ControllerName}> " +
                    $"{settings.UIName} =>");
                source.AppendLine(
                    $"            (UIDefinition<{entry.ViewName}, {entry.ControllerName}>)" +
                    $"m_definitions[\"{settings.UIName}\"];");
            }
        }

        private static void AppendDefinitionInitialization(
            StringBuilder source,
            IReadOnlyList<DefinitionEntry> entries)
        {
            source.AppendLine();
            source.AppendLine("        private static Dictionary<string, UIDefinition> CreateDefinitions()");
            source.AppendLine("        {");
            source.AppendLine(
                $"            var definitions = new Dictionary<string, UIDefinition>({entries.Count});");
            var chunkCount = (entries.Count + DEFINITIONCHUNKSIZE - 1) / DEFINITIONCHUNKSIZE;
            for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                source.AppendLine($"            AddDefinitions{chunkIndex}(definitions);");
            }
            source.AppendLine("            return definitions;");
            source.AppendLine("        }");
            for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                source.AppendLine();
                source.AppendLine(
                    $"        private static void AddDefinitions{chunkIndex}(" +
                    "Dictionary<string, UIDefinition> definitions)");
                source.AppendLine("        {");
                var startIndex = chunkIndex * DEFINITIONCHUNKSIZE;
                var endIndex = Math.Min(startIndex + DEFINITIONCHUNKSIZE, entries.Count);
                for (var index = startIndex; index < endIndex; index++)
                {
                    AppendDefinitionRegistration(source, entries[index]);
                }
                source.AppendLine("        }");
            }
        }

        private static void AppendDefinitionRegistration(
            StringBuilder source,
            DefinitionEntry entry)
        {
            var settings = entry.Settings;
            source.AppendLine($"            // 注册 {settings.UIName} 的静态界面定义。");
            source.AppendLine("            definitions.Add(");
            source.AppendLine($"                \"{settings.UIName}\",");
            source.AppendLine(
                $"                new UIDefinitionBuilder<{entry.ViewName}, {entry.ControllerName}>(" +
                $"\"{settings.UIName}\")");
            source.AppendLine($"                    .Location(\"{Escape(entry.Location)}\")");
            source.AppendLine($"                    .Layer(UILayer.{settings.Layer})");
            source.AppendLine($"                    .Layout(UILayoutMode.{settings.Layout})");
            source.AppendLine($"                    .Background(UIBackgroundMode.{settings.Background})");
            source.AppendLine($"                    .Input(UIInputMode.{settings.Input})");
            source.AppendLine($"                    .Cache(UICacheMode.{settings.Cache})");
            source.AppendLine($"                    .OpenMode(UIOpenMode.{settings.OpenMode})");
            source.AppendLine($"                    .Navigation(UINavigationMode.{settings.Navigation})");
            source.AppendLine(
                $"                    .PauseCovered({(settings.PauseCovered ? "true" : "false")})");
            source.AppendLine("                    .Build());");
            source.AppendLine();
        }

        private static string Escape(string value) =>
            value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    }
}
