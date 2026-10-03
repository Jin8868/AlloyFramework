using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AlloyFramework.Editor
{
    /// <summary>
    /// 所有业务 UI 共用的编辑器生成配置数据库。
    /// </summary>
    public sealed class UIAuthoringDatabase : ScriptableObject
    {
        public const string SettingsFolder = "Assets/Editor/UIEditor/UIAuthoring";
        public const string AssetPath = SettingsFolder + "/UIAuthoringSettings.asset";

        [SerializeField] private List<UIAuthoringSettings> m_items =
            new List<UIAuthoringSettings>();

        public IReadOnlyList<UIAuthoringSettings> Items => m_items;

        public UIAuthoringSettings Find(string prefabGuid)
        {
            if (string.IsNullOrEmpty(prefabGuid)) return null;
            foreach (var item in m_items)
                if (item != null && string.Equals(item.PrefabGuid, prefabGuid,
                        StringComparison.Ordinal))
                    return item;
            return null;
        }

        public UIAuthoringSettings GetOrCreate(string prefabPath)
        {
            var guid = AssetDatabase.AssetPathToGUID(prefabPath);
            if (string.IsNullOrEmpty(guid))
                throw new ArgumentException("请选择项目中的预制体资源。", nameof(prefabPath));

            var settings = Find(guid);
            if (settings != null) return settings;

            settings = UIAuthoringSettings.Create(guid, prefabPath);
            m_items.Add(settings);
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
            return settings;
        }

        public static UIAuthoringDatabase LoadOrCreate()
        {
            var database = AssetDatabase.LoadAssetAtPath<UIAuthoringDatabase>(AssetPath);
            if (database != null) return database;

            EnsureSettingsFolder();
            database = CreateInstance<UIAuthoringDatabase>();
            AssetDatabase.CreateAsset(database, AssetPath);
            AssetDatabase.SaveAssets();
            return database;
        }

        private static void EnsureSettingsFolder()
        {
            var parent = "Assets";
            foreach (var segment in new[] { "Editor", "AlloyFramework", "UIAuthoring" })
            {
                var next = parent + "/" + segment;
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(parent, segment);
                parent = next;
            }
        }
    }
}
