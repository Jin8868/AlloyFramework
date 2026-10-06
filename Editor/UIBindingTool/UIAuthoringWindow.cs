using System;
using System.Collections.Generic;
using System.IO;
using AlloyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace AlloyFramework.Editor
{
    public sealed class UIAuthoringWindow : EditorWindow
    {
        private sealed class PrefabItem
        {
            public string Guid;
            public string Path;
            public GameObject Prefab;
            public UIAuthoringSettings Settings;
        }

        [SerializeField] private GameObject m_prefab;
        [NonSerialized] private UIAuthoringSettings m_settings;
        [SerializeField] private string m_search = string.Empty;
        [SerializeField] private List<string> m_selectedPrefabGuids =
            new List<string>(); // 批量生成时选中的预制体 GUID。
        private readonly List<PrefabItem> m_prefabs = new List<PrefabItem>();
        private Vector2 m_listScroll;
        private Vector2 m_detailScroll;

        [MenuItem("★AlloyFramework★/UI/打开界面生成器", false, 100)]
        private static void Open()
        {
            var window = GetWindow<UIAuthoringWindow>("UI 生成器");
            window.minSize = new Vector2(820, 600);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.delayCall += Initialize;
        }

        private void OnDisable()
        {
            EditorApplication.delayCall -= Initialize;
            SaveSettings();
        }

        private void Initialize()
        {
            if (this == null)
            {
                return;
            }

            RefreshPrefabs();
            if (m_prefab != null && m_settings == null)
            {
                SelectPrefab(m_prefab);
            }
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("UI 生成器", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"已选 {GetSelectedPrefabCount()} 个", EditorStyles.miniLabel);
            GUILayout.Label($"业务预制体 {m_prefabs.Count} 个", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            DrawPrefabList();
            DrawDetails();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPrefabList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(285));
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            m_search = GUILayout.TextField(m_search ?? string.Empty,
                GUI.skin.FindStyle("ToolbarSeachTextField") ?? EditorStyles.textField);
            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(44))) RefreshPrefabs();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("全选筛选项", EditorStyles.toolbarButton))
            {
                SelectVisiblePrefabs();
            }

            if (GUILayout.Button("清空选择", EditorStyles.toolbarButton))
            {
                ClearPrefabSelection();
            }

            EditorGUILayout.EndHorizontal();
            m_listScroll = EditorGUILayout.BeginScrollView(m_listScroll);
            var itemButton = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold
            };
            foreach (var item in m_prefabs)
            {
                if (!MatchesSearch(item))
                    continue;
                var previousColor = GUI.backgroundColor;
                if (IsPrefabSelected(item.Guid))
                {
                    GUI.backgroundColor = new Color(0.56f, 0.84f, 0.63f);
                }
                else if (item.Prefab == m_prefab)
                {
                    GUI.backgroundColor = new Color(0.54f, 0.77f, 1f);
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                var isSelected = IsPrefabSelected(item.Guid);
                var selected = GUILayout.Toggle(isSelected, GUIContent.none, GUILayout.Width(18f));
                if (selected != isSelected)
                {
                    SetPrefabSelected(item.Guid, selected);
                }

                if (GUILayout.Button(item.Prefab.name, itemButton)) SelectPrefab(item.Prefab);
                EditorGUILayout.EndHorizontal();
                GUI.backgroundColor = previousColor;
                EditorGUILayout.LabelField(item.Path, EditorStyles.miniLabel);
                var layer = item.Settings != null ? item.Settings.Layer.ToString() : "未配置";
                var background = item.Settings != null ? item.Settings.Background.ToString() : "-";
                EditorGUILayout.LabelField($"层级 {layer}    背景 {background}", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawDetails()
        {
            EditorGUILayout.BeginVertical();
            m_detailScroll = EditorGUILayout.BeginScrollView(m_detailScroll);
            EditorGUILayout.Space(8);
            DrawBatchGenerateAction();
            EditorGUILayout.LabelField("界面配置", EditorStyles.boldLabel);
            var prefab = (GameObject)EditorGUILayout.ObjectField("UI 预制体", m_prefab,
                typeof(GameObject), false);
            if (prefab != m_prefab) SelectPrefab(prefab);
            if (m_settings == null)
            {
                EditorGUILayout.HelpBox("从左侧搜索并选择业务 UI 预制体，或使用上方对象框。", MessageType.Info);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
                return;
            }

            var prefabPath = AssetDatabase.GUIDToAssetPath(m_settings.PrefabGuid);
            EditorGUILayout.SelectableLabel(prefabPath, EditorStyles.textField, GUILayout.Height(20));
            EditorGUILayout.LabelField("YooAsset 地址", GetLocation(prefabPath));
            EditorGUILayout.Space(8);
            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.DisabledScope(m_settings.HasGenerated))
            {
                m_settings.UIName = EditorGUILayout.TextField("UI 名称", m_settings.UIName);
                m_settings.ScriptNamespace = EditorGUILayout.TextField("业务命名空间", m_settings.ScriptNamespace);
                m_settings.ViewFolder = FolderField("View 文件夹", m_settings.ViewFolder);
                m_settings.ControllerFolder = FolderField("Controller 文件夹", m_settings.ControllerFolder);
            }
            if (m_settings.HasGenerated)
                EditorGUILayout.HelpBox("已有手写脚本：名称、命名空间和目录已锁定；展示策略仍可修改并重新生成。", MessageType.Info);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("展示策略", EditorStyles.boldLabel);
            m_settings.Layer = (UILayer)EditorGUILayout.EnumPopup("层级", m_settings.Layer);
            m_settings.Layout = (UILayoutMode)EditorGUILayout.EnumPopup("布局", m_settings.Layout);
            m_settings.Background = (UIBackgroundMode)EditorGUILayout.EnumPopup("背景", m_settings.Background);
            m_settings.Input = (UIInputMode)EditorGUILayout.EnumPopup("输入", m_settings.Input);
            m_settings.Cache = (UICacheMode)EditorGUILayout.EnumPopup("缓存", m_settings.Cache);
            m_settings.OpenMode = (UIOpenMode)EditorGUILayout.EnumPopup("重复打开", m_settings.OpenMode);
            m_settings.Navigation = (UINavigationMode)EditorGUILayout.EnumPopup("导航", m_settings.Navigation);
            m_settings.PauseCovered = EditorGUILayout.Toggle("覆盖时暂停下层", m_settings.PauseCovered);
            if (EditorGUI.EndChangeCheck()) UIAuthoringSettings.SetDirty();
            if (m_settings.Background == UIBackgroundMode.Blur)
                EditorGUILayout.HelpBox(
                    "模糊已接入运行时。请配置 UIBlurForeground Layer，并在 URP Renderer 添加 UIBlurRendererFeature 和 Shader。",
                    MessageType.Info);
            EditorGUILayout.HelpBox(
                "背景黑色遮罩由预制体提供；布局、点击空白关闭和覆盖暂停需按项目的运行时策略处理。",
                MessageType.Info);

            EditorGUILayout.Space(12);
            if (GUILayout.Button("生成界面代码并绑定预制体", GUILayout.Height(36))) Generate();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void Generate()
        {
            SaveSettings();
            try
            {
                var prefabGuid = m_settings.PrefabGuid;
                AssetDatabase.ImportAsset(UIAuthoringDatabase.AssetPath,
                    ImportAssetOptions.ForceUpdate);
                m_settings = UIAuthoringSettings.TryLoad(prefabGuid);
                UIAuthoringGenerator.Generate(m_settings);
                RefreshPrefabs();
                ShowNotification(new GUIContent("脚本已生成，编译完成后自动绑定预制体。"));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("UI 生成失败", exception.Message, "确定");
            }
        }

        private void DrawBatchGenerateAction()
        {
            var selectedCount = GetSelectedPrefabCount();
            using (new EditorGUI.DisabledScope(selectedCount == 0))
            {
                if (GUILayout.Button($"生成选中项（{selectedCount}）", GUILayout.Height(30f)))
                {
                    GenerateSelected();
                }
            }

            EditorGUILayout.Space(8);
        }

        private void GenerateSelected()
        {
            SaveSettings();
            var settingsCollection = new List<UIAuthoringSettings>();
            try
            {
                // 选中但尚未打开过详情的预制体，也按默认规则创建生成配置。
                for (var index = 0; index < m_prefabs.Count; index++)
                {
                    var item = m_prefabs[index];
                    if (!IsPrefabSelected(item.Guid))
                    {
                        continue;
                    }

                    var settings = UIAuthoringSettings.LoadOrCreate(item.Path);
                    item.Settings = settings;
                    settingsCollection.Add(settings);
                }

                if (settingsCollection.Count == 0)
                {
                    ShowNotification(new GUIContent("请先勾选需要生成的 UI。"));
                    return;
                }

                UIAuthoringSettings.Save();
                AssetDatabase.ImportAsset(
                    UIAuthoringDatabase.AssetPath,
                    ImportAssetOptions.ForceUpdate);
                for (var index = 0; index < settingsCollection.Count; index++)
                {
                    var settings = settingsCollection[index];
                    settingsCollection[index] = UIAuthoringSettings.TryLoad(settings.PrefabGuid);
                }

                UIAuthoringGenerator.Generate(settingsCollection);
                RefreshPrefabs();
                ShowNotification(
                    new GUIContent($"已提交 {settingsCollection.Count} 个 UI，编译完成后自动绑定预制体。"));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("UI 批量生成失败", exception.Message, "确定");
            }
        }

        private void SelectPrefab(GameObject prefab)
        {
            SaveSettings();
            m_prefab = prefab;
            m_settings = null;
            if (prefab == null) return;
            var path = AssetDatabase.GetAssetPath(prefab);
            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
                !path.StartsWith("Assets/Res/Prefabs/UI/", StringComparison.Ordinal) ||
                !PrefabUtility.IsPartOfPrefabAsset(prefab) || prefab.GetComponentInChildren<UIRoot>(true) != null)
            {
                m_prefab = null;
                EditorUtility.DisplayDialog("选择 UI 预制体", "请选择 Assets/Res/Prefabs/UI 内的业务预制体，UIRoot 除外。", "确定");
                return;
            }
            m_settings = UIAuthoringSettings.LoadOrCreate(path);
            foreach (var item in m_prefabs)
                if (item.Prefab == prefab) item.Settings = m_settings;
        }

        private void RefreshPrefabs()
        {
            UIAuthoringGenerator.RecoverGeneratedSettings();
            m_prefabs.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Res/Prefabs/UI" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || prefab.GetComponentInChildren<UIRoot>(true) != null) continue;
                m_prefabs.Add(new PrefabItem
                {
                    Guid = guid, Path = path, Prefab = prefab,
                    Settings = UIAuthoringSettings.TryLoad(guid)
                });
            }
            m_prefabs.Sort((left, right) => string.Compare(left.Path, right.Path, StringComparison.Ordinal));
            RemoveMissingPrefabSelections();
        }

        private bool MatchesSearch(PrefabItem item)
        {
            return string.IsNullOrEmpty(m_search) ||
                   item.Path.IndexOf(m_search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   item.Prefab.name.IndexOf(m_search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SelectVisiblePrefabs()
        {
            for (var index = 0; index < m_prefabs.Count; index++)
            {
                var item = m_prefabs[index];
                if (MatchesSearch(item))
                {
                    SetPrefabSelected(item.Guid, true);
                }
            }
        }

        private void ClearPrefabSelection()
        {
            m_selectedPrefabGuids.Clear();
        }

        private bool IsPrefabSelected(string prefabGuid)
        {
            return m_selectedPrefabGuids.Contains(prefabGuid);
        }

        private void SetPrefabSelected(string prefabGuid, bool isSelected)
        {
            if (isSelected)
            {
                if (!m_selectedPrefabGuids.Contains(prefabGuid))
                {
                    m_selectedPrefabGuids.Add(prefabGuid);
                }

                return;
            }

            m_selectedPrefabGuids.Remove(prefabGuid);
        }

        private int GetSelectedPrefabCount()
        {
            var count = 0;
            for (var index = 0; index < m_prefabs.Count; index++)
            {
                if (IsPrefabSelected(m_prefabs[index].Guid))
                {
                    count++;
                }
            }

            return count;
        }

        private void RemoveMissingPrefabSelections()
        {
            for (var index = m_selectedPrefabGuids.Count - 1; index >= 0; index--)
            {
                var prefabGuid = m_selectedPrefabGuids[index];
                var containsPrefab = false;
                for (var prefabIndex = 0; prefabIndex < m_prefabs.Count; prefabIndex++)
                {
                    if (m_prefabs[prefabIndex].Guid == prefabGuid)
                    {
                        containsPrefab = true;
                        break;
                    }
                }

                if (!containsPrefab)
                {
                    m_selectedPrefabGuids.RemoveAt(index);
                }
            }
        }

        private static string GetLocation(string path)
        {
            const string root = "Assets/Res/";
            return path.StartsWith(root, StringComparison.Ordinal) && path.EndsWith(".prefab", StringComparison.Ordinal)
                ? path.Substring(root.Length, path.Length - root.Length - ".prefab".Length)
                : string.Empty;
        }

        private static string FolderField(string label, string path)
        {
            EditorGUILayout.BeginHorizontal();
            path = EditorGUILayout.TextField(label, path);
            if (GUILayout.Button("选择", GUILayout.Width(52)))
            {
                var initial = Directory.Exists(path) ? Path.GetFullPath(path) : Application.dataPath;
                var chosen = EditorUtility.OpenFolderPanel(label, initial, string.Empty);
                if (!string.IsNullOrEmpty(chosen))
                {
                    var assets = Path.GetFullPath(Application.dataPath);
                    var full = Path.GetFullPath(chosen);
                    if (!full.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(full, assets, StringComparison.OrdinalIgnoreCase))
                        EditorUtility.DisplayDialog("文件夹无效", "请选择当前项目 Assets 内的文件夹。", "确定");
                    else path = "Assets" + full.Substring(assets.Length).Replace('\\', '/');
                }
            }
            EditorGUILayout.EndHorizontal();
            return path;
        }

        private void SaveSettings()
        {
            if (m_settings != null) UIAuthoringSettings.Save();
        }
    }
}
