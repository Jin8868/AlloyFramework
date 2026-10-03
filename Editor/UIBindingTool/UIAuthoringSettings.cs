using System;
using System.Collections.Generic;
using System.IO;
using AlloyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace AlloyFramework.Editor
{
    /// <summary>
    /// 单个业务 UI 的编辑器生成配置，由 UIAuthoringDatabase 统一保存。
    /// </summary>
    [Serializable]
    public sealed class UIAuthoringSettings
    {
        [SerializeField] private string m_prefabGuid;
        [SerializeField] private string m_uiName;
        [SerializeField] private string m_scriptNamespace = "Game.UI";
        [SerializeField] private string m_viewFolder = "Assets/Scripts/Hotfix/UI/Views";
        [SerializeField] private string m_controllerFolder = "Assets/Scripts/Hotfix/UI/Controllers";
        [SerializeField] private bool m_hasGenerated;
        [SerializeField] private UILayer m_layer = UILayer.WINDOW;
        [SerializeField] private UILayoutMode m_layout = UILayoutMode.Window;
        [SerializeField] private UIBackgroundMode m_background = UIBackgroundMode.None;
        [SerializeField] private UIInputMode m_input = UIInputMode.Block;
        [SerializeField] private UICacheMode m_cache = UICacheMode.DestroyOnClose;
        [SerializeField] private UIOpenMode m_openMode = UIOpenMode.SingleRefresh;
        [SerializeField] private UINavigationMode m_navigation = UINavigationMode.None;
        [SerializeField] private bool m_pauseCovered;

        public string PrefabGuid => m_prefabGuid;
        public bool HasGenerated => m_hasGenerated;
        public string UIName { get => m_uiName; set => m_uiName = value; }
        public string ScriptNamespace { get => m_scriptNamespace; set => m_scriptNamespace = value; }
        public string ViewFolder { get => m_viewFolder; set => m_viewFolder = value; }
        public string ControllerFolder { get => m_controllerFolder; set => m_controllerFolder = value; }
        public UILayer Layer { get => m_layer; set => m_layer = value; }
        public UILayoutMode Layout { get => m_layout; set => m_layout = value; }
        public UIBackgroundMode Background { get => m_background; set => m_background = value; }
        public UIInputMode Input { get => m_input; set => m_input = value; }
        public UICacheMode Cache { get => m_cache; set => m_cache = value; }
        public UIOpenMode OpenMode { get => m_openMode; set => m_openMode = value; }
        public UINavigationMode Navigation { get => m_navigation; set => m_navigation = value; }
        public bool PauseCovered { get => m_pauseCovered; set => m_pauseCovered = value; }

        internal static UIAuthoringSettings Create(string prefabGuid, string prefabPath) =>
            new UIAuthoringSettings
            {
                m_prefabGuid = prefabGuid,
                m_uiName = MakeIdentifier(Path.GetFileNameWithoutExtension(prefabPath))
            };

        internal void MarkGenerated()
        {
            m_hasGenerated = true;
            SetDirty();
        }

        public static UIAuthoringSettings LoadOrCreate(string prefabPath) =>
            UIAuthoringDatabase.LoadOrCreate().GetOrCreate(prefabPath);

        public static UIAuthoringSettings TryLoad(string prefabGuid) =>
            UIAuthoringDatabase.LoadOrCreate().Find(prefabGuid);

        public static IReadOnlyList<UIAuthoringSettings> All =>
            UIAuthoringDatabase.LoadOrCreate().Items;

        internal static void SetDirty() =>
            EditorUtility.SetDirty(UIAuthoringDatabase.LoadOrCreate());

        internal static void Save()
        {
            SetDirty();
            AssetDatabase.SaveAssets();
        }

        private static string MakeIdentifier(string value)
        {
            var chars = value.ToCharArray();
            for (var index = 0; index < chars.Length; index++)
                if (!char.IsLetterOrDigit(chars[index]) && chars[index] != '_') chars[index] = '_';
            var result = new string(chars);
            return result.Length == 0 || char.IsDigit(result[0]) ? "UI_" + result : result;
        }
    }
}
